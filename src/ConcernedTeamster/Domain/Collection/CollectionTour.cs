using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What the shared job driver answered, in this product's words.
///
/// <b>Why a copy of four words rather than the library's own enum.</b> The tour
/// below is game-free and is driven by tests that do not compile the shared
/// runtime's job sources; the adapter maps the driver's answer onto this one,
/// one line per value, and that mapping is the only place the two meet.</summary>
internal enum TourProgress
{
    Unspecified = 0,

    /// <summary>Carry out the step.</summary>
    Do = 1,

    /// <summary>Every target the job was for was serviced.</summary>
    Finished = 2,

    /// <summary>Incomplete, not impossible: ask again next tick.</summary>
    Waiting = 3,

    /// <summary>The job is over and did not finish; the reason says why.</summary>
    Stopped = 4,
}

/// <summary>One planned stop: which source, which load it belongs to, where.
/// </summary>
internal readonly struct TourStep
{
    public TourStep(string key, int round, int load, CollectionPoint at)
    {
        Key = key ?? string.Empty;
        Round = round;
        Load = load;
        At = at;
    }

    /// <summary>The source's key, as the survey minted it.</summary>
    public string Key { get; }

    /// <summary>Which of the driver's rounds planned this stop. Each round
    /// numbers its loads from zero again, so a load is only the same load when
    /// both numbers match.</summary>
    public int Round { get; }

    /// <summary>Which load (trip) of its round's plan this stop belongs to, from
    /// zero. A change of load is a trip to the chest.</summary>
    public int Load { get; }

    /// <summary>Whether two stops belong to the same load.</summary>
    public bool SameLoadAs(TourStep other) => Round == other.Round && Load == other.Load;

    public CollectionPoint At { get; }
}

/// <summary>The driver's answer, mapped.</summary>
internal readonly struct TourAdvance
{
    public TourAdvance(TourProgress progress, TourStep step, bool hasStep, string reason)
    {
        Progress = progress;
        Step = step;
        HasStep = hasStep && progress == TourProgress.Do;
        Reason = reason ?? string.Empty;
    }

    public TourProgress Progress { get; }

    public TourStep Step { get; }

    public bool HasStep { get; }

    public string Reason { get; }
}

/// <summary>What the tour needs from the shared job driver, and nothing else.
/// The adapter implements it over <c>NpcJobDriver</c>; tests implement it over
/// a script.</summary>
internal interface ITourPump
{
    TourAdvance Next(CollectionPoint standingAt);

    void Done();

    void Skipped();

    void Failed();

    void Abandon();
}

/// <summary>What the tour asks the runtime to do this tick.</summary>
internal enum TourOrder
{
    /// <summary>Nothing: the tour is over, or waiting on the driver.</summary>
    None = 0,

    /// <summary>Walk towards <see cref="TourInstruction.Target"/>.</summary>
    Walk = 1,

    /// <summary>Start picking <see cref="TourInstruction.Key"/>; report the end
    /// through <see cref="CollectionTour.PickEnded"/>.</summary>
    Pick = 2,

    /// <summary>Move what he collected into the chosen chest; report the end
    /// through <see cref="CollectionTour.DepositEnded"/>.</summary>
    Deposit = 3,

    /// <summary>Stand still: a pick or a deposit is in flight, or he arrived.
    /// </summary>
    Hold = 4,
}

internal readonly struct TourInstruction
{
    public TourInstruction(TourOrder order, CollectionPoint target, string key)
    {
        Order = order;
        Target = target;
        Key = key ?? string.Empty;
    }

    public TourOrder Order { get; }

    public CollectionPoint Target { get; }

    public string Key { get; }

    public static TourInstruction Nothing => new TourInstruction(TourOrder.None, default, string.Empty);

    public static TourInstruction Hold => new TourInstruction(TourOrder.Hold, default, string.Empty);
}

/// <summary>Where a tour stands.</summary>
internal enum TourPhase
{
    Unspecified = 0,

    /// <summary>Asking the driver what is next.</summary>
    Asking = 1,

    /// <summary>Walking to a source.</summary>
    ToSource = 2,

    /// <summary>A pick is in flight.</summary>
    Picking = 3,

    /// <summary>Walking to the chosen chest with a load.</summary>
    ToChest = 4,

    /// <summary>A deposit is in flight.</summary>
    Depositing = 5,

    /// <summary>Over: every source the job was for was serviced and everything
    /// he collected went into the chest.</summary>
    Finished = 6,

