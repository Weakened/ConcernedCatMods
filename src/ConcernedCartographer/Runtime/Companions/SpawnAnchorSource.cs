using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Where "home" is for this character: the claimed bed if there is
/// one, otherwise the world's start location.
///
/// The resolution order matters more than it looks. <c>GetDeathPoint</c>,
/// <c>GetLogoutPoint</c> and <c>GetHomePoint</c> all exist and all look
/// tempting; none of them is home. They are places a player passed through,
/// and anchoring to one would drag the companion across the map behind an
/// ordinary journey. Only a claimed bed and the world's own starting point
/// are stable enough to be somebody's address.
///
/// The start location's lookup name is <b>data, not API</b> — the audit
/// confirmed the <c>StartTemple</c> type does not exist in this build — so it
/// is resolved by asking the live <c>ZoneSystem</c> and, failing that, by
/// enumerating its own location table. When neither works this reports no
/// anchor and the caller defers, which costs a player nothing: the compass
/// simply has not appeared yet.</summary>
internal sealed class SpawnAnchorSource : IAnchorSource
{
    /// <summary>Names to try first. These are candidates to look <i>up</i>,
    /// never positions to assume: a name that is not in this world's location
    /// table simply fails and the next one is tried.</summary>
    private static readonly string[] StartLocationCandidates =
    {
        "StartTemple",
        "Eikthyrnir",
    };

    private readonly ManualLogSource _log;
    private readonly RateLimitedLog _rateLimited;

    private string? _resolvedStartLocationName;
    private bool _startLocationSearched;
    private bool _loggedNoStartLocation;

    public SpawnAnchorSource(ManualLogSource log)
    {
        _log = log;
        _rateLimited = new RateLimitedLog(log, 60f);
    }

    /// <summary>The start-location name this world actually answered to, or
    /// null. Reported by the companion console tool so the pending evidence
    /// row can be closed with an observation instead of a guess.</summary>
    public string? ResolvedStartLocationName => _resolvedStartLocationName;

    /// <summary>The whole resolution in one call: bed if claimed, else the
    /// world's start. Kept for callers that do not care which they got.</summary>
    public bool TryGetAnchor(out CompanionAnchor anchor)
    {
        if (TryGetClaimedBed(out anchor))
        {
            return true;
        }

        return TryGetDefaultSpawn(out anchor);
    }

    /// <summary>What the PROFILE records as a claimed bed.
    ///
    /// Deliberately separate from the fallback, because the two answers need
    /// different treatment: this one records a position, not a bed, and
    /// survives the bed being destroyed. Whether the bed is still standing is
    /// <see cref="BedValidityProbe"/>'s question, and it can only be asked once
    /// this call has said where to look.</summary>
    public bool TryGetClaimedBed(out CompanionAnchor anchor)
    {
        anchor = CompanionAnchor.None;

        try
        {
            if (Game.instance == null)
            {
                return false;
            }

            PlayerProfile profile = Game.instance.GetPlayerProfile();
            if (profile == null || !profile.HaveCustomSpawnPoint())
            {
                return false;
            }

            Vector3 bed = profile.GetCustomSpawnPoint();
            if (!IsUsable(bed))
            {
                return false;
            }

            anchor = new CompanionAnchor(
                AnchorKind.ClaimedBed, new WorldPoint(bed.x, bed.y, bed.z));
            return true;
        }
        catch (Exception exception)
        {
            _rateLimited.Warning(
                "companion-anchor-bed",
                $"Could not read this character's claimed bed this session: {SafeLogText.Describe(exception)}");
            return false;
        }
    }

    /// <summary>The world's own starting point — the fallback for a character
    /// with no bed, and the only place a companion moves to when a bed is
    /// confirmed gone.</summary>
    public bool TryGetDefaultSpawn(out CompanionAnchor anchor)
    {
        anchor = CompanionAnchor.None;

        try
        {
            if (!TryGetStartLocation(out Vector3 start))
            {
                return false;
            }

            anchor = new CompanionAnchor(
                AnchorKind.DefaultSpawn, new WorldPoint(start.x, start.y, start.z));
            return true;
        }
        catch (Exception exception)
        {
            _rateLimited.Warning(
                "companion-anchor-start",
                $"Could not work out this world's starting point: {SafeLogText.Describe(exception)}");
            return false;
        }
    }

    private bool TryGetStartLocation(out Vector3 position)
    {
        position = Vector3.zero;

        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null)
        {
            return false;
        }

        if (_resolvedStartLocationName != null)
        {
            return zones.GetLocationIcon(_resolvedStartLocationName, out position) && IsUsable(position);
        }

        foreach (string candidate in StartLocationCandidates)
        {
            if (zones.GetLocationIcon(candidate, out position) && IsUsable(position))
            {
                _resolvedStartLocationName = candidate;
                _log.LogInfo($"Companion home point resolved from the world's \"{candidate}\" location.");
                return true;
            }
        }

        // The named candidates are not a list this build has to honour, so
        // when none of them answers the world is asked what locations it
        // actually has. This is an enumeration of live data, not a guess.
        if (!_startLocationSearched)
        {
            _startLocationSearched = true;
            string? discovered = SearchLocationTableForStart(zones);
            if (discovered != null && zones.GetLocationIcon(discovered, out position) && IsUsable(position))
            {
                _resolvedStartLocationName = discovered;
                _log.LogInfo($"Companion home point resolved from the world's \"{discovered}\" location.");
                return true;
            }
        }

        if (!_loggedNoStartLocation)
        {
            _loggedNoStartLocation = true;
            _log.LogInfo(
                "No claimed bed and no start location this build recognises, so the companion has no " +
                "home point yet. Claim a bed and it will settle there. Nothing is locked meanwhile.");
        }

        return false;
    }

    /// <summary>Walks <c>ZoneSystem</c>'s own location table looking for the
    /// start location. Entirely reflective and entirely optional: any shape
    /// this does not recognise returns null and the caller defers.</summary>
    private string? SearchLocationTableForStart(ZoneSystem zones)
    {
        try
        {
            const BindingFlags Instance =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            object? table =
                zones.GetType().GetField("m_locations", Instance)?.GetValue(zones);
            if (table is not IEnumerable entries)
            {
                return null;
            }

            foreach (object? entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                FieldInfo? nameField = entry.GetType().GetField("m_prefabName", Instance);
                if (nameField?.GetValue(entry) is not string name || name.Length == 0)
                {
                    continue;
                }

                // Valheim's starting location has always been named for the
                // temple the player wakes at. Matching on the substring rather
                // than an exact spelling survives a rename; matching on
                // nothing at all would be a guess.
                if (name.IndexOf("StartTemple", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return name;
                }
            }

            return null;
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                "Could not read this build's location table while looking for the world start point; " +
                $"the companion will wait for a claimed bed instead: {SafeLogText.Brief(exception)}");
            return null;
        }
    }

    private static bool IsUsable(Vector3 point)
    {
        return !(float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z))
            && point.sqrMagnitude > 0.0001f;
    }
}
