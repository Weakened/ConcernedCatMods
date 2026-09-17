using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary><b>PLACEHOLDER</b> for agent B's motion judgement (#314,
/// <c>Domain/Hauling/Navigation</c>), so Gunnar's mechanics can be exercised end
/// to end before B lands. Replaced wholesale at integration.
///
/// It judges progress from <b>both</b> bodies over fixed windows of
/// <see cref="HaulLimits.StallWindowSeconds"/> while the motor is commanded:
/// the cart moved at least <see cref="HaulLimits.StallCartDisplacementMetres"/>
/// is progress; the cart did not but Gunnar did is <see cref="HaulMotion.Wedged"/>
/// (something holds the cart); neither did is <see cref="HaulMotion.Stalled"/>.
/// A walking animation against a wedged cart is therefore never progress. It
/// keeps no failure cache: the executor's per-leg recovery budget is what
/// bounds retries until B's monitor, which remembers failure sites, lands.
/// </summary>
internal sealed class PlaceholderHaulMotionMonitor : IHaulMotionMonitor
{
    private readonly HaulLimits _limits;
    private bool _windowOpen;
    private float _windowStartedAt;
    private WorkPoint _pullerAtStart;
    private WorkPoint _cartAtStart;

    public PlaceholderHaulMotionMonitor(HaulLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public HaulMotion Current { get; private set; } = HaulMotion.Idle;

    public void Sample(float now, WorkPoint puller, WorkPoint cart, bool motorCommanded)
    {
        if (!motorCommanded)
        {
            Current = HaulMotion.Idle;
            _windowOpen = false;
            return;
        }

        if (!_windowOpen)
        {
            _windowOpen = true;
            _windowStartedAt = now;
            _pullerAtStart = puller;
            _cartAtStart = cart;
            if (Current == HaulMotion.Idle)
            {
                Current = HaulMotion.Progressing;
            }

            return;
        }

        if (now - _windowStartedAt < _limits.StallWindowSeconds)
        {
            return;
        }

        float cartMoved = cart.HorizontalDistanceTo(_cartAtStart);
        float pullerMoved = puller.HorizontalDistanceTo(_pullerAtStart);
        if (cartMoved >= _limits.StallCartDisplacementMetres)
        {
            Current = HaulMotion.Progressing;
        }
        else
        {
            Current = pullerMoved >= _limits.StallCartDisplacementMetres ? HaulMotion.Wedged : HaulMotion.Stalled;
        }

        _windowStartedAt = now;
        _pullerAtStart = puller;
        _cartAtStart = cart;
    }

    public void Reset()
    {
        Current = HaulMotion.Idle;
        _windowOpen = false;
    }
}
