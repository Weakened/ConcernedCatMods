using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Where Gunnar's tour plan stands, as the runtime and the console see
/// it.</summary>
internal enum TourPlanStanding
{
    /// <summary>No journal: no world is loaded, or it has no id to name a file
    /// by. A tour refuses to start, with a sentence.</summary>
    Closed = 0,

    /// <summary>A journal is open and no plan is open in it - none was ever
    /// written, or the last one ended. A start begins a new one.</summary>
    Idle = 1,

    /// <summary>Read back from the disk and not yet revalidated. Revalidation
    /// waits until his body, his record, the switch and the authority can all
    /// answer (<see cref="GunnarTourPlan.WaitingFor"/>).</summary>
    Recovered = 2,

    /// <summary>Revalidated after a reload: back to looking, holding exactly the
    /// load his body was measured holding, with its progress kept. The next start
    /// re-attaches it to the area and chest chosen in this world load.</summary>
    Replanned = 3,

    /// <summary>Open in this session and not running: the tour stopped while he
    /// was carrying something. The next start carries on from where it was.
    /// </summary>
    Suspended = 4,

    /// <summary>A tour is working it.</summary>
    Running = 5,

    /// <summary>Stopped for a person, with a sentence. Only
    /// <c>ct_collect resolve</c> leaves this.</summary>
    NeedsAttention = 6,
}

/// <summary>What the runtime measured, now, for a decision about a plan. Every
/// field is something the game was asked this frame; nothing is remembered.
/// </summary>
internal readonly struct TourPlanFacts
{
    public TourPlanFacts(
        NpcWorldEpoch world,
        int bodiesAnswering,
        IReadOnlyList<KeyValuePair<string, int>>? carried,
        bool mayWork,
        float at)
    {
        World = world;
        BodiesAnswering = bodiesAnswering < 0 ? 0 : bodiesAnswering;
        Carried = carried;
        MayWork = mayWork;
        At = at;
    }

    /// <summary>The world load that is on now.</summary>
    public NpcWorldEpoch World { get; }

    /// <summary>How many bodies answer to Gunnar: 0, 1, or more than one.
    /// </summary>
    public int BodiesAnswering { get; }

    /// <summary>What he holds, per material, measured from his own inventory.
    /// <b>Null is "could not be read", never "nothing".</b></summary>
    public IReadOnlyList<KeyValuePair<string, int>>? Carried { get; }

    /// <summary>The shared work-authority rule granted, now.</summary>
    public bool MayWork { get; }

    /// <summary>The caller's clock, in seconds.</summary>
    public float At { get; }
}

/// <summary>What one revalidation decided, with the sentence a player reads.
/// </summary>
internal readonly struct TourPlanVerdict
{
    public TourPlanVerdict(InterruptionResponse response, InterruptionCause cause, string sentence)
    {
        Response = response;
        Cause = cause;
        Sentence = sentence ?? string.Empty;
    }

    /// <summary>Unspecified when there was nothing to decide.</summary>
    public InterruptionResponse Response { get; }

    public InterruptionCause Cause { get; }

    public string Sentence { get; }

    public bool MayResume =>
        Response == InterruptionResponse.Continue || Response == InterruptionResponse.Replan;
}

/// <summary>Gunnar's collection tour, written down before it is acted on
/// (#379, CNPC-008). <b>The second production adopter of ConcernedNPC's
/// interruption layer</b>, after the Steward, and built the same way: the
/// tour's runtime stays the only thing that touches the world, and this is its
/// write-ahead witness.
///
/// <b>Game-free, and what it is handed.</b> Measured stacks as plain data (the
/// deposit port's per-material count, or null when it could not be read), the
/// number of bodies the hauling census found, the work-authority verdict as a
/// boolean, and the world load the registry says is on. It never asks the game
/// anything, so every path below is driven by a test over a fake world.
///
/// <b>One plan per Gunnar per world.</b> The job id is the constant
/// <see cref="JobId"/> and the identity is <see cref="WorkerKey.Gunnar"/>, so a
/// reload finds the same plan rather than a new one, and the file is named by
/// the world (<see cref="GunnarTourPlanFiles"/>).
///
/// <b>The ordering rule, as it applies to a tour.</b> Every pick and every trip
/// to the chest is announced (<see cref="IntendPick"/>,
/// <see cref="IntendDeposit"/>) before the world is touched and concluded with
/// what was measured afterwards. A failed announcement refuses the move. A move
/// whose outcome nobody measured stops the plan for a person rather than being
/// replayed or forgotten. The phases the library treats as moving material
/// (Provisioned, Reconciling) are reached through a measured no-op intent and
/// conclusion, Steward's pattern, which restates what he already holds and moves
/// nothing.
///
/// <b>What a tour persists.</b> Identity and job; phase; reservations, always
/// none, because a tour sets nothing aside - what it collects is found, not
/// reserved; what he carries, measured, one stack per material in name order;
/// sources worked and sources started (<see cref="NpcPlanState.TargetsDone"/>,
/// <see cref="NpcPlanState.TargetsTotal"/>); no vehicle, because a tour is on
/// foot (<c>DECISIONS.md</c> D17); the area as the source; the chest's key as the
/// destination - <b>evidence only after a reload</b>, because a network id names
/// something else in the next world load (D6, D15) and the player chooses the
/// chest again; and custody, through the intent and the conclusion.
///
/// <b>No priority ladder.</b> ConcernedNPC's activity ladder decides which of two
/// activities gets a body. It does not apply to Gunnar: his identity lease
/// (<see cref="CollectionIdentityLease"/>) lets exactly one job hold him at a
/// time and nothing preempts another - a haul and a tour refuse each other, an
/// ordered pick refuses while either runs. So "an interrupted job resumes rather
/// than restarts" is answered here by the plan staying open across whatever held
/// him, and being continued rather than begun again, with the same job id, the
/// same progress and the same load.</summary>
internal sealed class GunnarTourPlan
{
    /// <summary>The tour's job id. One per Gunnar per world; never numbered,
    /// because a numbered id is a new plan every time and a reload would find
    /// none of them.</summary>
    internal const string JobId = "teamster-collect";

