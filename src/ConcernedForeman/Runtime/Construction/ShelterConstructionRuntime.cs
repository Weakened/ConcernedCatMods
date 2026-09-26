using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Custody;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>What actually drives Thorstein through a confirmed build order: the
/// composition root for the execution loop, and the thing the plugin ticks.
///
/// <b>This type exists because #380 had everything except this.</b> The
/// blueprint, the pricing, the placement gate, the placer, the progress read, the
/// sentences and the panel were all written and all tested, and nothing joined
/// them: <c>WorldPiecePlacer</c> had no production caller at all. So a player
/// could mark a site, price it, confirm it and read a progress line while
/// nothing ever happened. Every seam below already existed; what is new is that
/// they are wired to each other and to the game loop.
///
/// <b>Which seam does what.</b>
/// <list type="bullet">
/// <item><see cref="BuildOrderRuntime"/> holds the player's confirmation and
/// prices the order against the live recipes. It is the <i>only</i> answer to the
/// gate's authority question.</item>
/// <item><see cref="ShelterBuildLoop"/> decides, with no game, what to do this
/// round: read the site, fetch a phase's material, walk, work, place, pay.</item>
/// <item><see cref="WorkerBuildWalk"/> over the C1 <c>IWorkerMotion</c> seam
/// moves him, and only while this runtime holds his actor mode.</item>
/// <item><see cref="WorldBuildMaterials"/> over <c>ICustodyRuntime</c> opens the
/// permitted supply chest and his own persisted inventory, and nothing
/// else.</item>
/// <item><see cref="GatedPiecePlacer"/> over <see cref="WorldPiecePlacer"/> is
/// the single authority-gated placement call in the product.</item>
/// <item><see cref="BuildPose"/> makes the work visible on his own body.</item>
/// </list>
///
/// <b>Host-only, and it holds his mode while it works.</b> The authority answer
/// is injected rather than recomputed, so there is one such policy in this
/// product; the actor-mode hold comes from Concerned NPC's one arbiter, so a
/// collection order cannot drive the same body at the same time and the refusal
/// is visible to every product at once.
///
/// <b>It ticks from the plugin's <c>Update</c>, not from the worker's work
/// tick.</b> The collection runtime owns <c>ForemanWorkerAI.WorkTick</c> and runs
/// its mutations there because a pick spawns loose ground drops that must not
/// interleave with the player's own auto-pickup. A build order spawns no ground
/// drops: it moves items between two named inventories and calls
/// <c>Player.PlacePiece</c> on the host player, which is the frame vanilla itself
/// places pieces in. Only one job may hold Thorstein, so while this runs the
/// collection loop is idle by construction.</summary>
internal sealed class ShelterConstructionRuntime
{
    /// <summary>How often a round runs. A round re-reads seventeen placements
    /// out of the world, which is cheap but not free, and nothing about building
    /// needs a decision every frame.</summary>
    internal const float RoundSeconds = 0.2f;

    /// <summary>How often a FINISHED shelter is looked at again.
    ///
    /// A finished shelter stays finished, and the only reason to look is that a
    /// player can knock a wall out. Running the ordinary round for that cost five
    /// arbiter Enter/Release pairs a second and five identical "the shelter is
    /// finished" lines a second, for as long as the world stayed open - with
    /// <c>MayRetireBody</c> and <c>MayRelocateHome</c> flickering false the whole
    /// time, so a collection order asking for him inside one of those windows got
    /// RefusedBusy. The look now goes through
    /// <see cref="ShelterBuildLoop.LooksFinished"/>, which needs no body and takes
    /// no hold.</summary>
    internal const float FinishedRecheckSeconds = 10f;

    private readonly BuildOrderRuntime _orders;
    private readonly IActorModeHold _modes;
    private readonly ICustodyRuntime _custody;
    private readonly IWorkerMotion _motion;
    private readonly Func<bool> _mayWork;
    private readonly Func<float> _now;
    private readonly Action<string> _log;

    private readonly GatedPiecePlacer _placer;
    private readonly WorldBuildMaterials _materials;
    private readonly ShelterBuildLoop _loop;

    private string? _job;
    // Outstanding construction survives release of the loaded-body hold.
    private string? _cleanupJob;
    private Guid _epoch;
    private bool _finished;
    private bool _saidAbsent;
    private float _finishedAt = float.NegativeInfinity;
    private float _lastRound = float.NegativeInfinity;
    private bool _faulted;
    private string _fault = string.Empty;

