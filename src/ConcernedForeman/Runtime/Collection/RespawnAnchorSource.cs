using System;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

internal enum RespawnAnchorKind
{
    Unspecified = 0,

    /// <summary>The claimed bed this character respawns at.</summary>
    ClaimedBed = 1,

    /// <summary>The world's start location.</summary>
    WorldStart = 2,
}

internal readonly struct RespawnAnchor
{
    public RespawnAnchor(RespawnAnchorKind kind, SitePoint point, bool verified)
    {
        Kind = kind;
        Point = point;
        Verified = verified;
    }

    public RespawnAnchorKind Kind { get; }

    public SitePoint Point { get; }

    /// <summary>For a bed: the bed was seen standing and current. False when its
    /// ground is not loaded, so it could not be looked at.</summary>
    public bool Verified { get; }

    /// <summary>The name the scope revision is computed over.</summary>
    public string RevisionName => Kind == RespawnAnchorKind.ClaimedBed ? "bed" : "start";

    public string Describe() =>
        Kind == RespawnAnchorKind.ClaimedBed
            ? (Verified ? "your bed" : "your bed (not loaded, so not checked yet)")
            : "the world start";
}

/// <summary>The latest valid respawn anchor (GATHER-02, D11): the claimed bed's
/// spawn point, else the world start. Never the player's moving position.
///
/// The idea is Cartographer's <c>SpawnAnchorSource</c>, copied rather than
/// referenced (D2). "Valid" follows vanilla's own respawn rule in 1.0.12
/// (<c>Game.FindSpawnPoint</c>): a custom spawn point is honoured only while a
/// bed the character owns is current there (<c>Bed.IsCurrent</c>); when the
/// ground is loaded and no such bed stands, vanilla falls back to the start
/// location, and so does this. The start location's name is the game's own
/// field, <c>Game.m_StartLocation</c>, not a guess.</summary>
internal static class RespawnAnchorSource
{
    private static readonly Collider[] Buffer = new Collider[64];

    internal static bool TryGetLatest(out RespawnAnchor anchor, out string failure)
    {
        anchor = default;
        try
        {
            Game game = Game.instance;
            ZoneSystem zones = ZoneSystem.instance;
            if (game == null || zones == null)
            {
                failure = "no world is loaded";
                return false;
            }

            PlayerProfile profile = game.GetPlayerProfile();
            if (profile != null && profile.HaveCustomSpawnPoint())
            {
                Vector3 bed = profile.GetCustomSpawnPoint();
                if (IsUsable(bed))
                {
                    if (!zones.IsZoneLoaded(bed))
                    {
                        anchor = new RespawnAnchor(RespawnAnchorKind.ClaimedBed, NaturalSourceClassifier.ToSitePoint(bed), verified: false);
                        failure = string.Empty;
                        return true;
                    }

                    if (IsCurrentBedAt(bed))
                    {
                        anchor = new RespawnAnchor(RespawnAnchorKind.ClaimedBed, NaturalSourceClassifier.ToSitePoint(bed), verified: true);
                        failure = string.Empty;
                        return true;
                    }

                    // Loaded, and no current bed of yours stands there: vanilla
                    // would respawn you at the start, so that is the anchor.
                }
            }

            if (zones.GetLocationIcon(game.m_StartLocation, out Vector3 start) && IsUsable(start))
            {
                anchor = new RespawnAnchor(RespawnAnchorKind.WorldStart, NaturalSourceClassifier.ToSitePoint(start), verified: true);
                failure = string.Empty;
                return true;
            }

            failure = "neither a claimed bed nor the world start could be found";
            return false;
        }
        catch (Exception exception)
        {
            failure = "the respawn point could not be read (" + exception.GetType().Name + ")";
            return false;
        }
    }

    private static bool IsCurrentBedAt(Vector3 point)
    {
        int count = Physics.OverlapSphereNonAlloc(point, 3f, Buffer, ~0, QueryTriggerInteraction.Collide);
        for (int index = 0; index < count; index++)
        {
            Collider collider = Buffer[index];
            Bed? bed = collider != null ? collider.GetComponentInParent<Bed>() : null;
            if (bed != null && bed.IsCurrent())
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUsable(Vector3 point) =>
        !(float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)) && point.sqrMagnitude > 0.0001f;
}
