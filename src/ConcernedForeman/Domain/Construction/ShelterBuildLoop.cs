using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>What the loop is doing right now. For the panel, the status line and
/// the tests - never written to disk.</summary>
internal enum BuildStep
{
    /// <summary>Nobody asked. Never a state the loop settles in.</summary>
    Unspecified = 0,

    /// <summary>No confirmed order, so nothing is being built and nothing is
    /// held.</summary>
    Idle = 1,

    /// <summary>Walking to the marker, because he was somewhere else.</summary>
    GoingToSite = 2,

    /// <summary>Walking to the permitted supply container.</summary>
    GoingToSupply = 3,

    /// <summary>Taking the phase's material out of that container.</summary>
    Provisioning = 4,

    /// <summary>Walking to where the next piece goes.</summary>
    GoingToPiece = 5,

    /// <summary>Standing at the piece, working. The visible half.</summary>
    Working = 6,

    /// <summary>Waiting for something a player can change: material, a chest, an
    /// obstruction, loaded ground.</summary>
    Waiting = 7,

    /// <summary>Every piece of the shelter is standing.</summary>
    Finished = 8,

    /// <summary>Over, and it did not finish. Everything it held has been
    /// accounted for.</summary>
    Stopped = 9,
}

/// <summary>The execution loop for a confirmed build order: reserve, carry,
/// move, work, place, commit - in that order, once per piece, and re-read from
/// the world every round.
///
/// <b>This is the thing #380 was missing.</b> Planning, pricing, gating, the
/// blueprint, the progress read and the sentences all existed and were all
/// tested; nothing called them in sequence, so a player could mark a site,
/// confirm it, read a progress line and watch nothing happen. Every decision
/// below was already written somewhere in this folder - what a shelter is, what
/// it costs, whether a piece may be placed, whether one is already standing -
/// and this file's whole job is to ask them in the right order and to move
/// exactly the material that answer implies.
///
/// <b>Progress is re-read every round, and that is the resume mechanism.</b>
/// <see cref="ConstructionProgress.Read"/> looks at the site rather than
/// remembering what was asked for, so a piece the player built by hand is
/// skipped, a piece Thorstein put up before a reload is skipped, and a piece
/// that turned out not to be standing is tried again. <c>Player.PlacePiece</c>
/// returning <c>void</c> is therefore not a problem: the look is the answer.
///
/// <b>Batches are phases.</b> One draw out of the permitted container covers the
/// remaining cost of the phase being built, which is what makes the trips
/// coherent - foundation, walls, roof, inside - instead of seventeen
/// single-piece errands. A second draw for the same phase only happens when he
/// cannot afford the next piece and <see cref="DrawRetrySeconds"/> has passed,
/// so the chest is opened once per phase in the ordinary case and the number is
/// counted (<see cref="Draws"/>) rather than asserted in a comment.
///
/// <b>Material is conserved because there are only four movements.</b> It comes
/// out of the container (<see cref="IBuildMaterials.Draw"/>), sits in his own
/// persisted inventory, leaves when a piece has actually gone up
/// (<see cref="IBuildMaterials.Commit"/>), or goes back
/// (<see cref="IBuildMaterials.PutBack"/>). The loop never spends for a piece
/// that was refused or that failed to appear, never spends twice for one piece,
/// and never draws for a piece that is already standing.
///
/// <b>Nothing is ever cleared to make a placement work.</b> An obstructed,
/// tilted, warded or unbuildable placement is a <i>named refusal</i> and the
/// order waits for a person. The placement-clearance policy is a parked owner
/// decision (<c>docs/settlement/cart-and-collection/DECISIONS.md</c> D13), so
/// levelling ground, removing an obstacle or destroying anything to get a piece
/// to go up is not a thing this loop is allowed to want.
///
/// <b>What a round costs, recorded because nothing pins it.</b>
/// <see cref="ConstructionProgress.Read"/> asks <see cref="IPieceSight"/> once per
/// planned placement - seventeen times - and the game-side implementation of that
/// is <c>Piece.GetAllPiecesInRadius</c>, a linear scan of the static piece list
/// with a distance test per piece. At the runtime's round rate that is eighty-five
/// scans a second in a built-up base, and there is no performance budget test in
/// this product to notice it getting worse. It is a whole read on purpose: the
/// completion condition, the phase order and the resume-after-reload behaviour all
/// depend on every placement being looked at, so narrowing it to "the next
/// buildable piece plus its phase" is a correctness change and wants its own
/// issue rather than a quiet edit here. The one case that was free to fix has
/// been: a FINISHED order used to run this forever, and now it is looked at every
/// ten seconds through <see cref="LooksFinished"/> instead.
///
/// <b>Placement and payment share a journalled commit (#398).</b> The placement
/// gate runs first. Custody persists CommitStarted before installation, verifies
/// the standing piece and its measured cost, then persists CommitFinished. A
/// missing outcome is a named repair; no inventory compensation is guessed.
/// These are the existing schema-v3 rows, not a separate construction format.</summary>
internal sealed class ShelterBuildLoop
{
    /// <summary>How close he has to be to a placement to work on it. A wood
    /// panel is two metres across and vanilla's own build range is comfortably
    /// more than this; the point of the number is that he is visibly <i>at</i>
    /// the piece rather than across the camp.</summary>
    internal const float PieceReachMetres = 2.5f;