    /// <summary>What an ordered pick or deposit is told while a plan is open.
    /// </summary>
    internal const string OpenPlanRefusal =
        "An interrupted collection tour is open, so he takes no other order until it is dealt with: " +
        "ct_collect start carries it on, ct_collect resolve settles it.";

    private static readonly NpcMaterialStack[] Nothing = new NpcMaterialStack[0];

    private readonly NpcIdentity _identity;
    private readonly INpcPlanCodec _codec;

    private NpcPlanJournal? _journal;
    private NpcPlanRun? _run;
    private TourPlanStanding _standing = TourPlanStanding.Closed;
    private string _sentence = string.Empty;
    private string _closedReason = "no world is loaded";
    private bool _unreadable;
    private bool _depositPending;

    internal GunnarTourPlan(INpcPlanCodec? codec = null)
    {
        if (!NpcIdentity.TryParse(WorkerKey.Gunnar.Value, out _identity))
        {
            throw new InvalidOperationException("Gunnar's identity could not be read.");
        }

        _codec = codec ?? new GunnarTourPlanCodec(_identity, JobId);
    }

    internal NpcIdentity Identity => _identity;

    internal TourPlanStanding Standing => _standing;

    /// <summary>The plan as the record says, or null when none is open.</summary>
    internal NpcPlanState? State => _run?.State;

    /// <summary>Whether a journal is open for the world that is loaded.</summary>
    internal bool HasJournal => _journal != null;

    /// <summary>Whether an ordered pick or deposit must refuse: a plan is open
    /// and not running. The tour's own deposit is the tour, so it is unaffected.
    /// </summary>
    internal bool BlocksOrders =>
        _standing == TourPlanStanding.Recovered
        || _standing == TourPlanStanding.Replanned
        || _standing == TourPlanStanding.Suspended
        || _standing == TourPlanStanding.NeedsAttention;

    /// <summary>Why the plan stopped for a person, or empty.</summary>
    internal string AttentionReason =>
        _standing == TourPlanStanding.NeedsAttention ? _sentence : string.Empty;

    // ------------------------------------------------------------------
    // The world-up edge, and the world-down edge
    // ------------------------------------------------------------------

    /// <summary>Opens this world's journal and reads what is there. No material
    /// moves and nothing is decided: a plan that was open becomes
    /// <see cref="TourPlanStanding.Recovered"/> and waits to be revalidated.
    /// </summary>
    /// <param name="path">The absolute plan path, or null when there is no
    /// world to name it by.</param>
    /// <param name="whyNoPath">The sentence to report when the path is null.
    /// </param>
    internal TourPlanStanding Open(string? path, string? whyNoPath)
    {
        Close();
        if (path == null)
        {
            _closedReason = string.IsNullOrEmpty(whyNoPath) ? "no world could be named" : whyNoPath!;
            return _standing;
        }

        if (!NpcPlanJournal.TryOpen(path, _codec, out NpcPlanJournal? journal, out string failure))
        {
            _closedReason = "the tour plan could not be opened (" + failure + ")";
            return _standing;
        }

        _journal = journal;
        _standing = TourPlanStanding.Idle;
        NpcPlanLoad loaded = journal!.Load();
        if (loaded.IsAbsent)
        {
            return _standing;
        }

        if (!loaded.IsLoaded || loaded.Plan == null)
        {
            return Unreadable(loaded.Failure);
        }

        NpcPlanState recovered = loaded.Plan;
        _run = NpcPlanRun.Resume(journal, recovered);
        if (_run == null)
        {
            return Unreadable("the rows named no plan");
        }

        if (recovered.Phase == NpcPlanPhase.NeedsAttention)
        {
            Block(recovered.Note.Length != 0
                ? recovered.Note
                : "Gunnar's last tour was stopped for somebody to look at, and it stays stopped until " +
                  "ct_collect resolve.");
            return _standing;
        }

        if ((recovered.Phase == NpcPlanPhase.Settled || recovered.Phase == NpcPlanPhase.Refunded)
            && recovered.Custody == NpcPlanCustody.Clear)
        {
            // Finished, and kept so that "this tour finished" stays different
            // from "there never was one". Nothing to resume.
            _run = null;
            return _standing;
        }

        _standing = TourPlanStanding.Recovered;
        _sentence = "an interrupted tour from the last session is waiting to be checked against his body";
        return _standing;
    }

    /// <summary>The world went away. Nothing is written: the last record is the
    /// safe resume point, whatever instant this is.</summary>
    internal void Close()
    {
        _journal = null;
        _run = null;
        _standing = TourPlanStanding.Closed;
        _sentence = string.Empty;
        _closedReason = "no world is loaded";
        _unreadable = false;
        _depositPending = false;
    }

    // ------------------------------------------------------------------
    // Revalidation
    // ------------------------------------------------------------------

    /// <summary>What a recovered plan still waits for before it may be
    /// revalidated, in words; empty when nothing. Revalidating against a census
    /// that has not finished, a body whose record has not been read, a switch
    /// that is off or an authority that refuses would decide from evidence that
    /// is about to change - and "authority lost" with a load would be a stop
    /// for a person over nothing at all.</summary>
    internal static string WaitingFor(
        WorkerBodyStatus body, bool recordTrusted, bool optedIn, bool authorityGranted)
    {
        if (!optedIn)
        {
            return "Gunnar's collection is off";
        }

        if (!authorityGranted)
        {
            return "work may not run here now";
        }

        switch (body)
        {
            case WorkerBodyStatus.Bound:
                return recordTrusted ? string.Empty : "his body has not read, or could not write, what he carries";
            case WorkerBodyStatus.NotFound:
            case WorkerBodyStatus.Duplicated:
                return string.Empty;
            case WorkerBodyStatus.NotLoaded:
                return "his body is in ground that is not loaded";
            case WorkerBodyStatus.Faulted:
                return "his body's worker faulted";
            case WorkerBodyStatus.Searching:
                return "the search for his body has not finished";
            default:
                return "the hauling runtime has not said where his body is";
        }
    }

    /// <summary>How many bodies answer to Gunnar, from the census; -1 when the
    /// census cannot say.</summary>
    internal static int BodiesFor(WorkerBodyStatus body)
    {
        switch (body)
        {
            case WorkerBodyStatus.Bound:
                return 1;
            case WorkerBodyStatus.NotFound:
                return 0;
            case WorkerBodyStatus.Duplicated:
                return 2;
            default:
                return -1;
        }
    }

