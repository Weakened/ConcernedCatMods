using System;
using System.Linq;
using System.Reflection;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>Gunnar's idle hammer work on his cart is scenery, and #381 asks for
/// a test that proves it.
///
/// <b>Two kinds of proof, and the second is the one that lasts.</b> The
/// behavioural tests run the routine for an in-game hour and compare the cart
/// before and after. The structural one asserts that the routine has no way to
/// say anything but "walk", "face", "swing" or "nothing" - so a later change
/// that started repairing something would have to add a value to an enum whose
/// doc comment says what it is for, in a commit somebody reads.</summary>
public sealed class CartUpkeepIdleTests
{
    private static CartCondition Cart(bool attached = false) =>
        new CartCondition(healthFraction: 0.42f, massKilograms: 310.5f, cargoUnits: 37, braked: false,
            attached: attached);

    [Fact]
    public void An_hour_of_idling_changes_nothing_about_the_cart()
    {
        var idle = new CartUpkeepIdle(intervalSeconds: 45d);
        CartCondition before = Cart();
        CartCondition condition = before;

        for (double now = 0d; now < 3600d; now += 1d)
        {
            idle.Decide(idle: true, cartIsHis: true, condition, distanceMetres: 1.0d, nowSeconds: now);
        }

        Assert.Equal(before, condition);
        Assert.Equal(before.HealthFraction, condition.HealthFraction);
        Assert.Equal(before.MassKilograms, condition.MassKilograms);
        Assert.Equal(before.CargoUnits, condition.CargoUnits);
        Assert.True(idle.Swings > 0, "an hour beside the cart with nothing to do should produce some swinging");
    }

    [Fact]
    public void The_routine_can_only_ever_say_four_things()
    {
        // The guarantee, structurally. There is no value for repair, consume,
        // heal or bless, so the code that drives this cannot ask for one.
        var actions = Enum.GetNames(typeof(CartUpkeepAction));

        Assert.Equal(
            new[] { "Nothing", "WalkToCart", "FaceCart", "PlayHammerOnce" },
            actions);
    }

    [Fact]
    public void The_routine_is_handed_the_cart_and_has_no_way_to_hand_one_back()
    {
        // A method that returned a CartCondition would be a method that could
        // return a different one. None does, and none takes anything that could
        // carry a write either.
        MethodInfo[] methods = typeof(CartUpkeepIdle)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToArray();

        Assert.All(methods, method =>
            Assert.NotEqual(typeof(CartCondition), method.ReturnType));
        Assert.All(methods, method =>
            Assert.All(method.GetParameters(), parameter =>
                Assert.False(parameter.ParameterType.IsByRef,
                    method.Name + " takes " + parameter.Name + " by reference, which is a way to write it")));
        Assert.All(
            typeof(CartCondition).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.False(property.CanWrite, property.Name + " is settable"));
    }

    [Fact]
    public void He_does_nothing_at_all_until_the_interval_has_passed()
    {
        var idle = new CartUpkeepIdle(intervalSeconds: 45d);

        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, true, Cart(), 1d, 0d));
        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, true, Cart(), 1d, 44d));
        Assert.Equal(CartUpkeepAction.PlayHammerOnce, idle.Decide(true, true, Cart(), 1d, 45d));
        Assert.Equal(1, idle.Swings);
    }

    [Fact]
    public void He_walks_over_before_he_swings()
    {
        var idle = new CartUpkeepIdle(intervalSeconds: 10d, walkWithinMetres: 2.5d, faceWithinMetres: 3.5d);
        idle.Decide(true, true, Cart(), 20d, 0d);

        Assert.Equal(CartUpkeepAction.WalkToCart, idle.Decide(true, true, Cart(), 20d, 10d));
        Assert.Equal(CartUpkeepAction.FaceCart, idle.Decide(true, true, Cart(), 3d, 11d));
        Assert.Equal(CartUpkeepAction.PlayHammerOnce, idle.Decide(true, true, Cart(), 1d, 12d));
    }

    [Fact]
    public void A_cart_something_is_jointed_to_is_a_cart_in_use()
    {
        var idle = new CartUpkeepIdle(intervalSeconds: 1d);
        idle.Decide(true, true, Cart(), 1d, 0d);

        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, true, Cart(attached: true), 1d, 100d));
        Assert.Equal(0, idle.Swings);
    }

    [Fact]
    public void A_job_holds_him_and_the_timer_does_not_bank_a_swing_he_owes()
    {
        // Idling for a while, then working for an hour, then idling again: the
        // hour of work must not produce an immediate swing the moment he stops.
        var idle = new CartUpkeepIdle(intervalSeconds: 45d);
        idle.Decide(true, true, Cart(), 1d, 0d);
        idle.Decide(idle: false, cartIsHis: true, Cart(), 1d, 10d);

        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, true, Cart(), 1d, 3600d));
        Assert.Equal(CartUpkeepAction.PlayHammerOnce, idle.Decide(true, true, Cart(), 1d, 3646d));
    }

    [Fact]
    public void Somebody_elses_cart_is_not_his_to_hammer()
    {
        var idle = new CartUpkeepIdle(intervalSeconds: 1d);
        idle.Decide(true, cartIsHis: false, Cart(), 1d, 0d);

        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, cartIsHis: false, Cart(), 1d, 100d));
        Assert.Equal(0, idle.Swings);
    }

    [Fact]
    public void A_distance_nobody_could_read_produces_nothing_rather_than_a_walk_to_nowhere()
    {
        var idle = new CartUpkeepIdle(intervalSeconds: 1d);
        idle.Decide(true, true, Cart(), 1d, 0d);

        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, true, Cart(), double.NaN, 100d));
        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, true, Cart(), -1d, 100d));
    }

    [Fact]
    public void A_world_going_away_forgets_the_timer()
    {
        var idle = new CartUpkeepIdle(intervalSeconds: 45d);
        idle.Decide(true, true, Cart(), 1d, 0d);
        idle.Reset();

        Assert.Equal(CartUpkeepAction.Nothing, idle.Decide(true, true, Cart(), 1d, 1000d));
    }

    [Fact]
    public void An_interval_of_nothing_is_a_machine_and_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CartUpkeepIdle(intervalSeconds: 0d));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CartUpkeepIdle(45d, walkWithinMetres: 5d, faceWithinMetres: 1d));
    }
}