    /// <summary>Over, and not finished. <see cref="CollectionTour.Reason"/> says
    /// why; what he still holds is in him and is never assumed to be anywhere
    /// else.</summary>
    Stopped = 7,
}

/// <summary>How a pick ended, from the port's measurement.</summary>
internal enum TourPickEnd
{
    Unspecified = 0,

    /// <summary>The port measured this many reaching him.</summary>
    Took = 1,

    /// <summary>The source refused before anything happened: gone, already
    /// picked, owned elsewhere, awaiting confirmation. Nothing is owed.</summary>
    Refused = 2,

    /// <summary>The pick was started and nothing was measured reaching him. The
    /// source may have been picked; nothing is assumed. Still owed, so the
    /// driver's own revalidation decides whether it is worth another look.
    /// </summary>
    NothingReached = 3,
}

/// <summary>How a deposit ended, from the deposit port's measurement.</summary>
internal enum TourDepositEnd
{
    Unspecified = 0,

    /// <summary>Everything he collected went in, measured.</summary>
    Emptied = 1,

    /// <summary>Some went in and the chest would take no more; the rest stays
    /// in him. The tour stops: walking another load to a full chest is the
    /// one-stone-per-trip loop by another route.</summary>
    ChestFull = 2,

    /// <summary>Nothing moved: the chest refused (unmarked, open, warded, gone,
    /// out of reach). He keeps everything.</summary>
    Refused = 3,

    /// <summary>Whether it moved could not be established. Nothing is retried
    /// or assumed; a person decides.</summary>
    Uncertain = 4,
}

/// <summary>The bounds a tour walks within.</summary>
internal sealed class CollectionTourLimits
{
    public CollectionTourLimits(float reachMetres, float stallSeconds, int mostSteps)
    {
        ReachMetres = reachMetres > 0f && !float.IsNaN(reachMetres) ? reachMetres : throw new ArgumentOutOfRangeException(nameof(reachMetres));
        StallSeconds = stallSeconds > 0f && !float.IsNaN(stallSeconds) ? stallSeconds : throw new ArgumentOutOfRangeException(nameof(stallSeconds));
        MostSteps = mostSteps > 0 ? mostSteps : throw new ArgumentOutOfRangeException(nameof(mostSteps));
    }

    /// <summary>How close he must stand to a source or the chest to act on it.
    /// The same reach the ordered pick and the deposit port already use.</summary>
    public float ReachMetres { get; }

    /// <summary>How long a walk may go without getting at least
    /// <see cref="ProgressMetres"/> closer before it is given up as unreachable.
    /// Bounded local recovery, never a teleport.</summary>
    public float StallSeconds { get; }

    /// <summary>How much closer counts as progress.</summary>
    public float ProgressMetres => 0.5f;

    /// <summary>The most steps one tour takes before it stops and says so: a
    /// liveness bound over and above the driver's own rounds cap.</summary>
    public int MostSteps { get; }

    public static CollectionTourLimits Default(CollectionLimits limits) =>
        new CollectionTourLimits(limits.PickupReachMetres, stallSeconds: 12f, mostSteps: 512);
}

/// <summary>Gunnar's collection tour (#381): survey, plan, walk a load, take it
/// to the one chest the player chose, measure it in, and the next load.
///
/// <b>What this owns, and what it does not.</b> Which source is next, how a job
/// splits into loads and in what order a load is walked are the shared job
/// driver's - <see cref="ITourPump"/> is all this sees of it. What this owns is
/// the part the driver knows nothing about: that a load ends at the chest. The
/// driver numbers every planned stop with the load it belongs to, so a change of
/// number is a trip to the chest and nothing else is.
///
/// <b>The property this type exists for: one trip to the chest per load.</b>
/// He goes to the chest only (a) when the next stop belongs to a different
/// load and he has collected something since the last deposit, or (b) when the
/// job is over and he has collected something. Never after a single source
/// because it was a single source, and never per item - that is the loop this
/// work exists to end, and <c>CollectionTourTests</c> counts deposits to prove
/// it.
///
/// <b>Material.</b> Nothing here counts material; the port measures picks and
/// the deposit port measures deposits, and both report through the ledger.
/// What this tracks is only whether anything was collected since the last
/// deposit, so a load that came to nothing does not cost a walk.
///
/// <b>No portals, no teleports.</b> Every movement this asks for is a walk.
/// A walk that stops getting closer for <see cref="CollectionTourLimits.StallSeconds"/>
/// is given up: a source is reported to the driver as failed (still owed, the
/// driver decides), and an unreachable chest stops the tour with what he holds
/// still in him.</summary>
internal sealed class CollectionTour
{
    private readonly ITourPump _pump;
    private readonly CollectionTourLimits _limits;
    private readonly CollectionPoint _chest;