    /// <summary>How close he has to be to the container. Custody's own reach
    /// check is three metres (<c>ForemanCustodyRuntime.ReachMetres</c>), so the
    /// walk aims inside it rather than at its edge - a transfer refused for
    /// reach after a successful walk is a loop that looks broken.</summary>
    internal const float SupplyReachMetres = 2f;

    /// <summary>How far from the marker counts as "not at the site yet". Inside
    /// this he goes straight to the piece; outside it he walks to the order
    /// first, which is what makes him visibly arrive at a build site rather than
    /// materialise at a wall.</summary>
    internal const float SiteApproachMetres = 8f;

    /// <summary>How long the working pose runs before the piece goes up. Long
    /// enough to see, short enough that seventeen of them are not an
    /// afternoon.</summary>
    internal const float WorkSeconds = 1.5f;

    /// <summary>How long a phase that came up short waits before opening the
    /// container again. A player who puts the missing wood in gets the order
    /// going again by itself; a player who does not is not costing anything.
    /// </summary>
    internal const float DrawRetrySeconds = 10f;

    private readonly Func<ShelterPlan> _plan;
    private readonly Func<bool> _authorised;
    private readonly IPieceSight _sight;
    private readonly IBuildWalk _walk;
    private readonly IBuildMaterials _materials;
    private readonly IPiecePlacer _placer;
    private readonly IBuildPose _pose;
    private readonly Action<string> _say;

    private string _orderTag = string.Empty;
    private BuildPhase _drawnPhase = BuildPhase.Unspecified;
    private float _drawnAt = float.NegativeInfinity;
    private string _drawRefusal = string.Empty;
    private bool _finishedSaid;
    private string? _workingKey;
    private float _workingSince;
    private bool _posed;
    private bool _settled;