    /// <param name="orders">The player's build order: the confirmation, and the
    /// plan priced from the game's own recipes.</param>
    /// <param name="modes">Thorstein's actor mode, over Concerned NPC's arbiter.
    /// Required to be his: a runtime handed somebody else's hold would drive his
    /// body on another identity's authority.</param>
    /// <param name="custody">The only thing that opens a container or the
    /// worker's own inventory.</param>
    /// <param name="motion">The only thing that moves him.</param>
    /// <param name="mayWork">The work-authority answer (D3): opted in, host, not
    /// dedicated, nobody else connected.</param>
    /// <param name="supply">The permitted supply container, read live from the
    /// settlement register.</param>
    /// <param name="pose">The visible work. Injected so a test can watch it and
    /// so a build with no animator still builds.</param>
    /// <param name="installer">What brings a piece into the world. Defaults to
    /// the host player's own <c>PlacePiece</c>.</param>
    internal ShelterConstructionRuntime(
        BuildOrderRuntime orders,
        IActorModeHold modes,
        ICustodyRuntime custody,
        IWorkerMotion motion,
        Func<bool> mayWork,
        Func<SupplyChest> supply,
        Func<float> now,
        Action<string> log,
        IBuildPose? pose = null,
        IPieceInstaller? installer = null)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        if (motion == null)
        {
            throw new ArgumentNullException(nameof(motion));
        }

        if (!modes.Worker.Equals(motion.Worker))
        {
            throw new ArgumentException(
                "The build loop's mode hold is for " + modes.Worker + " and its motion moves " +
                motion.Worker + ".", nameof(motion));
        }

        _custody = custody ?? throw new ArgumentNullException(nameof(custody));
        _motion = motion;
        _mayWork = mayWork ?? throw new ArgumentNullException(nameof(mayWork));
        _now = now ?? throw new ArgumentNullException(nameof(now));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        WorkerKey worker = modes.Worker;
        _materials = new WorldBuildMaterials(
            _custody,
            worker,
            supply ?? throw new ArgumentNullException(nameof(supply)),
            _log);

        // The single authority-gated placement call in the product: the gate,
        // then the installer, and nothing in between.
        _placer = new GatedPiecePlacer(new WorldPiecePlacer(
            new WorldPlacementProbe(_mayWork),
            installer ?? new HostPlayerPieceInstaller(),
            _log));

        _loop = new ShelterBuildLoop(
            () => _orders.Plan(),
            () => _orders.IsAuthorised,
            _orders.Sight,
            new WorkerBuildWalk(motion, () => _job),
            _materials,
            _placer,
            pose ?? new BuildPose(() => WorkerBody.FindLive(worker.Value), _log),
            _log);