    private TourStep _step;
    private bool _hasStep;
    private TourStep _loadOf;
    private bool _hasLoad;
    private bool _finishing;
    private string _stopReason = string.Empty;
    private float _bestDistance = float.PositiveInfinity;
    private float _bestAt = float.NaN;
    private bool _orderIssued;

    public CollectionTour(ITourPump pump, CollectionTourLimits limits, CollectionPoint chest, bool startWithDeposit)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _chest = chest;
        Reason = string.Empty;

        // He may already be carrying what an earlier, interrupted tour
        // collected. That is emptied first, so every load this tour plans
        // starts from his own empty hands rather than from somebody's guess.
        if (startWithDeposit)
        {
            CollectedSinceDeposit = true;
            EnterToChest();
        }
        else
        {
            Phase = TourPhase.Asking;
        }
    }

    public TourPhase Phase { get; private set; }

    /// <summary>Why the tour stopped, in words a player can read. Empty while it
    /// runs and when it finished.</summary>
    public string Reason { get; private set; }

    /// <summary>Whether anything reached him since the last deposit.</summary>
    public bool CollectedSinceDeposit { get; private set; }

    public int Picks { get; private set; }

    public int Deposits { get; private set; }

    public int Steps { get; private set; }

    /// <summary>How many different loads he has started.</summary>
    public int Loads { get; private set; }

    public bool IsOver => Phase == TourPhase.Finished || Phase == TourPhase.Stopped;

    /// <summary>One tick. <paramref name="standingAt"/> is where he is; <paramref name="now"/>
    /// is the runtime's clock in seconds.</summary>
    public TourInstruction Tick(CollectionPoint standingAt, float now)
    {
        switch (Phase)
        {
            case TourPhase.Asking:
                return Ask(standingAt, now);
            case TourPhase.ToSource:
                return Walk(standingAt, now, _step.At, arrived: EnterPicking, unreachable: SourceUnreachable);
            case TourPhase.ToChest:
                return Walk(standingAt, now, _chest, arrived: EnterDepositing, unreachable: ChestUnreachable);
            case TourPhase.Picking:
                return Issue(TourOrder.Pick, _step.At, _step.Key);
            case TourPhase.Depositing:
                return Issue(TourOrder.Deposit, _chest, string.Empty);
            default:
                return TourInstruction.Nothing;
        }
    }

    /// <summary>The pick the tour asked for has ended.</summary>
    public void PickEnded(TourPickEnd how, int taken)
    {
        if (Phase != TourPhase.Picking)
        {
            return;
        }

        Steps++;
        switch (how)
        {
            case TourPickEnd.Took when taken > 0:
                Picks++;
                CollectedSinceDeposit = true;
                _pump.Done();
                break;
            case TourPickEnd.Refused:
                _pump.Skipped();
                break;
            default:
                // Started, nothing measured. Owed, not done: the driver's own
                // revalidation of the source decides whether it comes back.
                _pump.Failed();
                break;
        }

        _hasStep = false;
        Phase = TourPhase.Asking;
    }

    /// <summary>The deposit the tour asked for has ended.</summary>
    public void DepositEnded(TourDepositEnd how, string detail)
    {
        if (Phase != TourPhase.Depositing)
        {
            return;
        }

        Deposits++;
        switch (how)
        {
            case TourDepositEnd.Emptied:
                CollectedSinceDeposit = false;
                if (_finishing)
                {
                    Close();
                    return;
                }

                // Back to the stop that sent him to the chest, if there was one.
                Phase = _hasStep ? TourPhase.ToSource : TourPhase.Asking;
                ResetWalk();
                return;
            case TourDepositEnd.ChestFull:
                Stop("the chest you chose is full, so he stopped with the rest still on him" +
                    Suffix(detail));
                return;
            case TourDepositEnd.Refused:
                Stop("the chest you chose refused what he brought, so he kept all of it" + Suffix(detail));
                return;
            default:
                Stop("whether what he brought went into the chest could not be established, so nothing " +
                    "more is attempted and nothing is assumed" + Suffix(detail));
                return;
        }
    }

    /// <summary>The player, the world or the authority ended the tour. What he
    /// holds stays in him.</summary>
    public void Cancel(string why)
    {
        if (IsOver)
        {
            return;
        }

        Stop(string.IsNullOrEmpty(why) ? "the tour was cancelled" : why);
    }

    private TourInstruction Ask(CollectionPoint standingAt, float now)
    {
        if (Steps >= _limits.MostSteps)
        {
            Stop("the tour took more steps than one tour is allowed and still had work left");
            return TourInstruction.Nothing;
        }

        TourAdvance advance = _pump.Next(standingAt);
        switch (advance.Progress)
        {
            case TourProgress.Do when advance.HasStep:
                _step = advance.Step;
                _hasStep = true;
                bool newLoad = !_hasLoad || !_step.SameLoadAs(_loadOf);
                if (newLoad)
                {
                    Loads++;
                }

                _loadOf = _step;
                _hasLoad = true;
                if (newLoad && CollectedSinceDeposit)
                {
                    // A new load begins: this one goes to the chest first, and
                    // the stop that announced the new load waits for him.
                    EnterToChest();
                    return Walk(standingAt, now, _chest, EnterDepositing, ChestUnreachable);
                }

                Phase = TourPhase.ToSource;
                ResetWalk();
                return Walk(standingAt, now, _step.At, EnterPicking, SourceUnreachable);
            case TourProgress.Finished:
                return End(standingAt, now, string.Empty);
            case TourProgress.Stopped:
                return End(standingAt, now, advance.Reason.Length != 0 ? advance.Reason : "the job stopped");
            case TourProgress.Waiting:
                return TourInstruction.Hold;
            default:
                Stop("the job driver gave an answer nobody recorded, so he stopped");
                return TourInstruction.Nothing;
        }
    }

    /// <summary>The job is over. A load collected and not yet in the chest goes
    /// there first - the final trip - and the tour closes on the deposit.</summary>
    private TourInstruction End(CollectionPoint standingAt, float now, string reason)
    {
        _hasStep = false;
        _stopReason = reason;
        if (CollectedSinceDeposit)
        {
            _finishing = true;
            EnterToChest();
            return Walk(standingAt, now, _chest, EnterDepositing, ChestUnreachable);
        }

        Close();
        return TourInstruction.Nothing;
    }

    private void Close()
    {
        if (_stopReason.Length == 0)
        {
            Phase = TourPhase.Finished;
            Reason = string.Empty;
        }
        else
        {
            Phase = TourPhase.Stopped;
            Reason = _stopReason;
        }
    }

    private TourInstruction Walk(
        CollectionPoint standingAt, float now, CollectionPoint to, Action arrived, Action unreachable)
    {
        float distance = standingAt.FlatDistanceTo(to);
        if (float.IsNaN(distance))
        {
            unreachable();
            return TourInstruction.Hold;
        }

        if (distance <= _limits.ReachMetres)
        {
            arrived();
            return TourInstruction.Hold;
        }

        if (float.IsNaN(_bestAt) || distance < _bestDistance - _limits.ProgressMetres)
        {
            _bestDistance = distance;
            _bestAt = now;
        }
        else if (now - _bestAt >= _limits.StallSeconds)
        {
            unreachable();
            return TourInstruction.Hold;
        }

        return new TourInstruction(TourOrder.Walk, to, string.Empty);
    }

    private TourInstruction Issue(TourOrder order, CollectionPoint at, string key)
    {
        if (_orderIssued)
        {
            return TourInstruction.Hold;
        }

        _orderIssued = true;
        return new TourInstruction(order, at, key);
    }

    private void EnterPicking()
    {
        Phase = TourPhase.Picking;
        _orderIssued = false;
    }

    private void EnterDepositing()
    {
        Phase = TourPhase.Depositing;
        _orderIssued = false;
    }

    private void EnterToChest()
    {
        Phase = TourPhase.ToChest;
        ResetWalk();
    }

    private void SourceUnreachable()
    {
        // Bounded local recovery has failed. The source is owed, not gone: the
        // driver plans for it again or gives up on it by its own rules.
        Steps++;
        _pump.Failed();
        _hasStep = false;
        Phase = TourPhase.Asking;
        ResetWalk();
    }

    private void ChestUnreachable()
    {
        Stop("he could not get to the chest you chose, so he stopped with what he collected still on him");
    }

    private void Stop(string why)
    {
        if (!IsOver)
        {
            try
            {
                _pump.Abandon();
            }
            finally
            {
                Phase = TourPhase.Stopped;
                Reason = why;
                _hasStep = false;
            }
        }
    }

    private void ResetWalk()
    {
        _bestDistance = float.PositiveInfinity;
        _bestAt = float.NaN;
    }

    private static string Suffix(string detail) =>
        string.IsNullOrEmpty(detail) ? "." : " (" + detail + ").";
}
