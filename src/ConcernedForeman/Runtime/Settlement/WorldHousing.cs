using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Housing;
using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Measures a settlement's housing against the real world
/// (CF-SET-009, #286).
///
/// <b>Vanilla's own rules, called rather than reimplemented.</b> #286 asks for
/// capacity measured from the finished building rather than asserted by a
/// blueprint, and `Bed.Interact` in the installed 1.0.14 build already contains
/// the definition of a bed somebody can sleep in:
///
/// <list type="bullet">
/// <item><c>CheckExposure</c> → <c>Cover.GetCoverForPoint(bed.GetSpawnPoint(),
/// out cover, out underRoof)</c>, refusing without a roof
/// (<c>$msg_bedneedroof</c>) and below 80 % cover
/// (<c>$msg_bedtooexposed</c>);</item>
/// <item><c>CheckFire</c> → <c>EffectArea.IsPointInsideArea(bed.transform.position,
/// EffectArea.Type.Heat)</c> (<c>$msg_bednofire</c>);</item>
/// <item>ownership → <c>ZDOVars.s_owner</c>.</item>
/// </list>
///
/// Both of the first two are <b>public, static and take a point</b>, so they can
/// be asked about a bed rather than about the player standing next to it. That
/// is what makes this a measurement instead of a model: a reimplementation that
/// agreed with the game today would disagree with it the first time the game
/// changed, and a player would be told their house was fine when the game would
/// not let them sleep in it.
///
/// <b>It reads and nothing else.</b> No bed is claimed, no owner is written, no
/// piece is touched. `Bed.Interact` is never called — it would claim the bed for
/// whoever interacted.</summary>
internal sealed class WorldHousing
{
    /// <summary>How far above and below the settlement circle to look. The
    /// designation is a circle on the ground and a bed can be upstairs, so the
    /// query is widened vertically and the horizontal containment test the
    /// designation already owns decides. Over-collect and filter exactly.
    /// </summary>
    private const float VerticalReachMetres = 32f;

    /// <summary>A hard cap on one survey, so its cost cannot grow with the size
    /// of somebody's base.</summary>
    private const int MaxBeds = 64;

    private readonly Func<Designation?> _settlementArea;
    private readonly Action<string>? _log;

    /// <param name="settlementArea">The marked settlement, or null when none is
    /// marked. A function rather than a value: a measurement is only as good as
    /// the moment it is asked for.</param>
    internal WorldHousing(Func<Designation?> settlementArea, Action<string>? log = null)
    {
        _settlementArea = settlementArea ?? throw new ArgumentNullException(nameof(settlementArea));
        _log = log;
    }

    /// <summary>What the settlement can house right now.
    ///
    /// A bed that cannot be measured comes back as
    /// <see cref="HousingFacts.Unmeasured"/> rather than being dropped, because
    /// "there is a bed here I could not check" and "there is no bed here" are
    /// different answers and only one of them is a reason to build.</summary>
    internal HousingCapacity Measure()
    {
        Designation? area = _settlementArea();
        if (area == null)
        {
            return HousingCapacity.None;
        }

        var facts = new List<HousingFacts>();
        foreach (Bed bed in FindBeds(area))
        {
            facts.Add(Measure(bed, area));
            if (facts.Count >= MaxBeds)
            {
                Report("more than " + MaxBeds + " beds are in the settlement; only the first were checked");
                break;
            }
        }

        return HousingCapacity.Measure(facts);
    }

    private HousingFacts Measure(Bed bed, Designation area)
    {
        string key = KeyOf(bed);
        try
        {
            ZNetView? view = bed.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                // Present in the scene and not yet a live object: not a bed
                // that failed, a bed that could not be asked about.
                return HousingFacts.Unmeasured(key);
            }

            Vector3 spawn = bed.GetSpawnPoint();
            Cover.GetCoverForPoint(spawn, out float cover, out bool underRoof);

            bool warm = EffectArea.IsPointInsideArea(bed.transform.position, EffectArea.Type.Heat) != null;
            bool claimed = view.GetZDO().GetLong(ZDOVars.s_owner, 0L) != 0L;
            bool inside = area.Contains(ToSitePoint(bed.transform.position));

            return new HousingFacts(key, true, inside, underRoof, cover, warm, claimed);
        }
        catch (Exception exception)
        {
            Report("a bed could not be checked (" + exception.GetType().Name + ")");
            return HousingFacts.Unmeasured(key);
        }
    }

    /// <summary>Beds near the settlement. Deliberately not every bed in the
    /// world: the designation's own radius bounds it, widened vertically so a
    /// bed upstairs is found and then filtered by the real horizontal test.
    /// </summary>
    private static IEnumerable<Bed> FindBeds(Designation area)
    {
        var found = new List<Bed>();
        Bed[] beds;
        try
        {
            beds = UnityEngine.Object.FindObjectsByType<Bed>(FindObjectsSortMode.None);
        }
        catch (Exception)
        {
            return found;
        }

        float reach = area.Radius + VerticalReachMetres;
        Vector3 centre = new Vector3(area.Centre.X, area.Centre.Y, area.Centre.Z);
        foreach (Bed bed in beds)
        {
            if (bed != null && Vector3.Distance(bed.transform.position, centre) <= reach)
            {
                found.Add(bed);
            }
        }

        return found;
    }

    /// <summary>Identity for one world load. A bed has no name and its uid is
    /// reassigned on load, which is why nothing here is persisted from it.
    /// </summary>
    private static string KeyOf(Bed bed)
    {
        try
        {
            ZNetView? view = bed.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                return "bed " + view.GetZDO().m_uid;
            }
        }
        catch (Exception)
        {
            // Fall through to the position, which is at least recognisable.
        }

        Vector3 at = bed.transform.position;
        return "bed at " + at.x.ToString("0") + "," + at.z.ToString("0");
    }

    private static SitePoint ToSitePoint(Vector3 point) => new SitePoint(point.x, point.y, point.z);

    private void Report(string what)
    {
        try
        {
            _log?.Invoke(what);
        }
        catch (Exception)
        {
            // A broken log sink must not cost a survey.
        }
    }
}
