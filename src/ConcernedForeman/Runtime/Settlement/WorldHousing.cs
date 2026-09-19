using System;
using System.Collections.Generic;
using System.Globalization;
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
/// <item>ownership → <c>ZDOVars.s_owner</c> against
/// <c>Game.instance.GetPlayerProfile().GetPlayerID()</c>, plus vanilla's own
/// public <c>Bed.IsCurrent()</c>. See <see cref="BedClaim"/> for why that has to
/// be three-way.</item>
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
/// whoever interacted. `Bed.IsCurrent()` only compares two positions.</summary>
internal sealed class WorldHousing
{
    /// <summary>How far above and below the settlement circle to look.
    ///
    /// The designation is a <b>circle on the ground</b> and a bed can be
    /// upstairs or in a cellar, so the collection radius is the diagonal of that
    /// circle and this height — <c>sqrt(r² + v²)</c> — and the designation's own
    /// horizontal test then decides. Widening the sphere by a flat
    /// <c>r + v</c> instead would reach this far <i>sideways</i> too, and sweep
    /// in a neighbour's longhouse.</summary>
    private const float VerticalReachMetres = 32f;

    /// <summary>A hard cap on how many beds one survey will probe.
    ///
    /// <c>Cover.GetCoverForPoint</c> is a sphere cast plus a ring of rays — this
    /// product's own building-diagnostics audit (§2.2) records that it is not
    /// free — so this bounds the per-bed measurement at 64 per command.
    /// <see cref="MaxPiecesExamined"/> bounds the other half.</summary>
    private const int MaxBeds = 64;

    /// <summary>A cap on the walk that finds those beds.
    ///
    /// <c>Piece.GetAllPiecesInRadius</c> hands back every piece a player has
    /// built inside the query sphere, and this asks each one whether it is a
    /// bed. Both halves grow with the size of somebody's base, so capping only
    /// the probe would have left the survey's real cost unbounded while the
    /// comment above claimed otherwise. Reaching this is reported the same way
    /// as reaching the bed cap: the count becomes a floor and the readout says
    /// so.</summary>
    private const int MaxPiecesExamined = 4000;

    private readonly WorkerSitePolicy _sitePolicy;
    private readonly Action<string>? _log;

    internal WorldHousing(WorkerSitePolicy sitePolicy, Action<string>? log = null)
    {
        _sitePolicy = sitePolicy ?? throw new ArgumentNullException(nameof(sitePolicy));
        _log = log;
    }

    /// <summary>What the settlement can house right now.
    ///
    /// The area is <b>passed in</b> rather than looked up again. Its caller has
    /// already opened the register and established that a settlement is marked,
    /// and re-deriving it here gave a second, silent way to fail that reported
    /// "your settlement houses nobody" when the truth was "I could not work out
    /// which settlement you meant".</summary>
    internal HousingCapacity Measure(Designation area)
    {
        if (area == null || !area.IsArea)
        {
            return HousingCapacity.NotSurveyed;
        }

        Vector3 centre = ToVector3(area.Centre);
        if (!TryFindBeds(area, centre, out List<FoundBed> beds, out bool truncated))
        {
            return HousingCapacity.NotSurveyed;
        }

        var facts = new List<HousingFacts>(beds.Count);
        foreach (FoundBed bed in beds)
        {
            facts.Add(Measure(bed));
        }

        return HousingCapacity.Measure(facts, truncated, !IsGroundFullyLoaded(area, centre));
    }

    /// <summary>One bed inside the settlement, and the position it was found at.
    ///
    /// The position is carried rather than re-read because
    /// <c>bed.transform.position</c> is a native interop call and the survey
    /// wanted it four times per bed — to filter, to name the place, to ask about
    /// the fire, and to re-check containment.
    ///
    /// <b>"Inside the settlement" is part of the type, not a hope.</b> This is
    /// private and <see cref="TryFindBeds"/> is its only constructor, called
    /// only after the designation's own <c>Contains</c> has passed — which is
    /// what lets <see cref="Measure(FoundBed)"/> report
    /// <c>insideSettlement: true</c> without paying for the test twice. Anything
    /// that stops filtering there has to stop making these, so the two cannot
    /// drift apart silently.</summary>
    private readonly struct FoundBed
    {
        public FoundBed(Bed bed, Vector3 at)
        {
            Bed = bed;
            At = at;
        }

        public Bed Bed { get; }

        public Vector3 At { get; }
    }