    /// <summary>Decides what an interrupted plan does now: continue, re-plan the
    /// remaining work, close it because it holds nothing, or stop it for a
    /// person - and writes that down. <b>Never moves anything and never builds
    /// anything.</b>
    ///
    /// <b>Through the library's revalidation, deliberately not its
    /// reconstruction.</b> <c>NpcPlanRecovery.Reconstruct</c> claims the body
    /// under a holder name; Gunnar's body is claimed by his identity lease when a
    /// job starts, so a claim made here would make that lease answer "busy".
    /// The body question is answered by the hauling census instead, through
    /// <see cref="TourPlanFacts.BodiesAnswering"/>.
    ///
    /// <b>Two checks the library cannot make, made first.</b> Whether what he
    /// holds is what the plan says he holds, and whether it could be read at all.
    /// A record and a body that disagree are never reconciled by a guess: the
    /// plan stops for a person, and the sentence says that a crash rollback - the
    /// world going back to its last save while the plan did not - may be why.
    /// </summary>
    internal TourPlanVerdict Revalidate(in TourPlanFacts facts) => Revalidate(facts, null, null);

    private TourPlanVerdict Revalidate(in TourPlanFacts facts, string? sourceKey, string? destinationKey)
    {
        NpcPlanRun? run = _run;
        bool inSession = _standing == TourPlanStanding.Suspended;
        if (run == null || (_standing != TourPlanStanding.Recovered && !inSession))
        {
            return new TourPlanVerdict(InterruptionResponse.Unspecified, InterruptionCause.Unspecified, Describe());
        }

        NpcPlanState plan = run.State;
        if (IsTerminal(plan.Phase))
        {
            return Over(run, plan, facts);
        }

        IReadOnlyList<NpcMaterialStack>? measured = Measured(facts.Carried);
        if (facts.BodiesAnswering == 1 && !plan.IsUncertain)
        {
            if (measured == null)
            {
                return Attention(
                    InterruptionCause.Unspecified,
                    "What Gunnar is carrying could not be read, so the interrupted tour is neither continued nor " +
                    "closed - the plan says he carried " + Describe(plan.Carried) + ". Try again in a moment; if it " +
                    "stays unreadable, the log says why, and ct_collect resolve records what he holds once it can be " +
                    "read.");
            }

            if (!SameLoad(plan.Carried, measured))
            {
                return Attention(InterruptionCause.TransferUncertain, Mismatch(plan.Carried, measured));
            }
        }

        bool areaMoved = inSession && sourceKey != null && destinationKey != null
            && (!string.Equals(sourceKey, plan.SourceKey, StringComparison.Ordinal)
                || !string.Equals(destinationKey, plan.DestinationKey, StringComparison.Ordinal));
        var evidence = new NpcPlanEvidence(
            facts.World,
            facts.BodiesAnswering,
            bodyDied: false,
            areaIsReadable: true,
            areaMoved: areaMoved,
            mayWork: facts.MayWork,
            containersAvailable: true,
            routeAvailable: true,
            playerPaused: inSession,
            at: facts.At);
        NpcPlanRecovered decided = NpcPlanRecovery.Revalidate(plan, evidence, NpcWorkInterruptionPolicy.Instance);
        InterruptionResponse response = decided.Outcome.Response;
        InterruptionCause cause = decided.Outcome.Cause;
        if (response == InterruptionResponse.Refund && plan.CarriedUnits > 0)
        {
            // A refund releases what was set aside and never spent. Accepting one
            // while he carries a load would turn it into silent disposal, which
            // the shared policy forbids by name - Steward's rule, here too.
            response = InterruptionResponse.NeedsAttention;
        }

        string sentence = Sentence(response, cause, plan);
        var outcome = new InterruptionOutcome(response, cause, decided.Outcome.NotBefore, sentence);
        NpcPlanSave adopted = run.Adopt(plan.WithPhase(PhaseAfter(response, plan.Phase), sentence), outcome);
        if (!adopted.IsSaved)
        {
            Block(SaveFailure("record what the interrupted tour does next", adopted));
            return new TourPlanVerdict(InterruptionResponse.NeedsAttention, cause, _sentence);
        }

        switch (response)
        {
            case InterruptionResponse.Continue:
                _standing = TourPlanStanding.Suspended;
                _sentence = sentence;
                break;

            case InterruptionResponse.Replan:
                if (run.State.CarriedUnits == 0)
                {
                    // Nothing to come back for: a re-planned tour holding nothing
                    // is closed, and the next start is a new tour.
                    const string closed = "The interrupted tour held nothing, so it is closed; nothing was moved.";
                    if (!Write(run.Stop(NpcPlanPhase.Refunded, closed), "close the empty tour"))
                    {
                        return new TourPlanVerdict(InterruptionResponse.NeedsAttention, cause, _sentence);
                    }

                    ToIdle();
                    return new TourPlanVerdict(InterruptionResponse.Refund, cause, closed);
                }

                _standing = TourPlanStanding.Replanned;
                _sentence = sentence;
                break;

            case InterruptionResponse.Refund:
                ToIdle();
                break;

            default:
                Block(sentence);
                break;
        }

        return new TourPlanVerdict(response, cause, sentence);
    }

    // ------------------------------------------------------------------
    // Starting, continuing, re-planning
    // ------------------------------------------------------------------

