using System;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>What one drink looks like.</summary>
internal readonly struct DrinkPlan
{
    public DrinkPlan(bool toast)
    {
        Toast = toast;
    }

    /// <summary>Whether he raises his mug in a toast before he drinks.</summary>
    public bool Toast { get; }
}

/// <summary>The steps of one drink, in the order they happen.</summary>
internal enum DrinkPhase
{
    /// <summary>Not drinking.</summary>
    None = 0,

    /// <summary>Getting to his feet for a toast. Only when he was sitting and
    /// is toasting: a toast is a standing thing, and the game will not start
    /// one from a seat either.</summary>
    StandUp = 1,

    /// <summary>The mug is in his hand, before anything is done with it.
    /// </summary>
    Raise = 2,

    /// <summary>The toast. Only when this drink has one.</summary>
    Toast = 3,

    /// <summary>The drink itself.</summary>
    Drink = 4,

    /// <summary>The mug goes away at the end of this.</summary>
    Lower = 5,

    /// <summary>Back down where he was. Only when he stood up for the toast.
    /// </summary>
    SitDown = 6,

    /// <summary>Finished.</summary>
    Done = 7,
}

/// <summary>How long each step of a drink takes, and which step a drink is on
/// after a given time.
///
/// A timeline rather than a state machine that waits on the animator: the
/// animations are the game's own, their lengths are asset data nobody here can
/// read before runtime, and a drink that waits for an animation event which
/// never comes is a companion standing with a mug in his hand forever. A fixed
/// timeline always finishes.</summary>
internal static class DrinkTimeline
{
    public const float StandUpSeconds = 1.1f;

    public const float RaiseSeconds = 0.5f;

    public const float ToastSeconds = 3f;

    public const float DrinkSeconds = 2.5f;

    public const float LowerSeconds = 0.5f;

    public const float SitDownSeconds = 1.1f;

    /// <summary>The whole drink, start to finish. <paramref name="standsUp"/>
    /// counts only with a toast, exactly as in <see cref="PhaseAt"/>.</summary>
    public static float Length(bool toast, bool standsUp)
    {
        float length = RaiseSeconds + DrinkSeconds + LowerSeconds;
        if (toast)
        {
            length += ToastSeconds;

            if (standsUp)
            {
                length += StandUpSeconds + SitDownSeconds;
            }
        }

        return length;
    }

    /// <summary>The step a drink is on <paramref name="elapsed"/> seconds in,
    /// and how far through that step it is, from 0 to 1.</summary>
    /// <param name="standsUp">Whether he gets up for it. Ignored without a
    /// toast: a plain drink is had wherever he is sitting.</param>
    public static DrinkPhase PhaseAt(float elapsed, bool toast, bool standsUp, out float progress)
    {
        progress = 0f;
        if (elapsed < 0f)
        {
            return DrinkPhase.None;
        }

        bool rises = toast && standsUp;
        float start = 0f;

        // Asked every frame of a drink, so it walks a fixed table rather than
        // building one.
        foreach ((DrinkPhase phase, float seconds) in Steps)
        {
            bool included = phase switch
            {
                DrinkPhase.StandUp => rises,
                DrinkPhase.Toast => toast,
                DrinkPhase.SitDown => rises,
                _ => true,
            };

            if (!included)
            {
                continue;
            }

            if (elapsed < start + seconds)
            {
                progress = (elapsed - start) / seconds;
                return phase;
            }

            start += seconds;
        }

        progress = 1f;
        return DrinkPhase.Done;
    }

    private static readonly (DrinkPhase Phase, float Seconds)[] Steps =
    {
        (DrinkPhase.StandUp, StandUpSeconds),
        (DrinkPhase.Raise, RaiseSeconds),
        (DrinkPhase.Toast, ToastSeconds),
        (DrinkPhase.Drink, DrinkSeconds),
        (DrinkPhase.Lower, LowerSeconds),
        (DrinkPhase.SitDown, SitDownSeconds),
    };
}

/// <summary>When he has a drink, and whether he toasts first.
///
/// Every so often, not on a beat: the gap is drawn afresh after every drink, so
/// two evenings at the same camp do not play out to the same clock. The first
/// one comes sooner than the rest, because a companion who has a drink at some
/// point in the next five minutes is a feature nobody who sits down to look for
/// it will ever see.
///
/// The clock only runs while he could drink - sitting, or standing about. A
/// walk across camp does not use up the wait, so he does not arrive somewhere
/// and immediately reach for his mug because the timer ran out on the way.
///
/// The toast is a chance, not a rule. Every drink with a toast in front of it
/// would turn a nice moment into a tic.</summary>
internal sealed class DrinkHabit
{
    public const float FirstDrinkMinimumSeconds = 45f;

    public const float FirstDrinkMaximumSeconds = 90f;

    public const float BetweenDrinksMinimumSeconds = 150f;

    public const float BetweenDrinksMaximumSeconds = 300f;

    /// <summary>One drink in three starts with a toast.</summary>
    public const double ToastChance = 1.0 / 3.0;

    private readonly Func<double> _random;
    private float _untilNext;

    /// <param name="random">A number in [0, 1) each time it is asked. The game
    /// passes a real random source; tests pass a fixed sequence.</param>
    public DrinkHabit(Func<double> random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _untilNext = Draw(FirstDrinkMinimumSeconds, FirstDrinkMaximumSeconds);
    }

    /// <summary>How long until the next drink, counted in time he could have
    /// one.</summary>
    public float SecondsUntilNext => _untilNext;

    /// <summary>Advances the wait. Returns true, with the drink to have, once
    /// it is time and he is somewhere he could have it.</summary>
    /// <param name="canDrink">Whether he is sitting or standing about, with
    /// nothing else going on. The wait pauses while this is false.</param>
    public bool Tick(float deltaTime, bool canDrink, out DrinkPlan plan)
    {
        plan = default;
        if (!canDrink)
        {
            return false;
        }

        _untilNext -= deltaTime;
        if (_untilNext > 0f)
        {
            return false;
        }

        plan = Now();
        return true;
    }

    /// <summary>A drink right now - the habit's own when it is due, or one
    /// asked for by hand. Either way the next one is the usual while after it,
    /// so a drink asked for is not followed by another a moment later.</summary>
    /// <param name="toast">Forces the toast one way or the other; null lets
    /// chance decide, as it does for the drinks he has on his own.</param>
    public DrinkPlan Now(bool? toast = null)
    {
        _untilNext = Draw(BetweenDrinksMinimumSeconds, BetweenDrinksMaximumSeconds);
        return new DrinkPlan(toast ?? _random() < ToastChance);
    }

    private float Draw(float minimum, float maximum)
    {
        double roll = _random();
        if (double.IsNaN(roll) || roll < 0d)
        {
            roll = 0d;
        }
        else if (roll >= 1d)
        {
            roll = 0.999999d;
        }

        return minimum + ((maximum - minimum) * (float)roll);
    }
}
