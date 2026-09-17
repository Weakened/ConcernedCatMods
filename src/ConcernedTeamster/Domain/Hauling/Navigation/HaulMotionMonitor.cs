using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>Judges a pull from BOTH bodies (CART-06): agent B's implementation
/// of <see cref="IHaulMotionMonitor"/>. A walking animation against a wedged
/// cart is not progress, so the cart's own movement decides.
///
/// While the motor is commanded, samples are kept for at least
/// <see cref="HaulLimits.StallWindowSeconds"/>. Until a whole window has passed
/// the pull is given the benefit of the doubt (Progressing). After that, over the
/// most recent window:
/// - the cart moved at least <see cref="HaulLimits.StallCartDisplacementMetres"/>
///   (net, flat - rocking back and forth is not progress): Progressing;
/// - it did not, but Gunnar got that far from where the window began, or is
///   there now: Wedged - something holds the cart;
/// - neither: Stalled.
///
/// Vanilla's hitch is rigid along the cart's length, so a held cart usually holds
/// Gunnar too and shows as Stalled; recovery treats the two alike. Any sample
/// with the motor not commanded is Idle and starts the window over. Samples that
/// are not finite are ignored, and a clock that runs backwards starts over. Clock
/// is the caller's, in seconds; allocation-free after construction.</summary>
internal sealed class HaulMotionMonitor : IHaulMotionMonitor
{
    /// <summary>Samples kept. With one recorded per
    /// <see cref="RecordsPerWindow"/>th of a window this covers two windows, at
    /// any sampling rate.</summary>
    public const int Capacity = 64;

    public const int RecordsPerWindow = 32;

    private readonly HaulLimits _limits;
    private readonly Record[] _records = new Record[Capacity];
    private int _count;
    private int _next;
    private Record _latest;
    private bool _hasLatest;
    private float _lastTime = float.NegativeInfinity;

    public HaulMotionMonitor(HaulLimits limits)
    {
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Validate();
        Current = HaulMotion.Idle;
    }

    public HaulMotion Current { get; private set; }

    /// <summary>How long the motor has been commanded without a break, as far
    /// as the kept samples reach.</summary>
    public float CommandedSeconds { get; private set; }

    /// <summary>The cart's net flat movement over the judged window.</summary>
    public float CartDisplacementMetres { get; private set; }

    /// <summary>Gunnar's net flat movement over the judged window.</summary>
    public float PullerDisplacementMetres { get; private set; }

    /// <summary>The furthest Gunnar got from where the judged window began.
    /// </summary>
    public float PullerExcursionMetres { get; private set; }

    public void Sample(float now, WorkPoint puller, WorkPoint cart, bool motorCommanded)
    {
        if (!CartRouteGeometry.IsFiniteValue(now) || !puller.IsFinite || !cart.IsFinite)
        {
            return;
        }

        if (now < _lastTime)
        {
            Reset();
        }

        _lastTime = now;
        if (!motorCommanded)
        {
            ClearWindow();
            Current = HaulMotion.Idle;
            return;
        }

        var record = new Record(now, puller, cart);
        float interval = _limits.StallWindowSeconds / RecordsPerWindow;
        if (_count == 0 || now - Newest().Time >= interval)
        {
            _records[_next] = record;
            _next = (_next + 1) % Capacity;
            _count = Math.Min(Capacity, _count + 1);
        }

        _latest = record;
        _hasLatest = true;
        Current = Judge();
    }

    public void Reset()
    {
        ClearWindow();
        _lastTime = float.NegativeInfinity;
        Current = HaulMotion.Idle;
    }

    private void ClearWindow()
    {
        _count = 0;
        _next = 0;
        _hasLatest = false;
        CommandedSeconds = 0f;
        CartDisplacementMetres = 0f;
        PullerDisplacementMetres = 0f;
        PullerExcursionMetres = 0f;
    }

    private HaulMotion Judge()
    {
        if (!_hasLatest || _count == 0)
        {
            return HaulMotion.Idle;
        }

        float window = _limits.StallWindowSeconds;
        Record oldest = At(0);
        CommandedSeconds = _latest.Time - oldest.Time;
        if (CommandedSeconds < window)
        {
            CartDisplacementMetres = 0f;
            PullerDisplacementMetres = 0f;
            PullerExcursionMetres = 0f;
            return HaulMotion.Progressing;
        }

        // The newest kept sample at least a window old begins the judged window.
        int start = 0;
        for (int index = _count - 1; index >= 0; index--)
        {
            if (_latest.Time - At(index).Time >= window)
            {
                start = index;
                break;
            }
        }

        Record begin = At(start);
        float excursion = 0f;
        for (int index = start + 1; index < _count; index++)
        {
            excursion = Math.Max(excursion, begin.Puller.HorizontalDistanceTo(At(index).Puller));
        }

        CartDisplacementMetres = begin.Cart.HorizontalDistanceTo(_latest.Cart);
        PullerDisplacementMetres = begin.Puller.HorizontalDistanceTo(_latest.Puller);
        PullerExcursionMetres = Math.Max(excursion, PullerDisplacementMetres);

        float threshold = _limits.StallCartDisplacementMetres;
        if (CartDisplacementMetres >= threshold)
        {
            return HaulMotion.Progressing;
        }

        return PullerExcursionMetres >= threshold ? HaulMotion.Wedged : HaulMotion.Stalled;
    }

    private Record Newest() => _records[(_next - 1 + Capacity) % Capacity];

    /// <summary>The kept sample at <paramref name="index"/>, oldest first.
    /// </summary>
    private Record At(int index) => _records[(_next - _count + index + Capacity) % Capacity];

    private readonly struct Record
    {
        public Record(float time, WorkPoint puller, WorkPoint cart)
        {
            Time = time;
            Puller = puller;
            Cart = cart;
        }

        public float Time { get; }

        public WorkPoint Puller { get; }

        public WorkPoint Cart { get; }
    }
}
