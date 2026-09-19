using System;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Answers "does somebody else own this ground" from the installed
/// game, or refuses because it could not tell.
///
/// Verified against the installed 1.0.12 <c>assembly_valheim.dll</c> (SHA256
/// <c>27a766a8…c393a84</c>, the same binary the worker spike audited):
///
/// <code>
/// public static bool CheckAccess(Vector3 point, float radius = 0f,
///                                bool flash = true, bool wardCheck = false)
/// </code>
///
/// With <c>wardCheck: true</c> it starts from "allowed" and denies only when an
/// <i>enabled</i> ward overlaps and <c>HaveLocalAccess()</c> is false — which is
/// exactly "somebody else's ward covers this". Overlap is
/// <c>Utils.DistanceXZ(ward, point) &lt; ward.m_radius + radius</c>, so passing
/// the designation's own radius asks the right question: do the two circles
/// meet. <c>flash: false</c> matters — a designation check must not make every
/// nearby shield flash.
///
/// <b>The part that forces a refusal.</b> <c>CheckAccess</c> reads a static list
/// that only contains wards whose objects are <i>loaded</i>. A ward on unloaded
/// ground is not absent, it is invisible — and answering "granted" from a list
/// that cannot see it is the silent skip the authority ADR forbids. So this
/// class refuses unless the ground around the designation is loaded; see
/// <see cref="LoadedMargin"/> for how far, and for what that number does and
/// does not rest on.</summary>
internal sealed class WorldDesignationSite : IDesignationSite
{
    /// <summary>How far past the designation's own extent the world has to be
    /// loaded before the ward answer means anything.
    ///
    /// Two caveats this constant used to state as fact. It is <b>our</b> number,
    /// not one read from the game: <c>ZoneSystem.m_zoneSize</c> is 64 in this
    /// build, but <c>ZoneSystem.GetZone</c> does not read that field — it
    /// divides by a hardcoded 64. And one zone exceeding a ward's 32 m reach
    /// holds for the ward radii this build ships; a prefab with a larger
    /// <c>m_radius</c> would reach past the margin. Prefab radii are asset data
    /// and cannot be read by decompiling the assembly, so that is an assumption
    /// rather than a verified fact.</summary>
    private const float LoadedMargin = 64f;

    /// <summary>Distance between the points the loaded check samples. A zone is
    /// 64 m, so points 32 m apart cannot straddle a zone without landing in it —
    /// no unloaded zone inside the sampled square can be stepped over.</summary>
    private const float SampleStep = 32f;

    public AreaAccess CheckAccess(SitePoint centre, float radius)
    {
        // The guard wraps the WHOLE answer, not only the ward call. This
        // interface's contract is that anything it cannot establish answers
        // Unavailable, and an escaping exception establishes nothing — wherever
        // it came from. IsZoneLoaded walks live zone state and is no more
        // exception-free than PrivateArea.CheckAccess is.
        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null)
            {
                // No zone system means no world. That is not "probably fine".
                return AreaAccess.Unavailable;
            }

            var point = new Vector3(centre.X, centre.Y, centre.Z);
            if (!IsSurroundingsLoaded(zones, point, radius))
            {
                return AreaAccess.Unavailable;
            }

            return PrivateArea.CheckAccess(point, radius, flash: false, wardCheck: true)
                ? AreaAccess.Granted
                : AreaAccess.Denied;
        }
        catch (Exception)
        {
            return AreaAccess.Unavailable;
        }
    }

    /// <summary>True when every zone the area could hide a ward in is loaded.
    ///
    /// Sampling world points rather than iterating zone ids keeps this on the
    /// one overload whose signature was verified, and the step is chosen so the
    /// sampling cannot skip a zone. It runs once when a player marks something,
    /// never on a tick.</summary>
    internal static bool IsSurroundingsLoaded(ZoneSystem zones, Vector3 centre, float radius)
    {
        float reach = radius + LoadedMargin;
        float min = -reach;

        for (float dx = min; dx <= reach; dx += SampleStep)
        {
            for (float dz = min; dz <= reach; dz += SampleStep)
            {
                if (!zones.IsZoneLoaded(new Vector3(centre.x + dx, centre.y, centre.z + dz)))
                {
                    return false;
                }
            }

            // The loop above can stop short of the far edge when the span is
            // not a whole number of steps, so the edge is always sampled.
            if (!zones.IsZoneLoaded(new Vector3(centre.x + dx, centre.y, centre.z + reach)))
            {
                return false;
            }
        }

        for (float dz = min; dz <= reach; dz += SampleStep)
        {
            if (!zones.IsZoneLoaded(new Vector3(centre.x + reach, centre.y, centre.z + dz)))
            {
                return false;
            }
        }

        return zones.IsZoneLoaded(new Vector3(centre.x + reach, centre.y, centre.z + reach));
    }
}
