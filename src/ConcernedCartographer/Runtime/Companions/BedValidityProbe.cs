using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Asks whether the bed behind a recorded spawn point is still there.
///
/// This exists because <c>PlayerProfile.HaveCustomSpawnPoint()</c> reports what
/// the <i>profile</i> remembers, not what the <i>world</i> contains. Destroy the
/// bed and the recorded position stays behind, so a companion anchored on the
/// profile alone would go on living in an empty field where a bed used to be.
/// The independent review flagged the gap; this closes it.
///
/// The three-way answer matters more than the lookup. The world can only be
/// asked about ground that is loaded, so a player two biomes from home gets
/// <see cref="AnchorValidity.Unknown"/> — and Unknown deliberately keeps the
/// bed. Treating "I cannot see it" as "it is gone" would relocate the companion
/// to the world's starting point every time the player went travelling, which
/// is precisely the wandering the residency rule is built to prevent. Only
/// loaded ground with no bed in it returns <see cref="AnchorValidity.Gone"/>.</summary>
internal sealed class BedValidityProbe
{
    /// <summary>How far from the recorded point a bed still counts as "that
    /// bed". A spawn point is offset from the bed's own origin, and a replaced
    /// bed lands a little off the old one; generous enough for both, tight
    /// enough that a neighbour's bed across the hall is not mistaken for it.</summary>
    private const float SearchRadius = 3.5f;

    private const int MaxHits = 24;

    private readonly ManualLogSource _log;
    private readonly Collider[] _hits = new Collider[MaxHits];
    private bool _loggedUnavailable;

    public BedValidityProbe(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>True once this build turned out not to support the lookup at
    /// all. Reported by the console tool; the residency rule then simply never
    /// sees <see cref="AnchorValidity.Gone"/> and the companion keeps whatever
    /// home it had.</summary>
    public bool LookupUnavailable { get; private set; }

    public AnchorValidity Check(Vector3 recordedSpawnPoint)
    {
        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.IsZoneLoaded(recordedSpawnPoint))
            {
                return AnchorValidity.Unknown;
            }

            int count = Physics.OverlapSphereNonAlloc(
                recordedSpawnPoint, SearchRadius, _hits, ~0, QueryTriggerInteraction.Collide);

            for (int index = 0; index < count; index++)
            {
                Collider hit = _hits[index];
                if (hit == null)
                {
                    continue;
                }

                if (hit.GetComponentInParent<Bed>() != null)
                {
                    return AnchorValidity.Valid;
                }
            }

            // Loaded ground, nothing bed-shaped in it. The one answer that may
            // move a companion off a claimed bed.
            return AnchorValidity.Gone;
        }
        catch (Exception exception)
        {
            if (!_loggedUnavailable)
            {
                _loggedUnavailable = true;
                LookupUnavailable = true;
                _log.LogInfo(
                    "This build does not let the mod check whether a claimed bed still exists, so the " +
                    "companion will stay at the last home point it knew rather than guess. Nothing " +
                    $"about your tools or progress is affected: {SafeLogText.Brief(exception)}");
            }

            return AnchorValidity.Unknown;
        }
    }
}
