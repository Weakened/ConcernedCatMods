using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>At most <see cref="PerMinute"/> navmesh path queries in any sixty
/// seconds, across every haul (<see cref="HaulLimits.PathQueriesPerMinute"/>).
/// A path query builds navmesh tiles around both of its ends, so it is the one
/// expensive question; a sliding window keeps a burst of re-plans from spending
/// what a whole minute allows at once and then some.
///
/// Clock-free: the caller passes its own time in seconds. Allocation-free after
/// construction. A clock that runs backwards (a new world, a reset timer) starts
/// the window over rather than locking planning out.</summary>
internal sealed class CartQueryBudget
{
    public const float WindowSeconds = 60f;

    private readonly float[] _takenAt;
    private int _count;
    private int _oldest;
    private float _latest = float.NegativeInfinity;

    public CartQueryBudget(int perMinute)
    {
        if (perMinute < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(perMinute), "At least one query a minute must be allowed.");
        }

        PerMinute = perMinute;
        _takenAt = new float[perMinute];
    }

    public int PerMinute { get; }

    /// <summary>Queries taken inside the window ending at <paramref name="now"/>.
    /// </summary>
    public int UsedAt(float now)
    {
        Expire(now);
        return _count;
    }

    public int RemainingAt(float now) => PerMinute - UsedAt(now);

    /// <summary>Takes one query at <paramref name="now"/> when the window has
    /// room. Nothing is taken when it does not.</summary>
    public bool TryTake(float now)
    {
        if (!CartRouteGeometry.IsFiniteValue(now))
        {
            return false;
        }

        Expire(now);
        if (_count >= PerMinute)
        {
            return false;
        }

        _takenAt[(_oldest + _count) % PerMinute] = now;
        _count++;
        _latest = now;
        return true;
    }

    /// <summary>The earliest time a query will be allowed: now when there is
    /// room, else when the oldest query in the window leaves it.</summary>
    public float NextAvailableAt(float now)
    {
        Expire(now);
        return _count < PerMinute ? now : _takenAt[_oldest] + WindowSeconds;
    }

    public void Reset()
    {
        _count = 0;
        _oldest = 0;
        _latest = float.NegativeInfinity;
    }

    private void Expire(float now)
    {
        if (!CartRouteGeometry.IsFiniteValue(now))
        {
            return;
        }

        if (now < _latest)
        {
            Reset();
            return;
        }

        while (_count > 0 && now - _takenAt[_oldest] >= WindowSeconds)
        {
            _oldest = (_oldest + 1) % PerMinute;
            _count--;
        }
    }
}

/// <summary>The clearance probes one plan may spend
/// (<see cref="HaulLimits.ClearanceProbesPerPlan"/>). Every sweep, overlap check
/// and side measurement takes one; when none are left the plan answers
/// <see cref="CartRouteVerdict.BudgetExhausted"/> instead of probing on.</summary>
internal sealed class CartProbeAllowance
{
    public CartProbeAllowance(int limit)
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "A probe allowance cannot be negative.");
        }

        Limit = limit;
    }

    public int Limit { get; }

    public int Used { get; private set; }

    public int Remaining => Limit - Used;

    public bool TryTake()
    {
        if (Used >= Limit)
        {
            return false;
        }

        Used++;
        return true;
    }

    public bool TryTake(int count)
    {
        if (count < 0 || Used + count > Limit)
        {
            return false;
        }

        Used += count;
        return true;
    }
}