    /// <summary>Opens the plan a tour is about to work, or refuses with the
    /// sentence a player reads. A recovered or stopped plan is revalidated here
    /// first, so a start never walks past an open question.
    ///
    /// <list type="bullet">
    /// <item>No plan open: a new one is begun and written down before he moves.
    /// </item>
    /// <item>A plan stopped earlier this session, same area and same chest:
    /// <b>continued</b> - same job, same progress, same load, no new beginning.
    /// </item>
    /// <item>A plan re-planned after a reload, or one whose area or chest was
    /// chosen again: re-attached to this world load, its keys rewritten, and
    /// walked forward with a measured no-op, keeping progress and load.</item>
    /// <item>A plan waiting for a person: refused, with why.</item>
    /// </list>
    ///
    /// In every case that starts, what he already carries is the first trip to
    /// the chest (the runtime's <c>startWithDeposit</c>).</summary>
    internal bool TryStart(in TourPlanFacts facts, string? sourceKey, string? destinationKey, out string sentence)
    {
        if (_journal == null)
        {
            sentence = "No tour starts: " + _closedReason + ", so nothing he did could be written down.";
            return false;
        }

        if (_standing == TourPlanStanding.Running)
        {
            sentence = "A tour is already working this plan.";
            return false;
        }

        if (facts.World.IsUnknown)
        {
            sentence = "No tour starts: no world load is current, so nothing he did could be told from the last one.";
            return false;
        }

        if (string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(destinationKey)
            || !GunnarTourPlanCodec.IsKey(sourceKey) || !GunnarTourPlanCodec.IsKey(destinationKey))
        {
            sentence = "No tour starts: the area or the chest has no name that can be written down.";
            return false;
        }

        IReadOnlyList<NpcMaterialStack>? measured = Measured(facts.Carried);
        if (measured == null)
        {
            sentence = "No tour starts: what Gunnar is carrying could not be read, and a plan that does not know " +
                "what he holds cannot say later what moved.";
            return false;
        }

        TourPlanVerdict verdict = default;
        if (_standing == TourPlanStanding.Recovered || _standing == TourPlanStanding.Suspended)
        {
            verdict = Revalidate(facts, sourceKey, destinationKey);
        }

        switch (_standing)
        {
            case TourPlanStanding.NeedsAttention:
                sentence = _sentence;
                return false;

            case TourPlanStanding.Idle:
                return BeginFresh(facts.World, sourceKey!, destinationKey!, measured, out sentence);

            case TourPlanStanding.Replanned:
                return ResumeReplanned(facts.World, sourceKey!, destinationKey!, measured, out sentence);

            case TourPlanStanding.Suspended when verdict.Response == InterruptionResponse.Continue:
                return Continue(measured, out sentence);

            default:
                sentence = "No tour starts: the open plan is in a state this build does not know how to continue (" +
                    _standing + "). Run ct_collect resolve.";
                return false;
        }
    }

    private bool BeginFresh(
        NpcWorldEpoch world, string sourceKey, string destinationKey,
        IReadOnlyList<NpcMaterialStack> measured, out string sentence)
    {
        NpcPlanRun? run = NpcPlanRun.Begin(
            _journal,
            NpcPlanState.Opening(_identity, JobId, world, 0),
            out NpcPlanSave opening);
        if (run == null)
        {
            // Nothing has moved and nothing is held: refusing here is the cheap
            // moment, and the plan stays idle rather than stopped for a person.
            sentence = "No tour starts: the plan could not be written down (" + opening.Failure + ").";
            return false;
        }

        _run = run;
        _standing = TourPlanStanding.Running;
        if (!Write(run.Record(run.State.WithRoute(sourceKey, destinationKey, string.Empty)), "name the area and the chest")
            || !WalkToExecuting(measured, "the load he already carried was measured at the start, not fetched"))
        {
            sentence = _sentence;
            return false;
        }

        sentence = string.Empty;
        return true;
    }

    private bool ResumeReplanned(
        NpcWorldEpoch world, string sourceKey, string destinationKey,
        IReadOnlyList<NpcMaterialStack> measured, out string sentence)
    {
        NpcPlanRun run = _run!;
        if (!SameLoad(run.State.Carried, measured))
        {
            Attention(InterruptionCause.TransferUncertain, Mismatch(run.State.Carried, measured));
            sentence = _sentence;
            return false;
        }

        _standing = TourPlanStanding.Running;
        if (!Write(run.Record(run.State.WithRoute(sourceKey, destinationKey, string.Empty)),
                "rewrite the area and the chest chosen in this world load")
            || (!run.State.World.Matches(world)
                && !Write(run.Reattach(world, "the area and the chest were chosen again in this world load"),
                    "re-attach the plan to this world load"))
            || !WalkToExecuting(measured, "the load he already carried was measured again, not fetched"))
        {
            sentence = _sentence;
            return false;
        }

        sentence = "the interrupted tour carries on: " + Progress(run.State) + "; what he carries goes to the chest first";
        return true;
    }

    private bool Continue(IReadOnlyList<NpcMaterialStack> measured, out string sentence)
    {
        NpcPlanRun run = _run!;
        if (!SameLoad(run.State.Carried, measured))
        {
            Attention(InterruptionCause.TransferUncertain, Mismatch(run.State.Carried, measured));
            sentence = _sentence;
            return false;
        }

        _standing = TourPlanStanding.Running;
        if (!WalkToExecuting(measured, "the load he already carried was measured again, not fetched"))
        {
            sentence = _sentence;
            return false;
        }

        sentence = "the stopped tour carries on: " + Progress(run.State);
        return true;
    }

