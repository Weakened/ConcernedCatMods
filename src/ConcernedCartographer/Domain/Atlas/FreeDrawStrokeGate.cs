using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>RC11 blocker 4: Free Draw creates a route entity only when a
/// stroke proves REAL — the hold has travelled at least the freehand point
/// spacing from where it began. A click-twitch, a hold that never moves,
/// or a hold that begins and ends over CC UI (the runtime feeds
/// held=false there) buffers at most one point and then evaporates,
/// so the route list can never fill with one-point fragments. Pure state
/// machine, exhaustively tested; the command handler just obeys the
/// decisions.</summary>
internal sealed class FreeDrawStrokeGate
{
    public enum DecisionKind
    {
        /// <summary>Nothing to do this frame.</summary>
        None,

        /// <summary>A real stroke begins: create the route and append
        /// BOTH <see cref="Decision.StrokeStart"/> and the current point.</summary>
        StartStroke,

        /// <summary>The stroke is live: append the current point.</summary>
        Append,

        /// <summary>The hold ended while still buffered: discard silently
        /// (this is the fragment-spam fix).</summary>
        DropBuffer,

        /// <summary>A live stroke ended (release / pointer over UI).</summary>
        EndStroke,
    }

    public readonly struct Decision
    {
        public Decision(DecisionKind kind, RoadPoint strokeStart)
        {
            Kind = kind;
            StrokeStart = strokeStart;
        }

        public DecisionKind Kind { get; }

        /// <summary>The buffered first point of the stroke; meaningful for
        /// <see cref="DecisionKind.StartStroke"/> only.</summary>
        public RoadPoint StrokeStart { get; }
    }

    private readonly float _minimumTravelMeters;
    private RoadPoint _bufferedStart;
    private bool _buffering;
    private bool _strokeLive;

    public FreeDrawStrokeGate(float minimumTravelMeters = RouteOperations.FreehandMinimumSpacingMeters)
    {
        _minimumTravelMeters = minimumTravelMeters;
    }

    public bool StrokeLive => _strokeLive;

    public Decision Observe(bool held, RoadPoint cursor)
    {
        if (!held)
        {
            if (_strokeLive)
            {
                _strokeLive = false;
                return new Decision(DecisionKind.EndStroke, default);
            }

            if (_buffering)
            {
                _buffering = false;
                return new Decision(DecisionKind.DropBuffer, default);
            }

            return new Decision(DecisionKind.None, default);
        }

        if (_strokeLive)
        {
            return new Decision(DecisionKind.Append, default);
        }

        if (!_buffering)
        {
            _buffering = true;
            _bufferedStart = cursor;
            return new Decision(DecisionKind.None, default);
        }

        if (_bufferedStart.HorizontalDistanceTo(cursor) >= _minimumTravelMeters)
        {
            _buffering = false;
            _strokeLive = true;
            return new Decision(DecisionKind.StartStroke, _bufferedStart);
        }

        return new Decision(DecisionKind.None, default);
    }

    /// <summary>Mode ended externally (Finish/Escape/panel close): forget
    /// everything without emitting a decision.</summary>
    public void Reset()
    {
        _buffering = false;
        _strokeLive = false;
    }
}
