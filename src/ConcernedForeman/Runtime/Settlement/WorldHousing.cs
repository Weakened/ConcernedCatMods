using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Housing;
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
/// whoever interacted. Every call in `Bed.IsCurrent()`'s own body is a read too,
/// though it is not merely a comparison: its IL is <c>IsMine() &amp;&amp;
/// Vector3.Distance(GetSpawnPoint(), GetCustomSpawnPoint()) &lt; 1f</c>, so it
/// re-reads the owner field and the profile. Two beds whose spawn points are
/// within a metre — bunks, or two beds pushed together — both answer true if the
/// player owns both. That is vanilla's own imprecision and this does not paper
/// over it.</summary>
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

    /// <summary>A cap on how many beds one survey will probe.
    ///
    /// <c>Cover.GetCoverForPoint</c> is a sphere cast plus a ring of rays — this
    /// product's own building-diagnostics audit (§2.2) records that it is not
    /// free — so this bounds the per-bed measurement at 64 per command.</summary>
    private const int MaxBeds = 64;

    /// <summary>A cap on how many <b>in-settlement</b> pieces the walk examines.
    ///
    /// Charged only after the designation's own containment test has passed. An
    /// earlier version charged every piece the query sphere returned, and that
    /// sphere reaches <c>sqrt(48² + 32²)</c> ≈ 58 m — so a neighbour's large
    /// build in the ring outside the circle could exhaust the budget before the
    /// walk reached one of the player's own beds, and the readout then said "No
    /// beds in the settlement, so it houses nobody" about a settlement full of
    /// them. Charging only what is inside also makes the answer stable: the
    /// in-circle set does not depend on <c>s_allPieces</c> ordering, so two
    /// surveys of an unchanged building agree.</summary>
    private const int MaxPiecesInSettlement = 4000;

    /// <summary>Whether the whole settlement's ground is loaded.
    ///
    /// A predicate rather than a policy object, because this needs exactly one
    /// answer and the knowledge belongs to whoever owns the designation's
    /// geometry. The first version asked <c>IsInLoadedGround</c> at five points —
    /// the centre and four compass edges — which for a 48 m settlement never
    /// samples the diagonal zones: zone (1,1) spans [32,96) and contains points
    /// 45 m from the centre, inside the circle and never probed.
    /// <c>WorldDesignationSite.IsSurroundingsLoaded</c> samples on a grid whose
    /// step is chosen so no zone inside the area can be stepped over, which is
    /// the same question asked properly.</summary>
    private readonly Func<Designation, bool> _isGroundFullyLoaded;

    private readonly Action<string>? _log;

    internal WorldHousing(Func<Designation, bool> isGroundFullyLoaded, Action<string>? log = null)
    {
        _isGroundFullyLoaded = isGroundFullyLoaded
            ?? throw new ArgumentNullException(nameof(isGroundFullyLoaded));
        _log = log;
    }

    /// <summary>Why a survey stopped short, so the log can say which and the
    /// readout is not left implying the other.</summary>
    private enum SurveyLimit
    {
        /// <summary>It finished.</summary>
        None,

        /// <summary>More beds inside the settlement than one command probes.
        /// </summary>
        TooManyBeds,

        /// <summary>More built pieces inside the settlement than one command
        /// walks.</summary>
        TooMuchToWalk,
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
        Vector3 centre = SitePoints.ToVector3(area.Centre);
        if (!TryFindBeds(area, centre, out List<FoundBed> beds, out SurveyLimit limit))
        {
            return HousingCapacity.NotSurveyed;
        }

        if (limit != SurveyLimit.None)
        {
            // Which cap fired reaches the log even though the readout says only
            // that one fired: a player reporting "my housing count is short"
            // otherwise leaves no way to tell a genuinely huge settlement from a
            // budget spent on something else.
            Report(limit == SurveyLimit.TooManyBeds
                ? "more than " + MaxBeds.ToString(CultureInfo.InvariantCulture) +
                  " beds are inside the settlement, so only the first were checked"
                : "more than " + MaxPiecesInSettlement.ToString(CultureInfo.InvariantCulture) +
                  " pieces are inside the settlement, so the search for beds stopped early");
        }

        var facts = new List<HousingFacts>(beds.Count);
        foreach (FoundBed bed in beds)
        {
            facts.Add(Measure(bed));
        }

        return HousingCapacity.Measure(
            facts,
            truncated: limit != SurveyLimit.None,
            groundIncomplete: !IsGroundFullyLoaded(area));
    }

    /// <summary>One bed inside the settlement, and the position it was found at.
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
        string key = KeyOf(found.At);

        try
        {
            ZNetView? view = bed.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                // Present in the scene and not yet a live object: not a bed that
                // failed, a bed that could not be asked about.
                return HousingFacts.Unmeasured(key);
            }

            if (!TryReadClaim(bed, view, out BedClaim claim))
            {
                return HousingFacts.Unmeasured(key);
            }

            Vector3 spawn = bed.GetSpawnPoint();
            Cover.GetCoverForPoint(spawn, out float cover, out bool underRoof);

            bool warm = EffectArea.IsPointInsideArea(found.At, EffectArea.Type.Heat) != null;

            return new HousingFacts(key, true, true, underRoof, cover, warm, claim);
        }
        catch (Exception exception)
        {
            Report("a bed could not be checked (" + exception.GetType().Name + ")");
            return HousingFacts.Unmeasured(key);
        }
    }

    /// <summary>Whose bed it is, three ways, or false when that cannot be told.
    ///
    /// <c>Bed.IsMine()</c> and <c>Bed.GetOwner()</c> are <b>private</b> in the
    /// stock assembly, so this does what they do rather than calling them —
    /// reading <c>ZDOVars.s_owner</c> and comparing it to the local profile's
    /// player id, both public — and reaches for vanilla's own public
    /// <c>IsCurrent()</c> only for the one comparison it alone can make.
    /// Depending on a publicized private would put a shipped product at the
    /// mercy of how the build machine was set up.
    ///
    /// <b>No profile means no answer.</b> An owned bed with no readable profile
    /// used to come back as somebody else's, which on a single-player world
    /// printed "somebody else already sleeps in it" for every bed the player had
    /// ever slept in — nothing in vanilla clears <c>s_owner</c>, so that is all
    /// of them. <c>NotMeasured</c> exists for exactly a question that could not
    /// be answered.</summary>
    private static bool TryReadClaim(Bed bed, ZNetView view, out BedClaim claim)
    {
        claim = BedClaim.Unclaimed;

        long owner = view.GetZDO().GetLong(ZDOVars.s_owner, 0L);
        if (owner == 0L)
        {
            return true;
        }

        Game? game = Game.instance;
        PlayerProfile? profile = game == null ? null : game.GetPlayerProfile();
        if (profile == null)
        {
            return false;
        }

        if (profile.GetPlayerID() != owner)
        {
            claim = BedClaim.SomebodyElses;
            return true;
        }

        claim = bed.IsCurrent() ? BedClaim.YoursAndCurrent : BedClaim.YoursButNotCurrent;
        return true;
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
    /// <b>Known limit, and it wants an in-game check.</b> A bed is recognised by
    /// asking the registered <c>Piece</c>'s own GameObject for a <c>Bed</c>.
    /// Every vanilla bed prefab carries both on one object, but a prefab from
    /// another mod that puts <c>Bed</c> on a child would be missed entirely
    /// rather than reported as unmeasurable — the one path here that can
    /// under-report housing without saying so. Widening to the children risks
    /// attributing a bed to a neighbouring piece, so this stays as it is until
    /// somebody has looked at the real prefabs in game.
    ///
    /// Returns false only when the query itself failed, which is a different
    /// answer from finding nothing.</summary>
    private bool TryFindBeds(
        Designation area, Vector3 centre, out List<FoundBed> beds, out SurveyLimit limit)
    {
        beds = new List<FoundBed>();
        limit = SurveyLimit.None;

        // A local rather than a reused field. Clearing a field keeps its
        // capacity, so one survey of a large base would park its high-water-mark
        // array on a runtime that lives as long as the plugin. This is a command
        // a player types, not a per-frame loop, so the collector is the right
        // owner.
        var pieces = new List<Piece>();

        // One try around the whole walk, not just the query. Reading `.transform`
        // on a piece destroyed between the query filling the list and this loop
        // dereferencing it throws MissingReferenceException, and a null check one
        // statement earlier does not prevent it. Escaping here bypassed every
        // failure state this class exists to produce: it surfaced as a raw
        // exception string from the command's blanket handler, with nothing
        // logged and every bed already collected discarded.
        try
        {
            float queryRadius = (float)Math.Sqrt(
                ((double)area.Radius * area.Radius) +
                ((double)VerticalReachMetres * VerticalReachMetres));
            Piece.GetAllPiecesInRadius(centre, queryRadius, pieces);

            int inSettlement = 0;
            foreach (Piece piece in pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                // Containment before the budget, and before the component
                // lookup. The piece and its bed share a GameObject, so the
                // piece's own position is the bed's.
                if (!area.Contains(SitePoints.ToSitePoint(piece.transform.position)))
                {
                    continue;
                }

                if (inSettlement >= MaxPiecesInSettlement)
                {
                    limit = SurveyLimit.TooMuchToWalk;
                    break;
                }

                inSettlement++;

                if (!piece.TryGetComponent(out Bed bed))
                {
                    continue;
                }

                if (beds.Count >= MaxBeds)
                {
                    // Tested before the add, so exactly MaxBeds beds is a
                    // complete survey rather than one claiming it skipped
                    // something.
                    limit = SurveyLimit.TooManyBeds;
                    break;
                }

                beds.Add(new FoundBed(bed, bed.transform.position));
            }
        }
        catch (Exception exception)
        {
            // Not "no beds". Nobody looked, and the readout has to say so.
            Report("the settlement's pieces could not be walked, so housing was not measured (" +
                exception.GetType().Name + ")");
            return false;
        }

        return true;
    }

    private bool IsGroundFullyLoaded(Designation area)
    {
        try
        {
            return _isGroundFullyLoaded(area);
        }
        catch (Exception)
        {
            // Could not establish it. Saying the ground is incomplete qualifies
            // the count rather than overstating it.
            return false;
        }
    }

    /// <summary>A place the player can walk to.
    ///
    /// Deliberately <b>not</b> the bed's uid. A <c>ZDOID</c> is reassigned in
    /// load order on every world load — the reason this product keeps an
    /// identity epoch beside every persisted key — so a number printed here
    /// names a different object after a reload, and it is a number the player
    /// cannot find in the world either way.
    ///
    /// The height is part of it. Without it, two beds on different floors of one
    /// house produced byte-identical lines — in the class whose 32 m vertical
    /// reach exists specifically to find the upstairs one, leaving the player
    /// unable to tell which floor the advice was about.</summary>
    private static string KeyOf(Vector3 at) =>
        "the bed at " +
        Mathf.RoundToInt(at.x).ToString(CultureInfo.InvariantCulture) + ", " +
        Mathf.RoundToInt(at.z).ToString(CultureInfo.InvariantCulture) + ", height " +
        Mathf.RoundToInt(at.y).ToString(CultureInfo.InvariantCulture);

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
