using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>What happened to a request to reserve a fire.</summary>
internal enum ReservationOutcome
{
    /// <summary>Not taken. Zero, so an unfilled result never reads as held.
    /// </summary>
    Refused = 0,

    Taken = 1,

    /// <summary>The same fire is already reserved by this holder. Idempotent:
    /// reserving twice is reserving once.</summary>
    AlreadyHeld = 2,

    /// <summary>A different fire is reserved. One body, one job.</summary>
    Busy = 3,
}

/// <summary>The one fire the Steward is currently committed to.
///
/// <b>Why a type and not a field.</b> With one worker, "the reserved target" is
/// arguably just the job's target, and writing it as a field would work today.
/// It is a type because the two things that must be true of it are rules rather
/// than values: a second reservation is <b>refused</b> rather than overwriting
/// the first, and releasing a reservation somebody else holds does
/// <b>nothing</b>. Both are one line here and are otherwise scattered
/// conditions that a second caller quietly gets wrong.
///
/// It is also where the honest bound lives. #340 asks for a bounded
/// deterministic scan <i>and reservation</i>; the bound is that exactly one can
/// be held, which is a property of this object rather than a convention the
/// loop follows.</summary>
internal sealed class UpkeepReservation
{
    private FuelTargetKey _target;
    private string? _holder;

    public bool IsHeld => _holder != null;

    /// <summary>The reserved fire, or the default when nothing is held.
    /// </summary>
    public FuelTargetKey Target => _target;

    /// <summary>The job holding it, or null.</summary>
    public string? Holder => _holder;

    /// <summary>How many units the trip was planned for, at the moment it was
    /// planned.
    ///
    /// <b>A plan, never a promise.</b> The fire may have burned down further or
    /// been fed by the player during the walk, so the loop revalidates and
    /// measures rather than trusting this. It is kept so the evidence line can
    /// say what was intended beside what happened.</summary>
    public int PlannedUnits { get; private set; }

    /// <summary>Increments on every change, so a stale read is detectable.
    /// </summary>
    public int Revision { get; private set; }

    public ReservationOutcome Take(FuelTargetKey target, string holder, int plannedUnits)
    {
        if (target.IsEmpty)
        {
            throw new ArgumentException("A reservation needs a fire.", nameof(target));
        }

        if (string.IsNullOrEmpty(holder))
        {
            throw new ArgumentException("A reservation needs a holder.", nameof(holder));
        }

        if (plannedUnits < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedUnits), plannedUnits, "A trip is planned for at least one unit.");
        }

        if (_holder != null)
        {
            if (string.Equals(_holder, holder, StringComparison.Ordinal) && _target.Equals(target))
            {
                return ReservationOutcome.AlreadyHeld;
            }

            return ReservationOutcome.Busy;
        }

        _target = target;
        _holder = holder;
        PlannedUnits = plannedUnits;
        Revision++;
        return ReservationOutcome.Taken;
    }

    /// <summary>Gives the fire back. Releasing one held by somebody else changes
    /// nothing and is not an error — a job that has already been cleaned up
    /// must be able to say so without having to check first.</summary>
    public bool Release(string holder)
    {
        if (_holder == null || !string.Equals(_holder, holder, StringComparison.Ordinal))
        {
            return false;
        }

        _target = default;
        _holder = null;
        PlannedUnits = 0;
        Revision++;
        return true;
    }

    public bool IsHeldBy(string? holder) =>
        _holder != null && holder != null && string.Equals(_holder, holder, StringComparison.Ordinal);

    public override string ToString() =>
        _holder == null
            ? "<nothing reserved>"
            : _target + " for " + _holder + " x" +
              PlannedUnits.ToString(CultureInfo.InvariantCulture);
}
