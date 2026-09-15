using TheConcernedCat.Companions.Quest;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>What the runtime should be doing about the collectible this frame.</summary>
internal enum CompassSignal
{
    /// <summary>Out of range, or the introduction is over. Nothing to show.</summary>
    Idle = 0,

    /// <summary>Near enough that the player has plainly seen it; worth
    /// recording as discovered.</summary>
    Noticed = 1,

    /// <summary>Close enough to examine. The prompt is shown.</summary>
    InReach = 2,
}

/// <summary>Distance-driven presence for the Broken Compass, with hysteresis.
///
/// Hysteresis is not a nicety here. Without it, a player standing at exactly
/// the prompt distance — which is where a player naturally stops, because that
/// is where the prompt appeared — makes the prompt strobe at their walking
/// frequency. The exit radius is deliberately larger than the entry radius, so
/// crossing in and crossing out are different events.
///
/// This type is pure arithmetic over a distance: no Unity, no transform, no
/// per-frame allocation. The adapter feeds it one float.</summary>
internal sealed class CompassProximity
{
    public const float DefaultNoticeRadius = 14f;
    public const float DefaultReachRadius = 4.5f;
    public const float DefaultHysteresis = 1.25f;

    private readonly float _noticeRadius;
    private readonly float _reachRadius;
    private readonly float _hysteresis;

    private bool _noticed;
    private bool _inReach;

    public CompassProximity(
        float noticeRadius = DefaultNoticeRadius,
        float reachRadius = DefaultReachRadius,
        float hysteresis = DefaultHysteresis)
    {
        _noticeRadius = noticeRadius > 0f ? noticeRadius : DefaultNoticeRadius;
        _reachRadius = reachRadius > 0f ? reachRadius : DefaultReachRadius;
        _hysteresis = hysteresis > 0f ? hysteresis : DefaultHysteresis;
    }

    /// <summary>True once the player has been close enough to have seen it in
    /// this presence session. Cleared by <see cref="Reset"/>.</summary>
    public bool HasNoticed => _noticed;

    /// <summary>Updates from one distance reading.</summary>
    /// <param name="distance">Metres from the player to the collectible. A
    /// negative distance means "not measurable" (nothing loaded, no player)
    /// and reads as out of range rather than as zero.</param>
    public CompassSignal Update(float distance)
    {
        if (distance < 0f || float.IsNaN(distance))
        {
            _inReach = false;
            return CompassSignal.Idle;
        }

        _inReach = _inReach
            ? distance <= _reachRadius + _hysteresis
            : distance <= _reachRadius;

        if (_inReach)
        {
            _noticed = true;
            return CompassSignal.InReach;
        }

        if (distance <= _noticeRadius)
        {
            _noticed = true;
            return CompassSignal.Noticed;
        }

        return CompassSignal.Idle;
    }

    /// <summary>Forgets this presence session. Called when the collectible is
    /// rebuilt somewhere else, so a newly placed compass is noticed on its own
    /// merits rather than inheriting the last one's history.</summary>
    public void Reset()
    {
        _noticed = false;
        _inReach = false;
    }
}

/// <summary>The exact order of quest transitions the introduction applies, and
/// the only place that order is written down.
///
/// Examining runs three transitions rather than one. The state machine already
/// promotes forward past skipped stages, so a single
/// <see cref="QuestTransition.BeginIntroduction"/> would reach the same state —
/// but then a player who examined the compass and quit before reading anything
/// would have no record of having found it. Running the sequence means every
/// stage the player actually reached is on disk, which is what makes an
/// interrupted introduction resumable instead of merely repeatable.</summary>
internal static class IntroductionSequence
{
    /// <summary>Transitions to apply when the player examines the collectible,
    /// in order. Each one is idempotent, so examining twice is free.</summary>
    public static readonly QuestTransition[] OnExamine =
    {
        QuestTransition.Discover,
        QuestTransition.Collect,
        QuestTransition.BeginIntroduction,
    };

    /// <summary>Applied when the player comes near enough to see it.</summary>
    public static readonly QuestTransition[] OnNoticed =
    {
        QuestTransition.Discover,
    };

    /// <summary>Welcoming Hulgi. One transition, and the only one that adds a
    /// companion.</summary>
    public static readonly QuestTransition[] OnWelcome =
    {
        QuestTransition.Welcome,
    };

    /// <summary>Choosing the tools without the story. Completes the
    /// introduction without recruiting anybody.</summary>
    public static readonly QuestTransition[] OnSkip =
    {
        QuestTransition.Skip,
    };
}