    private HousingFacts Measure(FoundBed found)
    {
        Bed bed = found.Bed;
        string key;
        try
        {
            key = KeyOf(found.At);
        }
        catch (Exception)
        {
            // Even naming the bed failed. A key is still owed, because a fact
            // with no key is a fact that cannot be read out.
            key = "a bed";
        }

        try
        {
            ZNetView? view = bed.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                // Present in the scene and not yet a live object: not a bed that
                // failed, a bed that could not be asked about.
                return HousingFacts.Unmeasured(key);
            }

            Vector3 spawn = bed.GetSpawnPoint();
            Cover.GetCoverForPoint(spawn, out float cover, out bool underRoof);

            bool warm = EffectArea.IsPointInsideArea(found.At, EffectArea.Type.Heat) != null;

            // Inside by construction: TryFindBeds applied the designation's own
            // containment test during collection and kept only beds that passed,
            // so asking again here would be the same sqrt for the same answer.
            // HousingRules still owns the rule, for any caller that does not
            // filter first.
            return new HousingFacts(key, true, true, underRoof, cover, warm, ClaimOf(bed, view));
        }
        catch (Exception exception)
        {
            Report("a bed could not be checked (" + exception.GetType().Name + ")");
            return HousingFacts.Unmeasured(key);
        }
    }

    /// <summary>Whose bed it is, three ways.
    ///
    /// <c>Bed.IsMine()</c> and <c>Bed.GetOwner()</c> are <b>private</b> in the
    /// stock assembly, so this does what they do rather than calling them —
    /// reading <c>ZDOVars.s_owner</c> and comparing it to the local profile's
    /// player id, both public — and reaches for vanilla's own public
    /// <c>IsCurrent()</c> only for the one comparison it alone can make.
    /// Depending on a publicized private would put a shipped product at the
    /// mercy of how the build machine was set up.</summary>
    private static BedClaim ClaimOf(Bed bed, ZNetView view)
    {
        long owner = view.GetZDO().GetLong(ZDOVars.s_owner, 0L);
        if (owner == 0L)
        {
            return BedClaim.Unclaimed;
        }

        Game? game = Game.instance;
        PlayerProfile? profile = game == null ? null : game.GetPlayerProfile();
        if (profile == null)
        {
            // Somebody owns it and there is no profile to compare against. The
            // conservative answer is that it is not ours to offer.
            return BedClaim.SomebodyElses;
        }

        if (profile.GetPlayerID() != owner)
        {
            return BedClaim.SomebodyElses;
        }

        return bed.IsCurrent() ? BedClaim.YoursAndCurrent : BedClaim.YoursButNotCurrent;
    }

    /// <summary>The beds inside the settlement, bounded twice.
    ///
    /// Found through <c>Piece.GetAllPiecesInRadius</c> rather than
    /// <c>FindObjectsByType&lt;Bed&gt;</c>, which is the pattern the Steward's
    /// fuel survey already established, and which buys two things beyond not
    /// scanning the scene:
    ///
    /// <list type="bullet">
    /// <item>vanilla's own call <b>skips the ghost layer</b>
    /// (<c>s_allPiece.gameObject.layer != s_ghostLayer</c>). The hammer's
    /// placement preview is a real <c>Bed</c> component on a live GameObject
    /// whose <c>ZNetView</c> has been destroyed, and a scene-wide type query
    /// finds it — so opening the build menu used to add a phantom bed nobody
    /// could check;</item>
    /// <item>a piece is what a player <i>built</i>, which is what a settlement is
    /// made of.</item>
    /// </list>
    ///
    /// Returns false only when the query itself failed, which is a different
    /// answer from finding nothing.</summary>
    private bool TryFindBeds(
        Designation area, Vector3 centre, out List<FoundBed> beds, out bool truncated)
    {
        beds = new List<FoundBed>();
        truncated = false;

        // A local rather than a reused field. Clearing a field keeps its
        // capacity, so one survey of a large base would park its high-water-mark
        // array on a runtime that lives as long as the plugin — and if anything
        // in the loop below threw, the field would go on holding a strong
        // reference to every piece in the sphere until somebody happened to run
        // the command again. This is a command a player types, not a per-frame
        // loop, so the collector is the right owner.
        var pieces = new List<Piece>();
        try
        {
            float queryRadius = (float)Math.Sqrt(
                ((double)area.Radius * area.Radius) +
                ((double)VerticalReachMetres * VerticalReachMetres));
            Piece.GetAllPiecesInRadius(centre, queryRadius, pieces);
        }
        catch (Exception exception)
        {
            // Not "no beds". Nobody looked, and the readout has to say so.
            Report("the settlement's pieces could not be listed, so housing was not measured (" +
                exception.GetType().Name + ")");
            return false;
        }

        int examined = 0;
        foreach (Piece piece in pieces)
        {
            if (examined >= MaxPiecesExamined)
            {
                truncated = true;
                break;
            }

            if (piece == null)
            {
                continue;
            }

            examined++;

            // Bed.Awake reads its own ZNetView off its own GameObject, so the
            // two are always on the same object. Looking in children would find
            // a bed belonging to a different piece. TryGetComponent is the
            // non-allocating form, and this runs once per built piece in range.
            if (!piece.TryGetComponent(out Bed bed) || bed == null)
            {
                continue;
            }

            Vector3 at = bed.transform.position;
            if (!area.Contains(ToSitePoint(at)))
            {
                // Filtered here rather than judged later: a neighbour's bed is
                // not advice, it is noise, and counting it against the budget
                // would let somebody else's longhouse push the player's own beds
                // out of their own survey.
                continue;
            }

            if (beds.Count >= MaxBeds)
            {
                // Tested before the add, so exactly MaxBeds beds is a complete
                // survey rather than one that claims it skipped something.
                truncated = true;
                break;
            }

            beds.Add(new FoundBed(bed, at));
        }

        return true;
    }

    /// <summary>Whether the whole settlement's ground is loaded.
    ///
    /// This is the only way the player can be told about beds in an unloaded
    /// zone, and it has to be asked at the <i>area</i>, not the bed: an object in
    /// an unloaded zone is not in <c>s_allPieces</c> at all, so it produces a
    /// shorter list rather than an unmeasurable entry. A per-bed "could not be
    /// checked" can never fire for it.
    ///
    /// Five points — the centre and the four compass edges — because a zone is
    /// 64 m and a settlement is usually smaller than one; the answer is
    /// best-effort and is reported as a caveat, never as a refusal.</summary>
    private bool IsGroundFullyLoaded(Designation area, Vector3 centre)
    {
        try
        {
            if (!_sitePolicy.IsInLoadedGround(centre))
            {
                return false;
            }

            float r = area.Radius;
            return _sitePolicy.IsInLoadedGround(centre + new Vector3(r, 0f, 0f))
                && _sitePolicy.IsInLoadedGround(centre + new Vector3(-r, 0f, 0f))
                && _sitePolicy.IsInLoadedGround(centre + new Vector3(0f, 0f, r))
                && _sitePolicy.IsInLoadedGround(centre + new Vector3(0f, 0f, -r));
        }
        catch (Exception)
        {
            // Could not establish it. Saying the ground is incomplete is the
            // answer that qualifies the count rather than overstating it.
            return false;
        }
    }

    /// <summary>A place the player can walk to.
    ///
    /// Deliberately <b>not</b> the bed's uid. A <c>ZDOID</c> is reassigned in
    /// load order on every world load — the reason this product keeps an
    /// identity epoch beside every persisted key — so a number printed here
    /// names a different object after a reload, and it is a number the player
    /// cannot find in the world either way. Coordinates are at least somewhere
    /// to stand.</summary>
    private static string KeyOf(Vector3 at)
    {
        return "the bed at " +
            Mathf.RoundToInt(at.x).ToString(CultureInfo.InvariantCulture) + ", " +
            Mathf.RoundToInt(at.z).ToString(CultureInfo.InvariantCulture);
    }

    private static SitePoint ToSitePoint(Vector3 point) => new SitePoint(point.x, point.y, point.z);

    private static Vector3 ToVector3(SitePoint point) => new Vector3(point.X, point.Y, point.Z);

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
