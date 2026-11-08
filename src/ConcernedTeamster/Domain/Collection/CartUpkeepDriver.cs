using System;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>The only motor calls the idle hammer routine may make, all on
/// Gunnar's <b>own</b> body (the D14.2 own-body carve-out): walk with his own
/// pathfinding, turn, stop. There is nothing here that reaches a cart, an
/// inventory, a lease or a job - so the driver that holds one of these cannot
/// either.</summary>
internal interface ICartUpkeepMotor
{
    /// <summary>Walks toward a point with the body's own pathfinding, at
    /// walking pace.</summary>
    void WalkTo(WorkPoint target, float arrivalRadiusMetres);

    /// <summary>Turns toward a horizontal direction without moving.</summary>
    void Face(float directionX, float directionZ);

    /// <summary>Stops commanding motion.</summary>
    void Stop();
}

/// <summary>Everything the idle hammer routine is told about the world at one
/// worker tick, read once and handed over as a value.
///
/// <b>Read, never written.</b> <see cref="Read"/> takes the haul executor only to
/// copy what its getters answer; the driver is handed this struct and never the
/// executor, so it has no reference through which a job, a lease or a phase
/// could be changed.</summary>
internal readonly struct CartUpkeepFacts
{
    public CartUpkeepFacts(
        bool switchedOn,
        bool authorityGranted,
        bool idle,
        bool cartIsHis,
        bool bodyWorking,
        CartCondition cart,
        WorkPoint worker,
        WorkPoint cartPosition,
        WorkPoint standAt)
    {
        SwitchedOn = switchedOn;
        AuthorityGranted = authorityGranted;
        Idle = idle;
        CartIsHis = cartIsHis;
        BodyWorking = bodyWorking;
        Cart = cart;
        Worker = worker;
        CartPosition = cartPosition;
        StandAt = standAt;
    }

    /// <summary>Teamster, hauling and the idle hammer setting are all on, and
    /// the start-up capability probe verified every member the routine and its
    /// port bind.</summary>
    public bool SwitchedOn { get; }

    /// <summary>The shared work-authority rule (D3) grants: opted in, a world,
    /// the host, not dedicated, nobody else connected.</summary>
    public bool AuthorityGranted { get; }

    /// <summary>A cart is assigned and he is doing nothing with it: no haul,
    /// no leg, no joint, no hold, and nobody holds his identity.</summary>
    public bool Idle { get; }

    /// <summary>The cart observed is the one his lease names, and it
    /// resolved.</summary>
    public bool CartIsHis { get; }

    /// <summary>His body is present, owned, alive and not faulted.</summary>
    public bool BodyWorking { get; }

    public CartCondition Cart { get; }

    public WorkPoint Worker { get; }

    public WorkPoint CartPosition { get; }

    /// <summary>Where he stands to swing: the point a puller's pivot rests at
    /// the handle, outside the cart's body. Walking at the cart's centre would
    /// have him bump an unbraked cart and shove it - a world effect the idle
    /// routine is not allowed - so he walks to this and only faces the centre.
    /// </summary>
    public WorkPoint StandAt { get; }

    /// <summary>Copies what the executor's getters answer, and nothing else.
    ///
    /// <paramref name="cart"/> and <paramref name="body"/> are the executor's
    /// own last observations (<c>LastCart</c>, <c>LastBody</c>), which its frame
    /// observer refreshes every rendered frame while a cart is assigned, so this
    /// reads no cart and no body of its own.</summary>
    public static CartUpkeepFacts Read(
        bool switchedOn,
        WorkAuthorityVerdict authority,
        HaulExecutor? executor)
    {
        if (executor == null)
        {
            return default;
        }

        CartLease? lease = executor.ActiveLease;
        CartObservation cart = executor.LastCart;
        PullerBodyFacts body = executor.LastBody;

        bool attached = executor.Attached || cart.HasJoint || cart.AttachFlag;
        bool idle =
            lease != null &&
            executor.Phase == HaulPhase.Ready &&
            executor.HaulId.Length == 0 &&
            !executor.Attached &&
            !executor.HoldingCart &&
            !executor.StillHolding &&
            executor.PendingStop == HaulStopIntent.Unspecified &&
            executor.Modes.JobId == null;

        var condition = new CartCondition(
            healthFraction: float.NaN,
            massKilograms: cart.BodyMassSumKg,
            cargoUnits: cart.CargoStacks,
            braked: cart.BrakeEngaged || cart.RootFrozen,
            attached: attached);

        return new CartUpkeepFacts(
            switchedOn,
            authority == WorkAuthorityVerdict.Granted,
            idle,
            cartIsHis: lease != null && cart.Resolved,
            bodyWorking: body.Present && !body.Dead && !body.Faulted,
            condition,
            body.Position,
            cart.CartPosition,
            cart.ApproachPoint);
    }
}

