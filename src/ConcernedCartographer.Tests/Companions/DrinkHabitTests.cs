using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Companions;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>Hulgi's drink: when he has one, when he toasts first, and the order
/// the steps come in.</summary>
public sealed class DrinkHabitTests
{
    private static Func<double> Rolls(params double[] values)
    {
        var queue = new Queue<double>(values);
        return () => queue.Count > 0 ? queue.Dequeue() : 0.5d;
    }

    private static List<DrinkPhase> PhasesOf(bool toast, bool standsUp)
    {
        var seen = new List<DrinkPhase>();
        float length = DrinkTimeline.Length(toast, standsUp);
        for (float t = 0f; t < length + 1f; t += 0.05f)
        {
            DrinkPhase phase = DrinkTimeline.PhaseAt(t, toast, standsUp, out _);
            if (seen.Count == 0 || seen[seen.Count - 1] != phase)
            {
                seen.Add(phase);
            }
        }

        return seen;
    }

    [Fact]
    public void APlainDrinkIsHadWhereHeSits()
    {
        // No toast, so nothing to get up for - even when he is sitting. The
        // mug comes out, he drinks, it goes away.
        Assert.Equal(
            new[] { DrinkPhase.Raise, DrinkPhase.Drink, DrinkPhase.Lower, DrinkPhase.Done },
            PhasesOf(toast: false, standsUp: true));
        Assert.Equal(
            DrinkTimeline.RaiseSeconds + DrinkTimeline.DrinkSeconds + DrinkTimeline.LowerSeconds,
            DrinkTimeline.Length(toast: false, standsUp: true),
            3);
    }

    [Fact]
    public void AToastComesBeforeTheDrinkNotAfterIt()
    {
        Assert.Equal(
            new[]
            {
                DrinkPhase.Raise, DrinkPhase.Toast, DrinkPhase.Drink, DrinkPhase.Lower, DrinkPhase.Done,
            },
            PhasesOf(toast: true, standsUp: false));
    }

    [Fact]
    public void AToastFromSittingGetsUpForItAndSitsBackDownAfterwards()
    {
        Assert.Equal(
            new[]
            {
                DrinkPhase.StandUp, DrinkPhase.Raise, DrinkPhase.Toast, DrinkPhase.Drink,
                DrinkPhase.Lower, DrinkPhase.SitDown, DrinkPhase.Done,
            },
            PhasesOf(toast: true, standsUp: true));

        Assert.Equal(
            DrinkTimeline.StandUpSeconds + DrinkTimeline.RaiseSeconds + DrinkTimeline.ToastSeconds +
            DrinkTimeline.DrinkSeconds + DrinkTimeline.LowerSeconds + DrinkTimeline.SitDownSeconds,
            DrinkTimeline.Length(toast: true, standsUp: true),
            3);
    }

    [Fact]
    public void ProgressRunsThroughEachStepFromNothingToAll()
    {
        float halfwayThroughTheToast = DrinkTimeline.RaiseSeconds + (DrinkTimeline.ToastSeconds / 2f);
        Assert.Equal(
            DrinkPhase.Toast,
            DrinkTimeline.PhaseAt(halfwayThroughTheToast, toast: true, standsUp: false, out float progress));
        Assert.Equal(0.5f, progress, 3);

        Assert.Equal(DrinkPhase.None, DrinkTimeline.PhaseAt(-1f, toast: true, standsUp: true, out _));
        Assert.Equal(
            DrinkPhase.Done,
            DrinkTimeline.PhaseAt(DrinkTimeline.Length(true, true), toast: true, standsUp: true, out float done));
        Assert.Equal(1f, done);
    }

    [Fact]
    public void TheFirstDrinkComesSoonerThanTheRest()
    {
        // Lowest roll every time, so every wait is its minimum.
        var habit = new DrinkHabit(() => 0d);
        Assert.Equal(DrinkHabit.FirstDrinkMinimumSeconds, habit.SecondsUntilNext, 3);

        Assert.False(habit.Tick(DrinkHabit.FirstDrinkMinimumSeconds - 1f, canDrink: true, out _));
        Assert.True(habit.Tick(1f, canDrink: true, out _));

        Assert.Equal(DrinkHabit.BetweenDrinksMinimumSeconds, habit.SecondsUntilNext, 3);
        Assert.True(DrinkHabit.FirstDrinkMaximumSeconds < DrinkHabit.BetweenDrinksMinimumSeconds);
    }

    [Fact]
    public void TheWaitPausesWhileHeCouldNotDrink()
    {
        // Walking across camp does not use up the wait, or he would sit down
        // and reach for his mug the moment he arrived.
        var habit = new DrinkHabit(() => 0d);
        Assert.False(habit.Tick(DrinkHabit.FirstDrinkMaximumSeconds * 10f, canDrink: false, out _));
        Assert.Equal(DrinkHabit.FirstDrinkMinimumSeconds, habit.SecondsUntilNext, 3);
    }

    [Fact]
    public void OnlySomeDrinksStartWithAToast()
    {
        // First wait, then (next wait, toast roll) per drink.
        var habit = new DrinkHabit(Rolls(0d, 0d, 0.1d, 0d, 0.9d, 0d, 0.34d));

        Assert.True(habit.Tick(DrinkHabit.FirstDrinkMinimumSeconds, true, out DrinkPlan first));
        Assert.True(first.Toast);

        Assert.True(habit.Tick(DrinkHabit.BetweenDrinksMinimumSeconds, true, out DrinkPlan second));
        Assert.False(second.Toast);

        // Just over one in three is not a toast.
        Assert.True(habit.Tick(DrinkHabit.BetweenDrinksMinimumSeconds, true, out DrinkPlan third));
        Assert.False(third.Toast);
    }

    [Fact]
    public void ADrinkAskedForByHandStillResetsTheWait()
    {
        // cc_companion drink: the toast is whatever was asked for, and the
        // next drink he has on his own is the usual while after it - not a
        // moment later because the clock had nearly run out anyway.
        var habit = new DrinkHabit(() => 0.99d);
        habit.Tick(DrinkHabit.FirstDrinkMaximumSeconds - 1f, true, out _);

        Assert.True(habit.Now(toast: true).Toast);
        Assert.True(habit.SecondsUntilNext >= DrinkHabit.BetweenDrinksMinimumSeconds);

        Assert.False(new DrinkHabit(() => 0d).Now(toast: false).Toast);
    }

    [Fact]
    public void ARandomSourceOutOfRangeStillGivesASensibleWait()
    {
        foreach (double roll in new[] { double.NaN, -3d, 1d, 42d })
        {
            var habit = new DrinkHabit(() => roll);
            Assert.InRange(
                habit.SecondsUntilNext,
                DrinkHabit.FirstDrinkMinimumSeconds,
                DrinkHabit.FirstDrinkMaximumSeconds);
        }
    }
}
