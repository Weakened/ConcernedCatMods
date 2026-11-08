using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Where one of Thorstein's shared-interruption plans stands.</summary>
internal enum ThorsteinPlanStanding
{
    /// <summary>No journal is open: no world, or it could not be opened.
    /// Work that needs a plan refuses.</summary>
    Closed = 0,

    /// <summary>A journal is open and no plan is running or owed.</summary>
    Idle = 1,

    /// <summary>A plan is running: movements are announced and concluded.
    /// </summary>
    Running = 2,

    /// <summary>The plan is open and waiting, in this session: paused, or
    /// ended carrying something. The next start continues it.</summary>
    Suspended = 3,

    /// <summary>Read back from the disk after a load and not yet revalidated.
    /// Nothing moves until it is.</summary>
    Recovered = 4,

    /// <summary>Revalidated after a load: the world moved under it, so the
    /// next start re-attaches it to this world load and continues.</summary>
    Replanned = 5,

    /// <summary>Stopped for a person. Nothing moves until it is resolved.
    /// </summary>
    NeedsAttention = 6,
}

/// <summary>Thorstein's adoption of the shared interruption layer (#379): the
/// write-ahead witness ConcernedNPC's <see cref="NpcPlanRun"/> keeps around his
/// two real executors, the collection loop and the construction loop, one plan
/// per job.
///
/// <b>The witness, not the executor, and not the custody truth.</b> This class
/// never walks, picks, fells, draws, places or deposits. Each executor asks it to
/// write an intent down before it touches the world, and records the measured
/// outcome after. Foreman's own marker-backed settlement journal stays the only
/// material-custody truth (DECISIONS.md D8, CONTRACTS.md §5.5): its rows, their
/// order and their voiding rule are unchanged, and this plan's intent is written
/// before the executor's first journal row and its conclusion after the
/// executor's last. What the plan says he carries is evidence, re-measured at
/// every start, never a claim against the ledger.
///
/// <b>What it adds.</b> A movement in flight when the process died is found on
/// the next load as an intent with no outcome, and the shared recovery policy
/// answers it NeedsAttention: that movement is never replayed, and nothing more
/// moves until a person resolves it. Anything else is revalidated against the
/// world on the next start - a reload is a re-plan, a pause continues - and is
/// continued with the same job id rather than begun again, so an interrupted
/// order resumes rather than restarts.
///
/// <b>Game-free</b>, so ConcernedForeman.Tests runs it against real plan files.
/// The product build proves it uses only the library's public surface.</summary>
internal sealed class ThorsteinWorkPlan
{
    /// <summary>The collection loop's job id. One per Thorstein per settlement;
    /// never numbered, because a numbered id is a new plan every time and a
    /// reload would find none of them.</summary>
    internal const string CollectJobId = "foreman-collect";

    /// <summary>The construction loop's job id.</summary>
    internal const string BuildJobId = "foreman-build";

    private static readonly NpcMaterialStack[] Nothing = new NpcMaterialStack[0];

    private readonly NpcIdentity _identity;
    private readonly string _jobId;
    private readonly INpcPlanCodec _codec;

    private NpcPlanJournal? _journal;
    private NpcPlanRun? _run;
    private ThorsteinPlanStanding _standing = ThorsteinPlanStanding.Closed;
    private string _sentence = string.Empty;
    private string _closedReason = "no world is loaded";
    private bool _unreadable;