    /// <summary>Walks the pipeline forward to <see cref="NpcPlanPhase.Executing"/>
    /// from wherever the plan is. The one phase the library treats as moving
    /// material on the way, Provisioned, is reached through a measured no-op:
    /// an intent to restate what he holds, and a conclusion recording the
    /// measurement. Nothing moves; the record says so.</summary>
    private bool WalkToExecuting(IReadOnlyList<NpcMaterialStack> measured, string restated)
    {
        NpcPlanRun run = _run!;
        for (int guard = 0; guard < 12 && run.State.Phase != NpcPlanPhase.Executing; guard++)
        {
            bool written;
            switch (run.State.Phase)
            {
                case NpcPlanPhase.Observing:
                    written = Write(run.Advance(NpcPlanPhase.Planned, "the area and the chest are named"), "record the plan");
                    break;
                case NpcPlanPhase.Planned:
                    written = Write(
                        run.Advance(NpcPlanPhase.Manifested, "the tour is walked in loads he can carry"),
                        "record the loads");
                    break;
                case NpcPlanPhase.Manifested:
                    written = Write(
                        run.Advance(NpcPlanPhase.Reserved, "a tour sets nothing aside: what it collects is found"),
                        "record the empty reservation");
                    break;
                case NpcPlanPhase.Reserved:
                    written = Write(run.Intend("about to restate what he already carries; nothing moves"),
                            "record the restatement")
                        && Write(run.Conclude(true, measured, run.State.TargetsDone, restated), "record the measurement")
                        && Write(run.Advance(NpcPlanPhase.Provisioned, "what he carries is on him, measured"),
                            "record the measured load");
                    break;
                case NpcPlanPhase.Provisioned:
                    written = Write(run.Advance(NpcPlanPhase.Routed, "the walk is ordered stop by stop"), "record the route");
                    break;
                case NpcPlanPhase.Routed:
                    written = Write(run.Advance(NpcPlanPhase.Executing, "he is collecting"), "record the start of work");
                    break;
                default:
                    Block("Gunnar's tour plan is in a phase a tour cannot start from (" + run.State.Phase +
                        "), so nothing moves. Run ct_collect resolve.");
                    return false;
            }

            if (!written)
            {
                return false;
            }
        }

        if (run.State.Phase != NpcPlanPhase.Executing)
        {
            Block("Gunnar's tour plan could not reach the point where he works, so nothing moves.");
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // The two movements: a pick, and a trip to the chest
    // ------------------------------------------------------------------

    /// <summary>Announces a pick before the port is asked for one. <b>False means
    /// do not pick</b>: a pick nothing wrote down is a pick nothing could say
    /// later had happened. The source counts as started from here.</summary>
    internal bool IntendPick(string note, out string failure)
    {
        if (!MayMove(out failure))
        {
            return false;
        }

        NpcPlanState now = _run!.State;
        var announced = new NpcPlanState(
            now.Identity, now.JobId, now.Phase, NpcPlanCustody.Pending, now.Reservations, now.Carried,
            now.TargetsDone, now.TargetsDone + 1, now.SourceKey, now.DestinationKey, now.VehicleKey,
            now.World, now.Attempt, note);
        _depositPending = false;
        if (!Write(_run.Record(announced), "write down the pick before it happens"))
        {
            failure = _sentence;
            return false;
        }

        return true;
    }

    /// <summary>Records what a pick came to: what he holds now, measured, and
    /// the source as worked - whether anything reached him or not. A
    /// measurement that could not be read is an outcome nobody can establish,
    /// and the plan stops for a person.</summary>
    internal bool ConcludePick(IReadOnlyList<KeyValuePair<string, int>>? carried, string note, out string failure) =>
        Conclude(carried, worked: true, certain: true, note, "after a pick", out failure);

    /// <summary>Announces a trip's deposit before any leg of it is attempted.
    /// <b>False means do not deposit.</b></summary>
    internal bool IntendDeposit(string note, out string failure)
    {
        if (!MayMove(out failure))
        {
            return false;
        }

        _depositPending = true;
        if (!Write(_run!.Intend(note), "write down the trip to the chest before it happens"))
        {
            failure = _sentence;
            return false;
        }

        return true;
    }

    /// <summary>Records what a trip to the chest came to. <b>Established only
    /// when every leg was certain and what he still holds afterwards could be
    /// read</b>; anything else stops the plan for a person, and the next start
    /// will not deposit again until somebody has looked.</summary>
    internal bool ConcludeDeposit(
        bool tallyCertain, IReadOnlyList<KeyValuePair<string, int>>? carriedAfter, string note, out string failure) =>
        Conclude(carriedAfter, worked: false, certain: tallyCertain, note, "after a trip to the chest", out failure);

    private bool Conclude(
        IReadOnlyList<KeyValuePair<string, int>>? carried, bool worked, bool certain, string note, string when,
        out string failure)
    {
        failure = string.Empty;
        NpcPlanRun? run = _run;
        if (run == null || _standing != TourPlanStanding.Running || run.State.Custody != NpcPlanCustody.Pending)
        {
            failure = "nothing was announced, so there is nothing to conclude";
            return false;
        }

        IReadOnlyList<NpcMaterialStack>? measured = Measured(carried);
        bool established = certain && measured != null;
        int done = worked ? run.State.TargetsTotal : run.State.TargetsDone;
        _depositPending = false;
        if (!Write(run.Conclude(established, measured ?? run.State.Carried, done, note), "record what moved " + when))
        {
            failure = _sentence;
            return false;
        }

        if (!established)
        {
            Attention(
                InterruptionCause.TransferUncertain,
                (worked
                    ? "Whether anything reached Gunnar from the last source could not be established - what he holds "
                    : "Whether what Gunnar brought went into the chest could not be established - ") +
                "so nothing is repeated and nothing is assumed. Look in his inventory and in the chest, then run " +
                "ct_collect resolve: it records what he holds now, and nothing is deposited again until then.");
            failure = _sentence;
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // The end of a tour
    // ------------------------------------------------------------------

    /// <summary>The tour ended, however it ended. Finished and empty settles the
    /// plan; stopped and empty closes it as refunded; still carrying, the plan
    /// stays open with the reason, so the next start takes the load to the chest
    /// first. A pick the tour abandoned in flight is concluded from a
    /// measurement; a trip to the chest abandoned in flight is not, because what
    /// reached the chest is exactly what nobody measured.</summary>
    /// <returns>False when a write this needed was not saved - the plan then
    /// waits for a person.</returns>
    internal bool Ended(bool finished, IReadOnlyList<KeyValuePair<string, int>>? carried, string reason)
    {
        NpcPlanRun? run = _run;
        if (run == null || _standing != TourPlanStanding.Running)
        {
            return true;
        }

        string why = string.IsNullOrEmpty(reason) ? "the tour ended" : reason;
        if (run.State.Custody == NpcPlanCustody.Pending)
        {
            bool deposit = _depositPending;
            if (!Conclude(carried, worked: !deposit, certain: !deposit, "the tour ended (" + why + ")",
                    deposit ? "when a trip to the chest was cut off" : "when a pick was cut off", out _))
            {
                return false;
            }
        }

        IReadOnlyList<NpcMaterialStack>? measured = Measured(carried);
        if (measured == null)
        {
            // The last conclusion was measured, and nothing has moved since
            // without being announced. The next start measures again and stops
            // for a person if the two disagree.
            Suspend("the tour ended (" + why + ") and what he carries could not be measured; the next start " +
                "measures it again");
            return true;
        }

        if (!SameLoad(run.State.Carried, measured))
        {
            Attention(InterruptionCause.TransferUncertain, Mismatch(run.State.Carried, measured));
            return false;
        }

        if (measured.Count != 0)
        {
            Suspend("the tour ended (" + why + ") with " + Describe(measured) + " still on him; the next start takes " +
                "it to the chest first");
            return true;
        }

        if (finished)
        {
            if (!Write(run.Intend("about to close the books from his measured, empty hands"), "record the closing")
                || !Write(run.Conclude(true, Nothing, run.State.TargetsDone, "he carries nothing, measured"),
                    "record the empty hands")
                || !Write(run.Advance(NpcPlanPhase.Reconciling, "every load went to the chest"), "record the reconciling")
                || !Write(run.Stop(NpcPlanPhase.Settled, "the tour finished: " + Progress(run.State)), "settle the tour"))
            {
                return false;
            }
        }
        else if (!Write(run.Stop(NpcPlanPhase.Refunded, "the tour stopped (" + why + ") holding nothing"),
                     "close the stopped tour"))
        {
            return false;
        }

        ToIdle();
        return true;
    }

    // ------------------------------------------------------------------
    // D16: he died
    // ------------------------------------------------------------------

    /// <summary>Called from the death host before vanilla's drop runs. Writes an
    /// intent so the drop's outcome can be concluded. <b>It never refuses
    /// anything</b>: a failed write here leaves the plan to stop for a person
    /// afterwards, and never stands between a dead body and its drop.</summary>
    internal void PrepareForDeath()
    {
        NpcPlanRun? run = _run;
        if (run == null || IsTerminal(run.State.Phase) || _standing == TourPlanStanding.NeedsAttention)
        {
            return;
        }

        try
        {
            run.Intend("Gunnar died; vanilla's own drop is about to run for what he carries");
        }
        catch (Exception)
        {
            // Never in the way of the drop.
        }
    }

    /// <summary>The drop ran. A full, measured drop of exactly what the plan
    /// says he carried closes the plan as refunded - what he held is on the
    /// ground where he fell, recorded in the death file. Anything else - a
    /// unit unresolved, a count that differs, an intent that was not written -
    /// stops it for a person.</summary>
    /// <param name="held">What the death report found on him.</param>
    /// <param name="dropped">What it measured onto the ground.</param>
    /// <param name="unresolved">What it could not account for.</param>
    /// <param name="record">Where the death rows were kept, in words.</param>
    internal void Died(int held, int dropped, int unresolved, string record)
    {
        NpcPlanRun? run = _run;
        if (run == null || IsTerminal(run.State.Phase))
        {
            return;
        }

        int recorded = run.State.CarriedUnits;
        string kept = string.IsNullOrEmpty(record) ? "the death record is in the log" : record;
        if (_standing != TourPlanStanding.NeedsAttention
            && run.State.Custody == NpcPlanCustody.Pending && unresolved == 0 && held == recorded && dropped == held)
        {
            _depositPending = false;
            if (Write(run.Conclude(true, Nothing, run.State.TargetsTotal,
                        "vanilla dropped all " + held.ToString(CultureInfo.InvariantCulture) + " where he fell"),
                    "record the drop")
                && Write(run.Stop(NpcPlanPhase.Refunded,
                        "Gunnar died and vanilla dropped everything he carried (" +
                        held.ToString(CultureInfo.InvariantCulture) + ") where he fell; " + kept + "."),
                    "close the tour"))
            {
                ToIdle();
            }

            return;
        }

        Attention(
            InterruptionCause.BodyDied,
            "Gunnar died. The tour's plan says he carried " + recorded.ToString(CultureInfo.InvariantCulture) +
            "; the death drop found " + held.ToString(CultureInfo.InvariantCulture) + ", put " +
            dropped.ToString(CultureInfo.InvariantCulture) + " on the ground and could not account for " +
            unresolved.ToString(CultureInfo.InvariantCulture) + " (" + kept + "). Nothing was recreated. Look " +
            "where he fell, then run ct_collect resolve.");
    }

    // ------------------------------------------------------------------
    // ct_collect resolve: the person's exit
    // ------------------------------------------------------------------

    /// <summary>The explicit human exit from a plan stopped for a person. A
    /// person has looked; this records what his body holds now and nothing else.
    ///
    /// <list type="bullet">
    /// <item>Needs a live world, exactly one Gunnar, and a readable measurement:
    /// a resolution recorded against an unknown amount is a guess.</item>
    /// <item>An unreadable plan file is moved aside as <c>.corrupt</c> first, and
    /// if it cannot be moved nothing is written over it.</item>
    /// <item>Empty-handed: a new plan begun and closed at once as refunded - a
    /// tombstone, so the next load knows a person resolved it.</item>
    /// <item>Carrying something: a new plan, its load the measurement, stopped
    /// where a tour would continue it - so the next start takes it to the chest.
    /// </item>
    /// </list>
    ///
    /// <b>It never recreates material</b>: nothing here moves anything, and the
    /// load it records is what was measured on him.</summary>
    internal bool Resolve(in TourPlanFacts facts, bool busy, out string sentence)
    {
        if (_journal == null)
        {
            sentence = "Nothing can be resolved: " + _closedReason + ".";
            return false;
        }

        if (_standing != TourPlanStanding.NeedsAttention)
        {
            sentence = "There is nothing to resolve: " + Describe() + ".";
            return false;
        }

        if (busy)
        {
            sentence = "He is doing something; let it finish or stop it before resolving.";
            return false;
        }

        if (facts.World.IsUnknown)
        {
            sentence = "Nothing can be resolved without a loaded world.";
            return false;
        }

        if (facts.BodiesAnswering != 1)
        {
            sentence = "Resolving records what Gunnar holds, so it needs exactly one Gunnar here to measure " +
                "(found " + facts.BodiesAnswering.ToString(CultureInfo.InvariantCulture) + ").";
            return false;
        }

        IReadOnlyList<NpcMaterialStack>? measured = Measured(facts.Carried);
        if (measured == null)
        {
            sentence = "What Gunnar holds could not be read, so nothing is resolved - a resolution recorded " +
                "against an unknown amount would be a guess.";
            return false;
        }

        string moved = string.Empty;
        if (_unreadable)
        {
            string? quarantined = _journal.TryQuarantine();
            if (quarantined == null)
            {
                sentence = "The unreadable tour plan could not be moved aside, so nothing was written over it.";
                return false;
            }

            _unreadable = false;
            moved = " The unreadable plan was kept beside it as a .corrupt copy.";
        }

        NpcPlanRun? run = NpcPlanRun.Begin(
            _journal, NpcPlanState.Opening(_identity, JobId, facts.World, 0), out NpcPlanSave begun);
        if (run == null)
        {
            sentence = SaveFailure("record the resolution", begun);
            return false;
        }

        _run = run;
        if (measured.Count == 0)
        {
            if (!Write(run.Stop(NpcPlanPhase.Refunded,
                    "a person resolved the stopped tour; he held nothing, and nothing was recreated"),
                    "close the resolved tour"))
            {
                sentence = _sentence;
                return false;
            }

            ToIdle();
            sentence = "Resolved: he holds nothing, so the old tour is closed." + moved;
            return true;
        }

        _standing = TourPlanStanding.Running;
        if (!WalkToExecuting(measured, "a person resolved the stopped tour; this is what he holds, measured"))
        {
            sentence = _sentence;
            return false;
        }

        Suspend("a person resolved the stopped tour; the next start takes what he holds to the chest");
        sentence = "Resolved: he holds " + Describe(measured) + ", recorded as measured. The next ct_collect start " +
            "takes it to the chest first; nothing was recreated." + moved;
        return true;
    }

    // ------------------------------------------------------------------
    // Saying where things stand
    // ------------------------------------------------------------------

    /// <summary>One line for <c>ct_collect status</c>.</summary>
    internal string Describe()
    {
        NpcPlanState? state = _run?.State;
        switch (_standing)
        {
            case TourPlanStanding.Closed:
                return "no tour plan (" + _closedReason + ")";
            case TourPlanStanding.Idle:
                return "no interrupted tour";
            case TourPlanStanding.Recovered:
                return "an interrupted tour from the last session is waiting to be checked" +
                    (state == null ? string.Empty : " (the plan says " + Describe(state.Carried) + ")");
            case TourPlanStanding.Replanned:
                return "an interrupted tour is waiting: he carries " + Describe(state?.Carried) + "; choose the chest " +
                    "and the area again and ct_collect start takes it to the chest first";
            case TourPlanStanding.Suspended:
                return "a stopped tour is open: he carries " + Describe(state?.Carried) + "; ct_collect start carries " +
                    "it on";
            case TourPlanStanding.Running:
                return "the tour's plan is recording (" + (state == null ? "?" : Progress(state)) + ")";
            default:
                return "the tour is stopped for you to look at: " + _sentence;
        }
    }

    /// <summary>What he holds as plain data, as the plan records it: one stack
    /// per material, positive counts, in name order. <b>Null when it could not
    /// be read</b> - a null input, a nameless material or a negative count.
    /// </summary>
    internal static IReadOnlyList<NpcMaterialStack>? Measured(IReadOnlyList<KeyValuePair<string, int>>? carried)
    {
        if (carried == null)
        {
            return null;
        }

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < carried.Count; index++)
        {
            KeyValuePair<string, int> stack = carried[index];
            if (string.IsNullOrEmpty(stack.Key) || stack.Value < 0)
            {
                return null;
            }

            if (stack.Value == 0)
            {
                continue;
            }

            totals.TryGetValue(stack.Key, out int already);
            totals[stack.Key] = already + stack.Value;
        }

        var stacks = new List<NpcMaterialStack>(totals.Count);
        foreach (KeyValuePair<string, int> total in totals)
        {
            stacks.Add(new NpcMaterialStack(NpcMaterial.Of(total.Key), total.Value));
        }

        return stacks;
    }

    /// <summary>The area as a source key: centre, radius and revision, invariant.
    /// Evidence for a person after a reload, never resolved into anything.
    /// </summary>
    internal static string AreaKey(CollectionArea area) =>
        area == null
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                "area {0:0.0},{1:0.0},{2:0.0} r{3:0.#} rev{4}",
                area.Centre.X, area.Centre.Y, area.Centre.Z, area.BoundingRadiusMetres, area.Revision);

    internal static string Describe(IReadOnlyList<NpcMaterialStack>? stacks)
    {
        if (stacks == null)
        {
            return "an unknown amount";
        }

        var text = new StringBuilder();
        for (int index = 0; index < stacks.Count; index++)
        {
            if (!stacks[index].IsValid)
            {
                continue;
            }

            text.Append(text.Length == 0 ? string.Empty : ", ")
                .Append(stacks[index].Count.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(stacks[index].Material.ItemName);
        }

        return text.Length == 0 ? "nothing" : text.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private bool MayMove(out string failure)
    {
        if (_run == null || _standing != TourPlanStanding.Running)
        {
            failure = "no tour plan is running, so nothing may move";
            return false;
        }

        if (!_run.MayAct || _run.State.Phase != NpcPlanPhase.Executing)
        {
            failure = "the tour plan is not in a state where anything may move";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private TourPlanVerdict Over(NpcPlanRun run, NpcPlanState plan, in TourPlanFacts facts)
    {
        var evidence = new NpcPlanEvidence(
            facts.World, facts.BodiesAnswering, false, true, false, facts.MayWork, true, true, false, facts.At);
        NpcPlanRecovered decided = NpcPlanRecovery.Revalidate(plan, evidence, NpcWorkInterruptionPolicy.Instance);
        if (decided.Next.Phase != plan.Phase || decided.Next.Custody != plan.Custody)
        {
            NpcPlanSave corrected = run.Adopt(decided.Next, decided.Outcome);
            if (!corrected.IsSaved)
            {
                Block(SaveFailure("correct the ending of the last tour", corrected));
                return new TourPlanVerdict(InterruptionResponse.NeedsAttention, decided.Outcome.Cause, _sentence);
            }
        }

        if (decided.Next.Phase == NpcPlanPhase.NeedsAttention)
        {
            Block("The last tour's record says it ended while a pick or a trip to the chest had no recorded " +
                "outcome, so it is not treated as finished. Look in Gunnar's inventory and the chest, then run " +
                "ct_collect resolve.");
            return new TourPlanVerdict(InterruptionResponse.NeedsAttention, decided.Outcome.Cause, _sentence);
        }

        ToIdle();
        return new TourPlanVerdict(decided.Outcome.Response, decided.Outcome.Cause, decided.Outcome.Reason);
    }

    private TourPlanVerdict Attention(InterruptionCause cause, string sentence)
    {
        NpcPlanRun? run = _run;
        if (run != null && run.State.Phase != NpcPlanPhase.NeedsAttention)
        {
            NpcPlanSave stopped = run.Stop(NpcPlanPhase.NeedsAttention, sentence);
            Block(stopped.IsSaved ? sentence : sentence + " The stop could not be written down either (" +
                stopped.Failure + ").");
        }
        else
        {
            Block(sentence);
        }

        return new TourPlanVerdict(InterruptionResponse.NeedsAttention, cause, _sentence);
    }

    private void Suspend(string note)
    {
        // A failed note leaves the last record, which is already a safe resume
        // point: nothing moved since it was written. So it is not a stop.
        _run?.Suspend(note);
        _standing = TourPlanStanding.Suspended;
        _sentence = note;
    }

    private TourPlanStanding Unreadable(string failure)
    {
        _unreadable = true;
        Block("Gunnar's tour plan file could not be read (" + (string.IsNullOrEmpty(failure) ? "no reason given" : failure) +
            "). It was left exactly as it is. Look at what he carries, then run ct_collect resolve - it moves the " +
            "file aside as a .corrupt copy before writing anything.");
        return _standing;
    }

    private void Block(string sentence)
    {
        _standing = TourPlanStanding.NeedsAttention;
        _sentence = string.IsNullOrEmpty(sentence)
            ? "Gunnar's tour stopped without a recorded reason; run ct_collect resolve."
            : sentence;
    }

    private void ToIdle()
    {
        _run = null;
        _standing = TourPlanStanding.Idle;
        _sentence = string.Empty;
        _depositPending = false;
    }

    private bool Write(NpcPlanSave save, string action)
    {
        if (save.IsSaved)
        {
            return true;
        }

        Block(SaveFailure(action, save));
        return false;
    }

    private static string SaveFailure(string action, NpcPlanSave save) =>
        "Gunnar's tour plan could not " + action + " (" + save.Failure + "), so nothing more moves. What he " +
        "holds stays in him; run ct_collect resolve once it can be written." +
        (save.CompleteCopyPath == null ? string.Empty : " A complete temporary copy was kept beside the plan.");

    private static string Sentence(InterruptionResponse response, InterruptionCause cause, NpcPlanState plan)
    {
        switch (response)
        {
            case InterruptionResponse.Continue:
                return "The stopped tour carries on from where it was: " + Progress(plan) + ".";

            case InterruptionResponse.Replan:
                return cause == InterruptionCause.AreaChanged
                    ? "The area or the chest was chosen again, so the rest of the tour is worked out again from " +
                      "there: " + Progress(plan) + "; what he carries goes to the chest first."
                    : "The world was loaded since this tour stopped. He still carries " + Describe(plan.Carried) +
                      ", as the plan says; choose the chest and the area again, and ct_collect start takes it to " +
                      "the chest first. Progress kept: " + Progress(plan) + ".";

            case InterruptionResponse.Refund:
                return "The interrupted tour held nothing, so it is closed; nothing was moved.";
        }

        switch (cause)
        {
            case InterruptionCause.BodyDuplicated:
                return "Two bodies answer to Gunnar, so the interrupted tour is not touched: acting would act on an " +
                    "arbitrary one of them. Deal with the second body first (ct_haul status says what it found), then " +
                    "run ct_collect resolve.";
            case InterruptionCause.TransferUncertain:
                return "A pick or a trip to the chest was in flight when the tour was interrupted, and nothing " +
                    "recorded what became of it - so it is neither repeated nor forgotten. Look in Gunnar's " +
                    "inventory and in the chest, then run ct_collect resolve: it records what he holds now, and " +
                    "nothing is deposited again until then.";
            case InterruptionCause.BodyLost:
                return "Gunnar's body is gone - removed with ct_haul retire force, or lost - while the tour's plan " +
                    "says he carried " + Describe(plan.Carried) + ". Nothing was recreated. Once he is back, run " +
                    "ct_collect resolve to record what he holds now.";
            case InterruptionCause.AuthorityLost:
                return "Work may not run here now, and the tour cannot simply be closed while he carries " +
                    Describe(plan.Carried) + ". Run ct_collect resolve when it may.";
            default:
                return "The interrupted tour cannot be continued or closed on its own (" + cause + "), so it is " +
                    "stopped for you to look at. He carries " + Describe(plan.Carried) + " by the plan. Run " +
                    "ct_collect resolve once you have looked.";
        }
    }

    private static string Mismatch(IReadOnlyList<NpcMaterialStack> recorded, IReadOnlyList<NpcMaterialStack> measured) =>
        "The tour's plan says Gunnar carried " + Describe(recorded) + ", but he holds " + Describe(measured) +
        " now. Nothing was replayed or replaced. If the game was closed without saving, this may be a crash " +
        "rollback - the world went back to its last save while the plan did not. Look at him and at the chest, " +
        "then run ct_collect resolve.";

    private static string Progress(NpcPlanState plan) =>
        plan.TargetsDone.ToString(CultureInfo.InvariantCulture) + " source(s) worked, carrying " +
        Describe(plan.Carried);

    private static bool SameLoad(IReadOnlyList<NpcMaterialStack> recorded, IReadOnlyList<NpcMaterialStack> measured)
    {
        if (recorded.Count != measured.Count)
        {
            return false;
        }

        for (int index = 0; index < recorded.Count; index++)
        {
            if (!recorded[index].Equals(measured[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTerminal(NpcPlanPhase phase) =>
        phase == NpcPlanPhase.Settled || phase == NpcPlanPhase.Refunded || phase == NpcPlanPhase.NeedsAttention;

    /// <summary>The phase a decision leaves a plan in. The library's own table
    /// is internal to it, and <c>NpcPlanRun.Adopt</c> refuses any other answer,
    /// so a copy that drifted would be refused rather than written.</summary>
    private static NpcPlanPhase PhaseAfter(InterruptionResponse response, NpcPlanPhase current)
    {
        switch (response)
        {
            case InterruptionResponse.Continue:
                return current;
            case InterruptionResponse.Replan:
                return NpcPlanPhase.Observing;
            case InterruptionResponse.Refund:
                return NpcPlanPhase.Refunded;
            default:
                return NpcPlanPhase.NeedsAttention;
        }
    }
}