        // The status line, wired HERE rather than by the plugin. Every surface a
        // player reads the order's state on goes through BuildOrderRuntime, so
        // "authorised" and "being built" must never be the same sentence - and a
        // hookup that lives in Plugin.cs is a hookup that can be deleted without
        // a single test noticing, which is the defect this whole change exists to
        // fix. Setting it in the composition root means the surface cannot exist
        // without the line.
        _orders.WorkLine = Describe;
    }

    /// <summary>The loop, for the panel and the status line.</summary>
    internal ShelterBuildLoop Loop => _loop;

    /// <summary>How many pieces this world load's placer has actually put in the
    /// world.</summary>
    internal int Placed => _placer.Placed;

    /// <summary>Whether a round has thrown. A latched runtime does no more work
    /// this session and the order keeps its last reported state.</summary>
    internal bool IsFaulted => _faulted;

    /// <summary>One round, rate limited. Called from the plugin's
    /// <c>Update</c>.</summary>
    internal void Tick()
    {
        if (_faulted)
        {
            return;
        }

        try
        {
            float now = _now();
            if (_custody.WorldLoadEpoch != _epoch)
            {
                // A new world load: the marker, the chest key and the body all
                // belong to the previous one.
                _epoch = _custody.WorldLoadEpoch;
                Release();
                _loop.Forget();
                _cleanupJob = null;
                _lastRound = float.NegativeInfinity;
                _finished = false;
                _finishedAt = float.NegativeInfinity;
            }

            if (!_orders.IsAuthorised)
            {
                if (_cleanupJob != null)
                {
                    // Absence releases the body, not the obligation to account
                    // for its building materials. Never refund another job's load.
                    if (!Present() || !Refusal(out _) ||
                        !ActorModeGrants.IsGranted(_modes.Enter(ActorMode.Recovering, _cleanupJob)))
                    {
                        Release();
                        return;
                    }
                    _job = _cleanupJob;
                    _loop.Cancel(now, "the build order was withdrawn.");
                    _cleanupJob = null;
                    Release();
                }

                _finished = false;
                return;
            }

            if (_finished)
            {
                // Standing, and nobody's worker. Looked at every ten seconds
                // rather than five times a second, and looked at WITHOUT taking
                // his body: a completed order that keeps the arbiter busy is a
                // completed order that stops him resting, being moved home, or
                // taking a collection order.
                if (now - _finishedAt < FinishedRecheckSeconds && now >= _finishedAt)
                {
                    return;
                }

                _finishedAt = now;
                if (_loop.LooksFinished())
                {
                    return;
                }

                // Something came down. Back to work.
                _finished = false;
            }

            if (now - _lastRound < RoundSeconds && now >= _lastRound)
            {
                return;
            }

            _lastRound = now;

            if (!Refusal(out string why))
            {
                if (_job != null)
                {
                    _loop.Cancel(now, why);
                    Release();
                }

                return;
            }

            // No body, no hold. A job that cannot reach its worker has nothing to
            // hold him FOR - every motion command is refused without the hold
            // anyway - so keeping it only stops a person retiring him or moving him
            // home while he is nowhere near the site. Checked BEFORE the hold is
            // taken rather than after the round, because releasing afterwards would
            // retake him on the very next round and put back the Enter/Release
            // churn this runtime just stopped doing.
            if (!Present())
            {
                if (_job != null)
                {
                    Release();
                }

                if (!_saidAbsent)
                {
                    _saidAbsent = true;
                    _log("Build order: Thorstein is not in loaded ground, so nothing is being built and he " +
                        "is nobody's worker until he is. The order stands.");
                }

                return;
            }

            _saidAbsent = false;
            if (_job == null && !TryHold())
            {
                return;
            }

            _loop.Tick(now);

            switch (_loop.Step)
            {
                case BuildStep.Finished:
                    _cleanupJob = null;
                    // Done. He is nobody's worker again, which is what lets his
                    // body rest, be moved home, or take another order - and the
                    // latch is what keeps it that way instead of retaking him
                    // every round to be told the same thing.
                    Release();
                    _finished = true;
                    _finishedAt = now;
                    break;
                case BuildStep.Waiting:
                case BuildStep.Stopped:
                    // Still held, and visibly not progressing: the order is the
                    // player's and a stopped one is theirs to cancel.
                    Hold(ActorMode.Paused);
                    break;
                default:
                    Hold(ActorMode.Working);
                    break;
            }
        }
        catch (Exception exception)
        {
            Latch(exception);
        }
    }

    /// <summary>A world has gone away. Gives the body back and forgets the load.
    /// Nothing is written: the chest and the body belong to a world that is not
    /// there.</summary>
    internal void OnWorldUnloaded()
    {
        try
        {
            Release();
            _loop.Forget();
            _cleanupJob = null;
            _epoch = Guid.Empty;
            _lastRound = float.NegativeInfinity;
            _finished = false;
            _finishedAt = float.NegativeInfinity;
        }
        catch (Exception exception)
        {
            _log("Build order: could not tidy up after the world closed: " + exception.GetType().Name + ".");
        }
    }

    /// <summary>The line a player reads about the work itself, under the order's
    /// own status. Never a claim that something happened in game.</summary>
    internal string Describe()
    {
        if (_faulted)
        {
            return "The build loop FAULTED (" + _fault + ") and does no more work this session; see the log.";
        }

        if (!_orders.IsAuthorised)
        {
            // AND WHAT THE LAST ROUND SAID, which is the point of this branch
            // rather than an afterthought. This method IS
            // BuildOrderRuntime.WorkLine, and WorkLine is what the panel's status
            // line and cf_build status both render - so returning early here meant
            // that cancelling an order showed "no build order is authorised" while
            // the only sentence naming the four wood still in Thorstein's hands
            // went to the BepInEx log, where nobody reads it. A withdrawal is
            // exactly when a player needs to be told where their material went.
            if (_cleanupJob != null)
                return "No build order is authorised. Material return is pending until Thorstein is loaded " +
                    "and free of other work; carried materials remain in his inventory. " +
                    (Refusal(out string pendingReason) ? string.Empty : pendingReason);

            string last = _loop.Step == BuildStep.Stopped || _loop.Step == BuildStep.Finished
                ? _loop.Reason
                : string.Empty;
            return last.Length == 0
                ? "Nothing is being built: no build order is authorised."
                : "No build order is authorised. " + last;
        }

        if (!Refusal(out string why))
        {
            return "Nothing is being built: " + why;
        }

        if (!Present())
        {
            return "Nothing is being built: Thorstein is not in loaded ground. He is nobody's worker " +
                "until he is, so he can be moved home or retired meanwhile, and the order stands.";
        }

        string carried = _materials.Carried.IsEmpty
            ? string.Empty
            : " He is carrying " + _materials.Carried.Describe() + ".";
        string chest = _loop.Draws == 1
            ? " The supply chest has been opened once for this order."
            : " The supply chest has been opened " + _loop.Draws + " times for this order.";
        string progress = _loop.Progress == null
            ? string.Empty
            : " " + ConstructionSentences.Working(_loop.Progress);
        return "Thorstein: " + Word(_loop.Step) + "." + progress + carried + chest +
            (_loop.Reason.Length == 0 ? string.Empty : " " + _loop.Reason);
    }

    /// <summary>Whether the loop may run at all right now, and why not.
    ///
    /// <b>The carried-material check is the one worth reading.</b> This product
    /// counts what Thorstein physically carries to decide what a build order still
    /// needs, so gathered material in the same inventory would make that number
    /// wrong in the direction that spends somebody else's wood. The question is
    /// asked of the RECORD, per place, across every order it knows - <b>not</b> of
    /// the live-order list, which was the first version of this check and which
    /// missed the ordinary case: a cancelled collection order is terminal and its
    /// carried material is documented as staying exactly where it is. The
    /// actor-mode arbiter separately stops a new collection order starting while
    /// this one holds him.</summary>
    private bool Refusal(out string why)
    {
        if (!Safe(_mayWork))
        {
            why = "this is not a world this runtime may build in: it builds only as the host, alone, " +
                "with the settlement runtime turned on.";
            return false;
        }

        if (!_custody.IsWritable)
        {
            why = "the settlement's record cannot be written now, so nothing is taken out of a chest.";
            return false;
        }

        // The complete question, and the one that matters: does the RECORD say
        // there is gathered material in his inventory that belongs to something
        // else? Asked of every order the record knows, terminal ones included -
        // which is the half the first version of this check got wrong. It only
        // refused while a collection order was non-terminal, and CANCELLED is
        // terminal; cancelling a collection order is documented as keeping the
        // carried material exactly where it physically is and continuing to record
        // it. So the ordinary outcome of a player cancelling was a build order
        // that counted his gathered Wood as its own, burned it into a wall or put
        // it into the SUPPLY chest instead of the collection order's delivery
        // container, and never told custody - a discrepancy the player could not
        // account for on the next reconcile.
        var theirs = new MaterialTally();
        foreach (string kind in _orders.MaterialKinds)
        {
            theirs.Add(kind, Held(kind));
        }

        if (!theirs.IsEmpty)
        {
            why = "the settlement's record says Thorstein is already holding " + theirs.Describe() +
                " for other work. Nothing is taken out of a chest for the shelter and nothing of his is " +
                "spent on it until that is settled - cf_settle status says which order, and " +
                "cf_settle reconcile settles it.";
            return false;
        }

        if (_custody.TryRecoverOrder(
                new WorkerId(_modes.Worker.Worker), out CollectionOrderDefinition? order, out CollectionOrderState state) &&
            order != null && !CollectionOrderStates.IsTerminal(state))
        {
            why = "Thorstein still has collection order " + order.Order.Value + " on the record (" + state +
                "). Finish, cancel or rebind it first; nothing is taken out of a chest for the shelter " +
                "meanwhile.";
            return false;
        }

        if (_materials.Uncertain != null)
        {
            why = _materials.Uncertain;
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>What the record says is at his place for anybody, for one item.
    /// Zero when it cannot say, which is the only reading that does not invent a
    /// claim about somebody else's material.</summary>
    private int Held(string kind)
    {
        try
        {
            return Math.Max(0, _custody.RecordedAt(_custody.WorkerLocation, new MaterialItem(kind, 1, 0)));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Whether there is a body to work with at all. A presence answer
    /// that could not be established is not a presence.</summary>
    private bool Present()
    {
        try
        {
            return _motion.IsPresent;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool TryHold()
    {
        ShelterPlan plan = _orders.Plan();
        if (!plan.IsPlanned)
        {
            return false;
        }

        string job = JobFor(plan.Marker);
        ActorModeOutcome outcome = _modes.Enter(ActorMode.Working, job);
        if (!ActorModeGrants.IsGranted(outcome))
        {
            // Every outcome that is not a grant is a refusal, Unspecified
            // included. A caller that only looked for RefusedBusy would drive a
            // body it has no hold on.
            _log("Build order: Thorstein could not be taken for " + job + " (" + outcome +
                "), so nothing is built and nothing is taken out of a chest.");
            return false;
        }

        _job = job;
        _cleanupJob = job;
        return true;
    }

    private void Hold(ActorMode mode)
    {
        string? job = _job;
        if (job == null || _modes.Mode == mode)
        {
            return;
        }

        if (!ActorModeGrants.IsGranted(_modes.Enter(mode, job)))
        {
            // The hold was taken away underneath us. Stop pretending to have it.
            _job = null;
        }
    }

    private void Release()
    {
        string? job = _job;
        _job = null;
        if (job != null)
        {
            _modes.Release(job);
        }
    }

    private void Latch(Exception exception)
    {
        if (_faulted)
        {
            return;
        }

        _faulted = true;
        _fault = SafeFailure.Brief(exception);
        _log("Build order FAULTED and does no more work this session. " + exception);
        try
        {
            Release();
        }
        catch (Exception)
        {
            // Already faulting; a second failure must not escape either.
        }
    }

    /// <summary>One job id per order, derived from the order itself so that the
    /// same order re-entering the mode is the same job and a different marker is
    /// not.</summary>
    private static string JobFor(BuildOrderMarker marker) => "build-shelter@" + marker.At;

    private static string Word(BuildStep step)
    {
        switch (step)
        {
            case BuildStep.Idle:
                return "idle";
            case BuildStep.GoingToSite:
                return "walking to the build site";
            case BuildStep.GoingToSupply:
                return "walking to the supply chest";
            case BuildStep.Provisioning:
                return "taking material out of the supply chest";
            case BuildStep.GoingToPiece:
                return "walking to the next piece";
            case BuildStep.Working:
                return "working";
            case BuildStep.Waiting:
                return "waiting";
            case BuildStep.Finished:
                return "finished";
            case BuildStep.Stopped:
                return "stopped";
            default:
                return "not saying";
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
            return false;
        }
    }
}

/// <summary>Where the permitted supply container comes from: the settlement
/// register's own <c>SupplyContainer</c> designation, and nothing else.
///
/// <b>It is its own type so the refusal is a sentence rather than a null.</b>
/// "No chest is marked", "the chest was marked in a previous world load" and
/// "the record could not be read" are three different things a player would do
/// three different things about, and a <c>Func</c> returning a nullable key can
/// only say the first.</summary>
internal static class SupplyChests
{
    /// <summary>The marked container, or an unnamed one when there is none this
    /// runtime may use.</summary>
    /// <param name="designations">The register's designations.</param>
    /// <param name="staleIdentity">Whether the register says the marked chest's
    /// identity belongs to a previous world load. A stale one is <b>not</b>
    /// used: the key would resolve to whatever object happens to hold it now.
    /// </param>
    internal static SupplyChest From(
        IReadOnlyList<TheConcernedCat.Settlement.Designations.Designation>? designations, bool staleIdentity)
    {
        if (designations == null || staleIdentity)
        {
            return default;
        }

        foreach (TheConcernedCat.Settlement.Designations.Designation designation in designations)
        {
            if (designation.Kind != TheConcernedCat.Settlement.Designations.DesignationKind.SupplyContainer)
            {
                continue;
            }

            string? key = designation.ContainerKey;
            return string.IsNullOrEmpty(key) ? default : new SupplyChest(key!, designation.Centre);
        }

        return default;
    }
}