    internal ShelterBuildLoop(
        Func<ShelterPlan> plan,
        Func<bool> authorised,
        IPieceSight sight,
        IBuildWalk walk,
        IBuildMaterials materials,
        IPiecePlacer placer,
        IBuildPose pose,
        Action<string> say)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _authorised = authorised ?? throw new ArgumentNullException(nameof(authorised));
        _sight = sight ?? throw new ArgumentNullException(nameof(sight));
        _walk = walk ?? throw new ArgumentNullException(nameof(walk));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _placer = placer ?? throw new ArgumentNullException(nameof(placer));
        _pose = pose ?? throw new ArgumentNullException(nameof(pose));
        _say = say ?? throw new ArgumentNullException(nameof(say));
    }

    /// <summary>What he is doing.</summary>
    internal BuildStep Step { get; private set; } = BuildStep.Idle;

    /// <summary>How many pieces this loop has put up since the order started.
    /// </summary>
    internal int Built { get; private set; }

    /// <summary>How many times the permitted container has been opened for this
    /// order. <b>Counted rather than claimed</b>: "the provision phase uses the
    /// chest once" is a number a test can read.</summary>
    internal int Draws { get; private set; }

    /// <summary>The last round's summary, for the panel.</summary>
    internal ShelterRound Round { get; private set; }

    /// <summary>The sentence a player would read for the last round.</summary>
    internal string Reason { get; private set; } = string.Empty;

    /// <summary>The progress read on the last round, or null before the first.
    /// </summary>
    internal ConstructionProgress? Progress { get; private set; }

    /// <summary>Whether the shelter is standing, asked without working.
    ///
    /// <b>Looking at a site does not need a worker</b>, and that is the whole
    /// point of this method: once the cottage is finished the runtime still has to
    /// notice a player knocking a wall out, and doing that through
    /// <see cref="Tick"/> would mean taking Thorstein's body out of the arbiter
    /// several times a second forever to look at something. This reads the plan
    /// and the site and touches nothing else.</summary>
    internal bool LooksFinished()
    {
        try
        {
            if (!Safe(_authorised))
            {
                return false;
            }

            ShelterPlan plan = _plan();
            if (!plan.IsPlanned)
            {
                return false;
            }

            ConstructionProgress progress = ConstructionProgress.Read(plan, _sight);
            if (progress.IsComplete)
            {
                return true;
            }

            // <b>Not IsComplete on its own, and the difference is a worker's body.</b>
            // IsComplete counts an UNKNOWN sighting as not-standing, which is the
            // right answer for deciding whether to BUILD and the wrong one for
            // deciding whether a finished shelter has stopped being finished:
            // unloaded ground reads Unknown, so a player simply walking away from a
            // completed cottage looked exactly like somebody taking it apart, the
            // exemption lapsed, and the order took Thorstein back to stand there
            // waiting - with MayRetireBody and MayRelocateHome false for as long as
            // they stayed away, and nothing telling them why. Only a piece that was
            // actually looked at and is not there counts as work: Missing because it
            // is gone, Blocked because something else is in its place, and Unknown
            // because nobody could tell is neither.
            foreach (CostedPiece piece in plan.Pieces)
            {
                PieceSighting sighting = progress.SightingOf(piece.Key);
                if (sighting == PieceSighting.Missing || sighting == PieceSighting.Blocked)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception)
        {
            // A look that failed is not a finished shelter, and the fail-closed
            // direction here is to go and check properly.
            return false;
        }
    }

    /// <summary>One round of work. Called from the worker's own tick.</summary>
    internal ShelterRound Tick(float now)
    {
        try
        {
            return Report(Run(now));
        }
        catch (Exception exception)
        {
            // A round that threw has not told us what moved, so nothing is put
            // back on a guess: whatever he was carrying is still his, which is
            // what the sentence says.
            Pose(false);
            Quiet();
            _settled = true;
            Step = BuildStep.Stopped;
            return Report(new ShelterRound(
                RoundOutcome.Stopped,
                Built,
                leftForAnotherRound: 0,
                carrying: Carried(),
                missing: null,
                reason: "the build round failed (" + SafeFailure.Brief(exception) +
                    "). Nothing more is placed and nothing has been taken out of a container since."));
        }
    }

    /// <summary>The order was withdrawn, the world is going away, or a person
    /// said stop. Puts back what he still holds and says where it went.
    ///
    /// <b>It does not send him back to the chest first, and that is a stated
    /// limitation rather than an oversight.</b> Custody's reach check is not
    /// waived for a cancellation, so a withdrawal while he is standing at the
    /// build site cannot reach the container: the material then stays in his own
    /// <i>persisted</i> inventory, nothing is lost, and the sentence says "he is
    /// still carrying X; it is in his own inventory and nothing has been lost"
    /// rather than implying a refund. A return trip would be a new phase that
    /// keeps his mode held after the player asked for it back, which is a
    /// behaviour change and its own issue. Unreturned reservations keep their
    /// original order and source. A later order cannot silently spend them;
    /// cf_settle reconcile names the holdings.</summary>
    internal ShelterRound Cancel(float now, string why)
    {
        Pose(false);
        Quiet();
        _settled = true;
        Step = BuildStep.Stopped;
        _drawnPhase = BuildPhase.Unspecified;

        MaterialTally refunded;
        string failure;
        try
        {
            refunded = _materials.PutBack(out failure);
        }
        catch (Exception exception)
        {
            refunded = new MaterialTally();
            failure = SafeFailure.Brief(exception);
        }

        string reason = string.IsNullOrEmpty(why) ? "it was stopped" : why;
        if (failure.Length != 0)
        {
            reason += " The material he was carrying could not be put back (" + failure + ").";
        }

        // Every sentence about a stop says both halves: what went back, and what
        // is still on him. A player who cannot tell a refund from a loss has
        // been told nothing.
        Reason = ConstructionSentences.Stopped(reason, refunded, Carried());
        _say(Reason);

        // The ROUND carries the finished sentence, not the bare reason: Tick's
        // own reporting takes a round's reason as the thing to show, and a
        // half-sentence there is how "it stopped" reaches a player with no word
        // about where their wood went.
        Round = new ShelterRound(
            RoundOutcome.Stopped, Built, leftForAnotherRound: 0, carrying: Carried(), missing: null, reason: Reason);
        return Round;
    }

    /// <summary>A world has gone away. Drops every per-load memory without
    /// touching an inventory: the container and the body belong to a world that
    /// is not there to be written to.</summary>
    internal void Forget()
    {
        Pose(false);
        Step = BuildStep.Idle;
        Built = 0;
        Draws = 0;
        Progress = null;
        Round = default;
        Reason = string.Empty;
        _orderTag = string.Empty;
        _drawnPhase = BuildPhase.Unspecified;
        _drawnAt = float.NegativeInfinity;
        _drawRefusal = string.Empty;
        _workingKey = null;
        _settled = false;
        _finishedSaid = false;
    }

    private ShelterRound Run(float now)
    {
        if (!Safe(_authorised))
        {
            // No confirmed order. If one was running, this is a withdrawal and
            // the material goes back with a sentence; if none ever was, there is
            // nothing to say.
            if (_orderTag.Length != 0)
            {
                _orderTag = string.Empty;
                return Cancel(now, "the build order was withdrawn.");
            }

            Pose(false);
            Step = BuildStep.Idle;

            // No reason, deliberately. A round with a sentence in it replaces
            // whatever the last round said, and the round AFTER a withdrawal
            // would otherwise overwrite "40 Wood went back where it came from"
            // with "there is no confirmed build order" - which is true and is not
            // the thing the player needs to read. What the state is now is
            // answered by Step and by the runtime's own status line.
            return new ShelterRound(RoundOutcome.Waiting, 0, 0, null, null, string.Empty);
        }

        ShelterPlan plan = _plan();
        if (!plan.IsPlanned)
        {
            Pose(false);
            Quiet();
            Step = BuildStep.Waiting;
            return new ShelterRound(RoundOutcome.Waiting, Built, 0, Carried(), null, plan.Refusal + ".");
        }

        string tag = TagOf(plan.Marker);
        if (!string.Equals(tag, _orderTag, StringComparison.Ordinal))
        {
            // A different order: a marker somewhere else, or the first one. Its
            // counters are its own, and nothing is carried over from the last.
            _materials.BeginOrder(tag);
            _orderTag = tag;
            Built = 0;
            Draws = 0;
            _drawnPhase = BuildPhase.Unspecified;
            _drawnAt = float.NegativeInfinity;
            _drawRefusal = string.Empty;
            _workingKey = null;
            _settled = false;
            _finishedSaid = false;
        }

        // Read from the world, every round, before anything else. This is what
        // makes a reload resume, a player's own wall count, and a piece that did
        // not actually go up get tried again.
        ConstructionProgress progress = ConstructionProgress.Read(plan, _sight);
        Progress = progress;

        if (progress.IsComplete)
        {
            return Finish(plan);
        }

        // Not complete any more, so the NEXT completion is a different completion
        // and gets its own report and its own tidy-up.
        //
        // <b>"Say it once" and "do the work once" are not the same thing</b>, and
        // conflating them is what this line fixes. Without it a second completion -
        // after a player knocked a wall out and Thorstein rebuilt it - took the
        // early return in Finish, so the leftover material from the repair trip was
        // never put back in the chest, and the status line reported whatever the
        // last ROUND had said (in the observed case "put up bed.") because the
        // early return hands back a Round that later rounds have overwritten.
        _finishedSaid = false;

        if (_settled)
        {
            // Stopped, and still stopped: a stopped order does not restart
            // itself. Saying so is the whole of it.
            Pose(false);
            return new ShelterRound(
                RoundOutcome.Stopped, Built, progress.Remaining.Count, Carried(), null, Reason);
        }

        if (!_walk.IsPresent)
        {
            Pose(false);
            Step = BuildStep.Waiting;
            return new ShelterRound(
                RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(), null,
                "Thorstein is not here to build it. Nothing has been taken out of a container.");
        }

        CostedPiece? next = NextBuildable(progress);
        if (next == null)
        {
            return Stalled(progress);
        }

        CostedPiece piece = next.Value;
        BuildPhase phase = piece.Phase;

        // Provisioning: one draw for the phase, out of the container the player
        // permitted and nothing else.
        MaterialTally batch = RemainingCostOf(progress, phase);
        MaterialTally shortOfBatch = batch.Missing(_materials.Carried);
        bool canAffordNext = _materials.IsReserved(piece) && Covers(_materials.Carried, piece.Recipe);
        bool firstDrawForPhase = phase != _drawnPhase;
        bool mayRetryDraw = canAffordNext == false && now - _drawnAt >= DrawRetrySeconds;

        if ((!shortOfBatch.IsEmpty || !canAffordNext) && (firstDrawForPhase || mayRetryDraw))
        {
            if (!_materials.TrySupply(out SitePoint supply, out string refusal))
            {
                Pose(false);
                Quiet();
                Step = BuildStep.Waiting;
                return new ShelterRound(
                    RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(), shortOfBatch,
                    ConstructionSentences.ContainerUnavailable(refusal));
            }

            if (_walk.Position.HorizontalDistanceTo(supply) > SupplyReachMetres)
            {
                return Walk(BuildStep.GoingToSupply, supply, SupplyReachMetres, progress,
                    "fetching " + shortOfBatch.Describe() + " for " + BuildPhases.Describe(phase) + ".");
            }

            Pose(false);
            Step = BuildStep.Provisioning;
            _drawnPhase = phase;
            _drawnAt = now;
            var pieces = new List<CostedPiece>();
            foreach (CostedPiece remaining in progress.Remaining)
                if (remaining.Phase == phase) pieces.Add(remaining);
            BuildDraw draw = _materials.Draw(pieces);
            if (draw.IsRefused)
            {
                // Kept, because the next round would otherwise report the
                // shortfall instead - and "you are short of wood" for a chest
                // that is warded sends a player to fetch wood they already have.
                _drawRefusal = ConstructionSentences.ContainerUnavailable(draw.Refusal);
                return new ShelterRound(
                    RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(), shortOfBatch,
                    _drawRefusal);
            }

            _drawRefusal = string.Empty;
            Draws++;

            if (!draw.Drawn.IsEmpty)
            {
                _say("Thorstein took " + draw.Drawn.Describe() + " out of the supply chest for " +
                    BuildPhases.Describe(phase) + ".");
            }

            canAffordNext = _materials.IsReserved(piece) && Covers(_materials.Carried, piece.Recipe);
            if (!canAffordNext)
            {
                return new ShelterRound(
                    RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(),
                    new MaterialTally().Add(piece.Recipe).Missing(_materials.Carried), string.Empty);
            }
        }

        if (!canAffordNext)
        {
            Pose(false);
            Quiet();
            Step = BuildStep.Waiting;
            return new ShelterRound(
                RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(),
                new MaterialTally().Add(piece.Recipe).Missing(_materials.Carried), _drawRefusal);
        }

        // The site, then the piece. The order matters and is not cosmetic: the
        // chest can be anywhere in the camp, so provisioning first and walking to
        // the marker second is one trip out and one trip back rather than three.
        // Inside SiteApproachMetres the marker walk is skipped, because by then
        // the piece walk IS the walk to the site.
        SitePoint site = plan.Marker.At;
        if (_walk.Position.HorizontalDistanceTo(site) > SiteApproachMetres)
        {
            return Walk(BuildStep.GoingToSite, site, PieceReachMetres, progress,
                "walking to the build site.");
        }

        // To the piece.
        if (_walk.Position.HorizontalDistanceTo(piece.Placement.At) > PieceReachMetres)
        {
            return Walk(BuildStep.GoingToPiece, piece.Placement.At, PieceReachMetres, progress,
                "going to put up " + piece.Placement.Piece.Prefab + ".");
        }

        // The visible work, around the placement rather than instead of it.
        if (!string.Equals(_workingKey, piece.Key, StringComparison.Ordinal))
        {
            // Aimed at the piece even though he is already inside
            // PieceReachMetres of it. A four-metre cottage's panels are all
            // within one step of the marker, so without this he would put the
            // whole thing up from wherever he happened to stop - and the walk to
            // a piece would be a branch that this blueprint never takes, which
            // is precisely the kind of never-executed path #380 is about. The
            // goal is a metre or two away and vanilla's motor closes it in a
            // step; a goal it cannot close cannot stall the build either,
            // because the round's own reach check has already passed.
            _walk.WalkTo(piece.Placement.At, PieceReachMetres);
            _workingKey = piece.Key;
            _workingSince = now;
            Pose(true);
        }

        if (now - _workingSince < WorkSeconds && now >= _workingSince)
        {
            Step = BuildStep.Working;
            return new ShelterRound(
                RoundOutcome.Working, Built, progress.Remaining.Count, Carried(), null,
                "working on " + piece.Placement.Piece.Prefab + ".");
        }

        return PlaceAndPay(piece, progress);
    }

    /// <summary>Gate, place, pay - in that order and nowhere else in this file.
    ///
    /// <b>The cost is checked against what he carries and paid out of it.</b>
    /// The gate's own cost check (<see cref="PlacementRefusal.Cost"/>) is given
    /// <see cref="IBuildMaterials.Carried"/>, so a piece is never placed against
    /// material that is not in his hands, and the pay is the same amount out of
    /// the same inventory.</summary>
    private ShelterRound PlaceAndPay(CostedPiece piece, ConstructionProgress progress)
    {
        MaterialTally carried = _materials.Carried;
        MaterialTally spent = new MaterialTally();
        bool Commit(Func<bool> install, out string failure) => _materials.Commit(piece, () =>
        {
            if (!install()) return false;
            PiecePlacement placement = piece.Placement;
            return _sight.Look(in placement) == PieceSighting.Standing;
        }, out spent, out failure);
        PiecePlaced placed = _placer.Place(piece, carried, authorised: true, out string reason, Commit);
        Pose(false);
        _workingKey = null;

        switch (placed)
        {
            case PiecePlaced.Placed:
                break;

            case PiecePlaced.Refused:
                // The system working. Nothing was spent, and the sentence says
                // which check refused it and that nothing left a container.
                Step = BuildStep.Waiting;
                return new ShelterRound(
                    RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(), null,
                    ConstructionSentences.Refused(piece.Placement, reason));

            default:
                // Every gate said yes and the piece did not appear. Retrying is
                // pointless in a way a refusal's is not, so the order stops and
                // names it rather than walking back and forth forever.
                _settled = true;
                Step = BuildStep.Stopped;
                Reason = piece.Placement.Piece.Prefab + " at " + piece.Placement.At +
                    " could not complete its placement and payment (" + reason +
                    "). Nothing else is built; run cf_settle reconcile before moving its material.";
                return new ShelterRound(
                    RoundOutcome.Stopped, Built, progress.Remaining.Count, Carried(), null, Reason);
        }

        // The standing piece and its payment were both confirmed inside the
        // commit callback, with no yield point between them.
        Built++;
        Step = BuildStep.Working;
        _say("Thorstein put up " + piece.Placement.Piece.Prefab + " at " + piece.Placement.At + " for " +
            spent.Describe() + ".");
        return new ShelterRound(
            RoundOutcome.Working, Built, Math.Max(0, progress.Remaining.Count - 1), Carried(), null,
            "put up " + piece.Placement.Piece.Prefab + ".");
    }

    /// <summary>The shelter is standing.
    ///
    /// <b>It says so once.</b> A round is cheap and a finished shelter stays
    /// finished, so without this guard every round after completion repeated the
    /// sentence, asked the chest to take material it no longer holds, and handed
    /// the runtime a fresh Finished step to release the body on - five identical
    /// log lines a second, for as long as the world is open. Reported once, then
    /// the same round is handed back unchanged.</summary>
    private ShelterRound Finish(ShelterPlan plan)
    {
        Pose(false);
        Quiet();
        Step = BuildStep.Finished;
        _drawnPhase = BuildPhase.Unspecified;

        if (_finishedSaid)
        {
            return Round;
        }

        _finishedSaid = true;
        MaterialTally back = new MaterialTally();
        string failure = string.Empty;
        if (!_materials.Carried.IsEmpty)
        {
            back = _materials.PutBack(out failure);
        }

        Reason = "The shelter is finished: " + plan.Pieces.Count + " pieces are standing." +
            (back.IsEmpty ? string.Empty : " " + back.Describe() + " that was left over went back into the supply chest.") +
            (Carried().IsEmpty
                ? string.Empty
                : " Thorstein is still carrying " + Carried().Describe() +
                  (failure.Length == 0 ? "; it is in his own inventory." : " (" + failure + ")."));
        _say(Reason);
        Round = new ShelterRound(RoundOutcome.Finished, Built, 0, Carried(), null, Reason);
        return Round;
    }

    /// <summary>There is nothing buildable this round and the shelter is not
    /// finished. Either something is in the way - which is a person's to fix and
    /// never this loop's - or a phase's ground could not be read.</summary>
    private ShelterRound Stalled(ConstructionProgress progress)
    {
        Pose(false);
        Quiet();
        Step = BuildStep.Waiting;

        IReadOnlyList<CostedPiece> blocked = progress.Blocked;
        string why = blocked.Count > 0
            ? ConstructionSentences.Obstructed(blocked)
            : "the ground where the next pieces go could not be read, so nothing is placed there. " +
              "Stay near the site and it goes on by itself.";
        return new ShelterRound(
            RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(), null, why);
    }

    private ShelterRound Walk(
        BuildStep step, SitePoint to, float tolerance, ConstructionProgress progress, string what)
    {
        Pose(false);
        Step = step;
        _workingKey = null;
        if (!_walk.WalkTo(to, tolerance))
        {
            Step = BuildStep.Waiting;
            return new ShelterRound(
                RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(), null,
                "Thorstein could not be sent " + what + " Nothing has moved.");
        }

        if (_walk.Status == BuildWalkStatus.Deferred)
        {
            string deferral = _walk.Deferral;
            Step = BuildStep.Waiting;
            return new ShelterRound(
                RoundOutcome.Waiting, Built, progress.Remaining.Count, Carried(), null,
                "Thorstein cannot get there: " +
                (deferral.Length == 0 ? "the way was not passable" : deferral) +
                ". Nothing has been taken out of a container.");
        }

        return new ShelterRound(
            RoundOutcome.Working, Built, progress.Remaining.Count, Carried(), null, what);
    }

    private ShelterRound Report(ShelterRound round)
    {
        Round = round;
        if (round.Reason.Length != 0)
        {
            Reason = round.Reason;
        }
        else if (!round.Missing.IsEmpty)
        {
            Reason = ConstructionSentences.ShortOfMaterial(round.Missing);
        }

        return round;
    }

    /// <summary>The first piece whose disposition is <c>Build</c>: not standing,
    /// nothing in the way, and every earlier phase finished. The phase order is
    /// <see cref="ConstructionProgress"/>'s and is not repeated here.</summary>
    private static CostedPiece? NextBuildable(ConstructionProgress progress)
    {
        foreach (CostedPiece piece in progress.Plan.Pieces)
        {
            if (progress.DispositionOf(piece) == PieceDisposition.Build)
            {
                return piece;
            }
        }

        return null;
    }

    /// <summary>What the pieces of one phase that are not standing still cost.
    /// <b>Not the phase's total</b>, or a phase the player half built by hand
    /// would be provisioned twice over.</summary>
    private static MaterialTally RemainingCostOf(ConstructionProgress progress, BuildPhase phase)
    {
        var tally = new MaterialTally();
        foreach (CostedPiece piece in progress.Remaining)
        {
            if (piece.Phase == phase && progress.SightingOf(piece.Key) == PieceSighting.Missing)
            {
                tally.Add(piece.Recipe);
            }
        }

        return tally;
    }

    private static bool Covers(MaterialTally carried, PieceRecipe recipe) =>
        new MaterialTally().Add(recipe).Missing(carried).IsEmpty;

    private static string TagOf(BuildOrderMarker marker) => string.Format(
        CultureInfo.InvariantCulture, "{0}@{1}/{2:0.###}", marker.Kind, marker.At, marker.Yaw);

    private MaterialTally Carried()
    {
        try
        {
            return _materials.Carried.Copy();
        }
        catch (Exception)
        {
            // A round that cannot read what he holds must still be able to
            // report; an empty tally here only ever understates, and every
            // sentence that uses it says "still carrying" rather than "nothing".
            return new MaterialTally();
        }
    }

    private void Pose(bool on)
    {
        if (_posed == on)
        {
            return;
        }

        _posed = on;
        try
        {
            _pose.Working(on);
        }
        catch (Exception)
        {
            // Presentation. A build that stopped because of an animator would be
            // a defect; a build with no arm movement is a disappointment.
        }
    }

    private void Quiet()
    {
        try
        {
            _walk.Stop();
        }
        catch (Exception)
        {
            // Stopping a walk that is already stopped, or a body that has gone,
            // is not a failure of the round.
        }
    }

    private static bool Safe(Func<bool> gate)
    {
        try
        {
            return gate();
        }
        catch (Exception)
        {
            // Authority that could not be established is not authority.
            return false;
        }
    }
}