    internal ThorsteinWorkPlan(NpcIdentity identity, string jobId, INpcPlanCodec? codec = null)
    {
        if (identity.IsEmpty)
        {
            throw new ArgumentException("Thorstein's identity is required.", nameof(identity));
        }

        if (!string.Equals(jobId, CollectJobId, StringComparison.Ordinal) &&
            !string.Equals(jobId, BuildJobId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Thorstein has two jobs: " + CollectJobId + " and " + BuildJobId + ".", nameof(jobId));
        }

        _identity = identity;
        _jobId = jobId;
        _codec = codec ?? new ThorsteinPlanCodec(identity, jobId);
    }

    internal string JobId => _jobId;

    internal ThorsteinPlanStanding Standing => _standing;

    internal NpcPlanState? State => _run?.State;

    internal bool IsRunning => _standing == ThorsteinPlanStanding.Running;

    /// <summary>Why work that needs this plan may not start or go on: a person
    /// must look, a record read back has not been revalidated yet, or no record
    /// can be written. Empty when nothing blocks.</summary>
    internal string BlockedReason => _standing switch
    {
        ThorsteinPlanStanding.Closed => "Thorstein's work plan is not open (" + _closedReason + "), so he does not work",
        ThorsteinPlanStanding.NeedsAttention => _sentence,
        _ => string.Empty,
    };

    internal string LastSentence => _sentence;

    // ------------------------------------------------------------------
    // The world-up edge, and the world-down edge
    // ------------------------------------------------------------------

    /// <summary>Opens this settlement's journal for this job and reads what is
    /// there. No material moves and nothing is decided: a plan that was open
    /// becomes <see cref="ThorsteinPlanStanding.Recovered"/> and waits to be
    /// revalidated by the next start.</summary>
    internal ThorsteinPlanStanding Open(string? path, string? whyNoPath)
    {
        Close(string.Empty);
        if (path == null)
        {
            _closedReason = string.IsNullOrEmpty(whyNoPath) ? "no settlement could be named" : whyNoPath!;
            return _standing;
        }

        if (!NpcPlanJournal.TryOpen(path, _codec, out NpcPlanJournal? journal, out string failure))
        {
            _closedReason = "the plan could not be opened (" + failure + ")";
            return _standing;
        }

        _journal = journal;
        _standing = ThorsteinPlanStanding.Idle;
        NpcPlanLoad loaded = journal!.Load();
        if (loaded.IsAbsent)
        {
            return _standing;
        }

        if (!loaded.IsLoaded || loaded.Plan == null)
        {
            _unreadable = true;
            return Attention("Thorstein's " + Job + " plan could not be read (" + loaded.Failure + "). It was left " +
                "exactly as it is; look at what he carries, then resolve it.");
        }

        _run = NpcPlanRun.Resume(journal, loaded.Plan);
        if (_run == null)
        {
            _unreadable = true;
            return Attention("Thorstein's " + Job + " plan named no resumable work, so nothing was replayed; resolve it.");
        }

        if (loaded.Plan.Phase == NpcPlanPhase.NeedsAttention)
        {
            return Attention("Thorstein's " + Job + " plan was stopped for a person before the reload (" +
                loaded.Plan.Note + "); resolve it.");
        }

        if ((loaded.Plan.Phase == NpcPlanPhase.Settled || loaded.Plan.Phase == NpcPlanPhase.Refunded) &&
            loaded.Plan.Custody == NpcPlanCustody.Clear)
        {
            // A finished plan: nothing is owed, and the next start begins anew.
            _run = null;
            return _standing;
        }

        _standing = ThorsteinPlanStanding.Recovered;
        return _standing;
    }

    /// <summary>The world went away. In-memory state is dropped; the file keeps
    /// the last thing written, which is the safe resume point.</summary>
    internal void Close(string why)
    {
        _journal = null;
        _run = null;
        _unreadable = false;
        _sentence = string.Empty;
        _standing = ThorsteinPlanStanding.Closed;
        _closedReason = string.IsNullOrEmpty(why) ? "no world is loaded" : why;
    }

    // ------------------------------------------------------------------
    // Starting, and continuing what an interruption left
    // ------------------------------------------------------------------

    /// <summary>Starts or continues the plan for one stretch of work.
    /// <list type="bullet">
    /// <item>Idle: a new plan, walked to Executing with what he carries now
    /// measured, not fetched.</item>
    /// <item>Recovered: revalidated first with the shared policy against
    /// <paramref name="evidence"/>. A movement whose outcome was never written
    /// stops for a person; a refund of material he still holds stops for a
    /// person; a re-plan continues; a plan that held nothing and is refunded
    /// closes and a new one begins.</item>
    /// <item>Replanned or Suspended: continued with the same job id, re-attached
    /// to this world load, its load re-measured.</item>
    /// <item>NeedsAttention or Closed: refused.</item>
    /// </list>
    /// False means do not work. Nothing here moves material.</summary>
    internal bool Start(
        in NpcPlanEvidence evidence, string sourceKey, string destinationKey,
        IReadOnlyList<KeyValuePair<string, int>>? measured, out string sentence)
    {
        if (_standing == ThorsteinPlanStanding.Running)
        {
            sentence = string.Empty;
            return true;
        }

        if (_standing == ThorsteinPlanStanding.Closed || _standing == ThorsteinPlanStanding.NeedsAttention)
        {
            sentence = BlockedReason;
            return false;
        }

        IReadOnlyList<NpcMaterialStack>? load = Measured(measured);
        if (load == null || evidence.World.IsUnknown)
        {
            sentence = "Thorstein's " + Job + " cannot start: what he carries could not be measured, or no world is loaded.";
            return false;
        }

        if (_standing == ThorsteinPlanStanding.Recovered && !Revalidate(evidence))
        {
            sentence = _sentence;
            return false;
        }

        if (_standing == ThorsteinPlanStanding.Idle)
        {
            return BeginFresh(evidence.World, sourceKey, destinationKey, load, out sentence);
        }

        // Replanned or Suspended: the same plan, carried on.
        NpcPlanRun run = _run!;
        _standing = ThorsteinPlanStanding.Running;
        if ((!run.State.World.Matches(evidence.World) &&
                !Write(run.Reattach(evidence.World, "taken up again in this world load"), "re-attach the plan to this world load")) ||
            !Write(run.Record(run.State.WithRoute(sourceKey, destinationKey, string.Empty)), "name this stretch of work") ||
            !WalkToExecuting(load, "what he carries was measured again when the work resumed, not fetched") ||
            !Restate(load, "what he carries was measured again when the work resumed, not fetched"))
        {
            sentence = _sentence;
            return false;
        }

        sentence = "Thorstein carries on the interrupted " + Job + " rather than starting it again.";
        return true;
    }

    private bool Revalidate(in NpcPlanEvidence evidence)
    {
        NpcPlanRun run = _run!;
        NpcPlanState recovered = run.State;
        NpcPlanRecovered decision = NpcPlanRecovery.Revalidate(recovered, evidence, NpcWorkInterruptionPolicy.Instance);
        if (decision.WasAlreadyOver)
        {
            if ((decision.Next.Phase != recovered.Phase || decision.Next.Custody != recovered.Custody) &&
                !Write(run.Adopt(decision.Next, decision.Outcome), "correct its ending"))
            {
                return false;
            }

            if (decision.Next.Phase == NpcPlanPhase.NeedsAttention)
            {
                Attention(decision.Outcome.Reason);
                return false;
            }

            _run = null;
            _standing = ThorsteinPlanStanding.Idle;
            return true;
        }

        if (decision.Outcome.Response == InterruptionResponse.Refund && recovered.CarriedUnits > 0)
        {
            // A refund only releases material that never moved. Accepting it
            // while he still holds material would turn refund into disposal.
            var held = new InterruptionOutcome(
                InterruptionResponse.NeedsAttention,
                decision.Outcome.Cause,
                0f,
                "the interruption cannot refund material Thorstein already carries; somebody must look");
            if (Write(run.Adopt(recovered.WithPhase(NpcPlanPhase.NeedsAttention, held.Reason), held), "record the stop"))
            {
                Attention(held.Reason);
            }

            return false;
        }

        if (!Write(run.Adopt(decision.Next, decision.Outcome), "record the recovery decision"))
        {
            return false;
        }

        switch (decision.Outcome.Response)
        {
            case InterruptionResponse.Continue:
                _standing = ThorsteinPlanStanding.Suspended;
                return true;

            case InterruptionResponse.Replan:
                _standing = ThorsteinPlanStanding.Replanned;
                return true;

            case InterruptionResponse.Refund:
                _run = null;
                _standing = ThorsteinPlanStanding.Idle;
                return true;

            default:
                Attention(decision.Outcome.Reason);
                return false;
        }
    }

    private bool BeginFresh(
        NpcWorldEpoch world, string sourceKey, string destinationKey, IReadOnlyList<NpcMaterialStack> load,
        out string sentence)
    {
        NpcPlanRun? run = NpcPlanRun.Begin(_journal, NpcPlanState.Opening(_identity, _jobId, world, 0), out NpcPlanSave opening);
        if (run == null)
        {
            sentence = "Thorstein's " + Job + " does not start: its plan could not be written down (" + opening.Failure + ").";
            return false;
        }

        _run = run;
        _standing = ThorsteinPlanStanding.Running;
        if (!Write(run.Record(run.State.WithRoute(sourceKey, destinationKey, string.Empty)), "name the work") ||
            !WalkToExecuting(load, "what he already carried was measured at the start, not fetched"))
        {
            sentence = _sentence;
            return false;
        }

        sentence = string.Empty;
        return true;
    }

    /// <summary>Walks the pipeline forward to Executing from wherever the plan
    /// is. Provisioned, the one phase the library treats as moving material on
    /// the way, is reached through a measured no-op.</summary>
    private bool WalkToExecuting(IReadOnlyList<NpcMaterialStack> load, string restated)
    {
        NpcPlanRun run = _run!;
        for (int guard = 0; guard < 12 && run.State.Phase != NpcPlanPhase.Executing; guard++)
        {
            bool written;
            switch (run.State.Phase)
            {
                case NpcPlanPhase.Observing:
                    written = Write(run.Advance(NpcPlanPhase.Planned, "the work and its area are named"), "record the plan");
                    break;
                case NpcPlanPhase.Planned:
                    written = Write(run.Advance(NpcPlanPhase.Manifested, "the work is done in loads he can carry"), "record the loads");
                    break;
                case NpcPlanPhase.Manifested:
                    written = Write(
                        run.Advance(NpcPlanPhase.Reserved, "what he sets aside is the settlement record's, not this plan's"),
                        "record the empty reservation");
                    break;
                case NpcPlanPhase.Reserved:
                    written = Write(run.Intend("about to restate what he already carries; nothing moves"), "record the restatement")
                        && Write(run.Conclude(true, load, run.State.TargetsDone, restated), "record the measurement")
                        && Write(run.Advance(NpcPlanPhase.Provisioned, "what he carries is on him, measured"), "record the measured load");
                    break;
                case NpcPlanPhase.Provisioned:
                    written = Write(run.Advance(NpcPlanPhase.Routed, "the walk is ordered stop by stop"), "record the route");
                    break;
                case NpcPlanPhase.Routed:
                    written = Write(run.Advance(NpcPlanPhase.Executing, "he is working"), "record the start of work");
                    break;
                default:
                    Attention("Thorstein's " + Job + " plan is in a phase work cannot start from (" + run.State.Phase + ").");
                    return false;
            }

            if (!written)
            {
                return false;
            }
        }

        if (run.State.Phase != NpcPlanPhase.Executing)
        {
            Attention("Thorstein's " + Job + " plan could not reach the point where he works.");
            return false;
        }

        return true;
    }

    /// <summary>Records a fresh measurement when it differs from the plan's
    /// evidence: an intent that moves nothing, concluded with the measurement.
    /// The settlement record, not this plan, decides whether a difference is a
    /// loss (D8); this only keeps the evidence honest.</summary>
    private bool Restate(IReadOnlyList<NpcMaterialStack> load, string note)
    {
        NpcPlanRun run = _run!;
        if (SameLoad(run.State.Carried, load))
        {
            return true;
        }

        return Write(run.Intend("about to restate what he carries; nothing moves"), "record the restatement")
            && Write(run.Conclude(true, load, run.State.TargetsDone, note), "record the measurement");
    }

    // ------------------------------------------------------------------
    // The movements
    // ------------------------------------------------------------------

    /// <summary>Announces a movement of material before the executor touches the
    /// world. <b>False means do not move it</b>: a movement nothing wrote down is
    /// one nothing could say later had happened.</summary>
    internal bool Intend(string note, out string failure)
    {
        failure = string.Empty;
        NpcPlanRun? run = _run;
        if (_standing != ThorsteinPlanStanding.Running || run == null || !run.MayAct)
        {
            failure = _standing == ThorsteinPlanStanding.Running
                ? "Thorstein's " + Job + " plan has a movement in flight, so nothing more moves"
                : "Thorstein's " + Job + " plan is not running (" + _standing + ")";
            return false;
        }

        if (!Write(run.Intend(note), "write down what is about to move"))
        {
            failure = _sentence;
            return false;
        }

        return true;
    }

    /// <summary>Records what the movement came to, measured. An outcome that
    /// could not be established, or a load that could not be read, stops the
    /// plan for a person: the next start will not repeat the movement.</summary>
    internal bool Conclude(bool established, IReadOnlyList<KeyValuePair<string, int>>? measured, string note, out string failure)
    {
        failure = string.Empty;
        NpcPlanRun? run = _run;
        if (_standing != ThorsteinPlanStanding.Running || run == null || run.State.Custody != NpcPlanCustody.Pending)
        {
            failure = "nothing was announced, so there is nothing to conclude";
            return false;
        }

        IReadOnlyList<NpcMaterialStack>? load = Measured(measured);
        bool certain = established && load != null;
        if (!Write(run.Conclude(certain, load ?? run.State.Carried, run.State.TargetsDone, note), "record what moved"))
        {
            failure = _sentence;
            return false;
        }

        if (!certain)
        {
            Stop("whether the last movement of Thorstein's " + Job + " happened could not be established, so nothing " +
                "is repeated and nothing is assumed. Look at what he carries, then resolve it.");
            failure = _sentence;
            return false;
        }

        return true;
    }

    /// <summary>The work paused or stopped for something that is not a fault:
    /// the plan is left exactly as it stands, waiting, and the next start
    /// continues it.</summary>
    internal void Suspend(string note)
    {
        NpcPlanRun? run = _run;
        if (_standing != ThorsteinPlanStanding.Running || run == null)
        {
            return;
        }

        if (run.State.Custody == NpcPlanCustody.Pending)
        {
            Stop("Thorstein's " + Job + " was interrupted with a movement in flight (" + note + "); nothing is repeated. Resolve it.");
            return;
        }

        // A failed note is not a lost plan: the record on disk is the same safe
        // resume point it was a moment ago.
        run.Suspend(note);
        _standing = ThorsteinPlanStanding.Suspended;
    }

    /// <summary>The work ended, however it ended. Finished or stopped, holding
    /// nothing, the plan closes (settled or refunded). Still holding something,
    /// it stays open and waiting, so the next start continues it from what he
    /// holds. A movement still in flight stops it for a person.</summary>
    internal void Ended(bool finished, IReadOnlyList<KeyValuePair<string, int>>? measured, string reason)
    {
        NpcPlanRun? run = _run;
        if (run == null || (_standing != ThorsteinPlanStanding.Running && _standing != ThorsteinPlanStanding.Suspended))
        {
            return;
        }

        string why = string.IsNullOrEmpty(reason) ? "the work ended" : reason;
        if (run.State.Custody == NpcPlanCustody.Pending)
        {
            Stop("Thorstein's " + Job + " ended with a movement in flight (" + why + "); nothing is repeated. Resolve it.");
            return;
        }

        IReadOnlyList<NpcMaterialStack>? load = Measured(measured);
        if (load == null || load.Count != 0)
        {
            // Carrying, or not measurable: the plan waits, and the next start
            // measures again and continues it. Already waiting: nothing to
            // write, so a caller that reports the same ending every round does
            // not write every round.
            if (_standing == ThorsteinPlanStanding.Suspended)
            {
                return;
            }

            Suspend("the work ended (" + why + ") while he still carries something; the next start continues it");
            return;
        }

        if (!Restate(load, "measured empty at the end (" + why + ")"))
        {
            return;
        }

        Close(finished, why);
    }

    private void Close(bool finished, string why)
    {
        NpcPlanRun run = _run!;
        if (Write(run.Stop(finished ? NpcPlanPhase.Settled : NpcPlanPhase.Refunded, why), "close the plan"))
        {
            _run = null;
            _standing = ThorsteinPlanStanding.Idle;
        }
    }

    /// <summary>Stops the plan for a person, with the reason.</summary>
    internal void Stop(string reason)
    {
        NpcPlanRun? run = _run;
        if (run != null && !NpcPlanProgressionIsTerminal(run.State.Phase))
        {
            run.Stop(NpcPlanPhase.NeedsAttention, reason);
        }

        Attention(reason);
    }

    // ------------------------------------------------------------------
    // The human exit
    // ------------------------------------------------------------------

    /// <summary>A person has looked. Records what he holds now, measured, and
    /// nothing else: an unreadable file is moved aside first; empty-handed, a
    /// tombstone closes it; carrying something, a new plan holds the
    /// measurement and waits for the next start. <b>Never recreates
    /// material.</b></summary>
    internal bool Resolve(NpcWorldEpoch world, IReadOnlyList<KeyValuePair<string, int>>? measured, out string sentence)
    {
        if (_journal == null)
        {
            sentence = "Nothing can be resolved: " + _closedReason + ".";
            return false;
        }

        if (_standing != ThorsteinPlanStanding.NeedsAttention)
        {
            sentence = "Thorstein's " + Job + " plan needs no resolving (" + _standing + ").";
            return false;
        }

        IReadOnlyList<NpcMaterialStack>? load = Measured(measured);
        if (world.IsUnknown || load == null)
        {
            sentence = "What Thorstein holds could not be measured, so nothing is resolved; a resolution recorded " +
                "against an unknown amount would be a guess.";
            return false;
        }

        string moved = string.Empty;
        if (_unreadable)
        {
            if (_journal.TryQuarantine() == null)
            {
                sentence = "The unreadable plan could not be moved aside, so nothing was written over it.";
                return false;
            }

            _unreadable = false;
            moved = " The unreadable plan was kept beside it as a .corrupt copy.";
        }

        NpcPlanRun? run = NpcPlanRun.Begin(_journal, NpcPlanState.Opening(_identity, _jobId, world, 0), out NpcPlanSave begun);
        if (run == null)
        {
            sentence = "The resolution could not be written (" + begun.Failure + ").";
            return false;
        }

        _run = run;
        _standing = ThorsteinPlanStanding.Running;
        if (load.Count == 0)
        {
            if (!Write(run.Stop(NpcPlanPhase.Refunded, "a person resolved the stopped plan; he held nothing"), "close the resolved plan"))
            {
                sentence = _sentence;
                return false;
            }

            _run = null;
            _standing = ThorsteinPlanStanding.Idle;
            sentence = "Resolved: he holds nothing, so the old " + Job + " plan is closed." + moved;
            return true;
        }

        if (!WalkToExecuting(load, "a person resolved the stopped plan; this is what he holds, measured"))
        {
            sentence = _sentence;
            return false;
        }

        Suspend("a person resolved the stopped plan; the next start continues from what he holds");
        sentence = "Resolved: what he holds (" + Describe(load) + ") is recorded as measured; nothing was recreated." + moved;
        return true;
    }

    internal string Describe()
    {
        NpcPlanState? state = _run?.State;
        var text = new StringBuilder("Thorstein's ").Append(Job).Append(" plan: ").Append(_standing);
        if (state != null)
        {
            text.Append(" (").Append(state.Phase).Append(", custody ").Append(state.Custody)
                .Append(", carrying ").Append(Describe(state.Carried)).Append(')');
        }

        if (_standing == ThorsteinPlanStanding.NeedsAttention || _standing == ThorsteinPlanStanding.Closed)
        {
            text.Append(": ").Append(BlockedReason);
        }

        return text.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string Job => _jobId == CollectJobId ? "collection" : "building";

    private ThorsteinPlanStanding Attention(string reason)
    {
        _sentence = reason;
        _standing = ThorsteinPlanStanding.NeedsAttention;
        return _standing;
    }

    private bool Write(NpcPlanSave save, string what)
    {
        if (save.IsSaved)
        {
            return true;
        }

        Attention("Thorstein's " + Job + " plan could not " + what + " (" + save.Failure + "), so nothing more moves.");
        return false;
    }

    private static bool NpcPlanProgressionIsTerminal(NpcPlanPhase phase) =>
        phase == NpcPlanPhase.Settled || phase == NpcPlanPhase.Refunded || phase == NpcPlanPhase.NeedsAttention;

    /// <summary>A measurement as material stacks; null when it could not be
    /// read. Zero and negative counts are dropped, names are ordered, so two
    /// equal loads compare equal.</summary>
    internal static IReadOnlyList<NpcMaterialStack>? Measured(IReadOnlyList<KeyValuePair<string, int>>? carried)
    {
        if (carried == null)
        {
            return null;
        }

        var stacks = new List<NpcMaterialStack>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> entry in carried)
        {
            if (string.IsNullOrEmpty(entry.Key) || !seen.Add(entry.Key))
            {
                return null;
            }

            if (entry.Value > 0)
            {
                stacks.Add(new NpcMaterialStack(new NpcMaterial(entry.Key, 1, 0), entry.Value));
            }
        }

        stacks.Sort((left, right) => string.CompareOrdinal(left.Material.ItemName, right.Material.ItemName));
        return stacks.Count == 0 ? Nothing : stacks;
    }

    private static bool SameLoad(IReadOnlyList<NpcMaterialStack> left, IReadOnlyList<NpcMaterialStack> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (NpcMaterialStack stack in left)
        {
            counts[stack.Material.ItemName] = stack.Count;
        }

        foreach (NpcMaterialStack stack in right)
        {
            if (!counts.TryGetValue(stack.Material.ItemName, out int count) || count != stack.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(IReadOnlyList<NpcMaterialStack> load)
    {
        if (load.Count == 0)
        {
            return "nothing";
        }

        var parts = new List<string>(load.Count);
        foreach (NpcMaterialStack stack in load)
        {
            parts.Add(stack.Count.ToString(CultureInfo.InvariantCulture) + " " + stack.Material.ItemName);
        }

        return string.Join(", ", parts);
    }
}