/// <summary>Turns <see cref="CartUpkeepIdle"/>'s answer into his own body's
/// motion and, once in a while, one call to the hammer port (#381, owner
/// decision 2026-10-31, <c>DECISIONS.md</c> D21).
///
/// <b>Game-free, so the claim is tested rather than promised.</b> Its whole
/// reach is the three own-body motor calls of <see cref="ICartUpkeepMotor"/> and
/// the one <see cref="Action"/> it is handed for the swing. It is given the facts
/// as a value and never the executor, the cart, an inventory or a lease, so
/// <c>CartUpkeepDriverTests</c> can run it for an in-game hour beside a real
/// executor and show that the cart, the lease, the phase, the identity hold and
/// the body's inventory are exactly what they were.
///
/// <b>What it answers.</b> The action it took. <see cref="CartUpkeepAction.Nothing"/>
/// means it took none and the caller hands the tick to the haul executor, which
/// stops him if he is still moving - so every way out of the routine, a job
/// arriving included, ends in the executor's own hands.</summary>
internal sealed class CartUpkeepDriver
{
    /// <summary>How far away an assigned cart may stand and still be one he
    /// notices. Beyond it he is not "idle near his cart" (GUNNAR_COLLECTION.md
    /// §8), the routine is disarmed, and he does not set off across the map to
    /// hammer at it.</summary>
    public const float NoticeWithinMetres = 12f;

    /// <summary>Where the walk stops: inside the routine's own "beside it"
    /// distance, so the walk ends where a swing may begin.</summary>
    public const float ArrivalRadiusMetres = 2f;

    /// <summary>A walk that has not arrived after this long is given up and the
    /// timer starts again: a cart he cannot path to must not hold him walking
    /// into it for ever.</summary>
    public const double GiveUpWalkingAfterSeconds = 20d;

    private readonly CartUpkeepIdle _idle;
    private readonly ICartUpkeepMotor _motor;
    private readonly Action _swing;
    private double _walkingSince = double.NaN;

    public CartUpkeepDriver(CartUpkeepIdle idle, ICartUpkeepMotor motor, Action swing)
    {
        _idle = idle ?? throw new ArgumentNullException(nameof(idle));
        _motor = motor ?? throw new ArgumentNullException(nameof(motor));
        _swing = swing ?? throw new ArgumentNullException(nameof(swing));
    }

    /// <summary>How many swings the routine has asked for.</summary>
    public int Swings => _idle.Swings;

    /// <summary>One worker tick.</summary>
    public CartUpkeepAction Step(CartUpkeepFacts facts, double nowSeconds)
    {
        bool positions = facts.Worker.IsFinite && facts.CartPosition.IsFinite && facts.StandAt.IsFinite;
        double distance = positions ? facts.Worker.HorizontalDistanceTo(facts.StandAt) : double.NaN;

        // Every gate folds into "idle": a switch off, authority refused, a body
        // that is not working, a position nobody could read, or a cart further
        // than he notices. Not idle disarms the timer, so none of those banks a
        // swing for later.
        bool idle =
            facts.SwitchedOn &&
            facts.AuthorityGranted &&
            facts.Idle &&
            facts.BodyWorking &&
            positions &&
            distance <= NoticeWithinMetres;

        CartUpkeepAction action = _idle.Decide(idle, facts.CartIsHis, facts.Cart, distance, nowSeconds);
        if (action != CartUpkeepAction.WalkToCart && action != CartUpkeepAction.FaceCart)
        {
            _walkingSince = double.NaN;
        }
        else if (double.IsNaN(_walkingSince))
        {
            _walkingSince = nowSeconds;
        }
        else if (nowSeconds - _walkingSince > GiveUpWalkingAfterSeconds)
        {
            _walkingSince = double.NaN;
            _idle.Reset();
            _motor.Stop();
            return CartUpkeepAction.Nothing;
        }

        float towardX = facts.CartPosition.X - facts.Worker.X;
        float towardZ = facts.CartPosition.Z - facts.Worker.Z;
        switch (action)
        {
            case CartUpkeepAction.WalkToCart:
                _motor.WalkTo(facts.StandAt, ArrivalRadiusMetres);
                return action;

            case CartUpkeepAction.FaceCart:
                // The last steps: still walking in, turned square to the cart
                // rather than to the path's last corner.
                _motor.WalkTo(facts.StandAt, ArrivalRadiusMetres);
                _motor.Face(towardX, towardZ);
                return action;

            case CartUpkeepAction.PlayHammerOnce:
                _motor.Stop();
                _motor.Face(towardX, towardZ);
                _swing();
                return action;

            default:
                return CartUpkeepAction.Nothing;
        }
    }

    /// <summary>Forgets the timer and any walk in progress, because the world it
    /// was counting in has gone.</summary>
    public void Reset()
    {
        _walkingSince = double.NaN;
        _idle.Reset();
    }
}
