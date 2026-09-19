using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>The only things Gunnar's idle cart routine is allowed to ask for.
/// Zero is "nothing".
///
/// <b>The list is the guarantee.</b> #381 says the idle hammer work is cosmetic
/// only: it repairs nothing, consumes nothing, restores no hit points and grants
/// no buff. The cheapest way to make that true rather than promised is for the
/// routine to have no way to say anything else - there is no value here for
/// "repair", "consume", "heal" or "bless", so the code that drives it cannot ask
/// for one, and adding one is a visible edit to a type whose doc comment says
/// what it is for.</summary>
internal enum CartUpkeepAction
{
    /// <summary>Nothing at all. The answer whenever the cart is not idle, is not
    /// his, is being pulled, or he has something better to do.</summary>
    Nothing = 0,

    /// <summary>Walk towards the cart. Through the vanilla motor, like every
    /// other step he takes.</summary>
    WalkToCart = 1,

    /// <summary>Turn to face it.</summary>
    FaceCart = 2,

    /// <summary>Play the hammer animation once. An animation trigger and
    /// nothing else: the same transient, unsaved visual the game plays for any
    /// character swinging anything.</summary>
    PlayHammerOnce = 3,
}

/// <summary>Everything about a cart that this routine may look at - and
/// therefore everything a test can compare before and after to prove nothing
/// moved.
///
/// It is a value type with no setters on purpose. The routine is handed one and
/// hands nothing back; there is no path by which it could write one.</summary>
internal readonly struct CartCondition : IEquatable<CartCondition>
{
    public CartCondition(
        float healthFraction,
        float massKilograms,
        int cargoUnits,
        bool braked,
        bool attached)
    {
        HealthFraction = healthFraction;
        MassKilograms = massKilograms;
        CargoUnits = cargoUnits;
        Braked = braked;
        Attached = attached;
    }

    /// <summary>How intact the cart is, as the game reports it.</summary>
    public float HealthFraction { get; }

    /// <summary>What the cart weighs right now. Read only - Teamster writes a
    /// mass in exactly one file, on Gunnar's own body, and never on a cart.
    /// </summary>
    public float MassKilograms { get; }

    public int CargoUnits { get; }

    public bool Braked { get; }

    /// <summary>Whether something is jointed to it right now.</summary>
    public bool Attached { get; }

    public bool Equals(CartCondition other) =>
        HealthFraction.Equals(other.HealthFraction) &&
        MassKilograms.Equals(other.MassKilograms) &&
        CargoUnits == other.CargoUnits &&
        Braked == other.Braked &&
        Attached == other.Attached;

    public override bool Equals(object? obj) => obj is CartCondition other && Equals(other);

    public override int GetHashCode() =>
        (HealthFraction, MassKilograms, CargoUnits, Braked, Attached).GetHashCode();

    public override string ToString() =>
        "health " + HealthFraction.ToString("0.##") + ", " + MassKilograms.ToString("0.#") + " kg, " +
        CargoUnits + " units" + (Braked ? ", braked" : string.Empty) + (Attached ? ", attached" : string.Empty);
}

/// <summary>Gunnar occasionally walks over to his idle cart, faces it and plays
/// the hammer animation. It is scenery (#381).
///
/// <b>What it does not do, and cannot.</b> It repairs nothing, consumes nothing,
/// restores no hit points and grants no buff. That is not a promise about the
/// code below; it is a property of what this type is able to say. Its entire
/// output is one <see cref="CartUpkeepAction"/> - walk, face, swing, or nothing
/// - and its entire input is a <see cref="CartCondition"/> it is handed. There
/// is no port here through which a hit point could be written, and the only
/// world-facing call the adapter makes on its behalf is an animation trigger,
/// which is transient, unsaved, and exactly what the game plays when any
/// character swings anything.
///
/// <b>Why he does it at all.</b> A worker who stands perfectly still between
/// jobs looks broken. This is the smallest thing that makes him look like
/// somebody waiting rather than somebody stopped, and the owner asked for it by
/// name.
///
/// <b>When he does not.</b> Never while the cart is attached to anything, never
/// while a job holds him, never when the cart is not the one he was assigned,
/// and never more often than the interval - so the animation is a thing a player
/// notices once in a while rather than a machine.</summary>
internal sealed class CartUpkeepIdle
{
    private readonly double _intervalSeconds;
    private readonly double _walkWithinMetres;
    private readonly double _faceWithinMetres;
    private double _nextAt;
    private bool _armed;

    public CartUpkeepIdle(double intervalSeconds = 45d, double walkWithinMetres = 2.5d, double faceWithinMetres = 3.5d)
    {
        if (intervalSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "An interval of nothing is a machine.");
        }

        if (walkWithinMetres <= 0d || faceWithinMetres < walkWithinMetres)
        {
            throw new ArgumentOutOfRangeException(
                nameof(walkWithinMetres),
                "He has to be able to stand somewhere that counts as beside it.");
        }

        _intervalSeconds = intervalSeconds;
        _walkWithinMetres = walkWithinMetres;
        _faceWithinMetres = faceWithinMetres;
    }

    /// <summary>How many times the hammer animation has been asked for. For the
    /// status line and for the test that proves the animation happened <b>and</b>
    /// that nothing else did.</summary>
    public int Swings { get; private set; }

    /// <summary>What he should do about his idle cart at this instant.
    ///
    /// <paramref name="condition"/> is taken and never returned, and no overload
    /// of this method returns one: the caller compares what it read before with
    /// what it reads after, and they are equal because nothing here can make
    /// them differ.</summary>
    public CartUpkeepAction Decide(
        bool idle,
        bool cartIsHis,
        CartCondition condition,
        double distanceMetres,
        double nowSeconds)
    {
        if (!idle || !cartIsHis || condition.Attached)
        {
            // A cart something is jointed to is a cart in use, and a worker with
            // a job has a job. Both disarm, so the timer does not accumulate a
            // swing he owes from a period when he was busy.
            _armed = false;
            return CartUpkeepAction.Nothing;
        }

        if (double.IsNaN(distanceMetres) || distanceMetres < 0d)
        {
            return CartUpkeepAction.Nothing;
        }

        if (!_armed)
        {
            _armed = true;
            _nextAt = nowSeconds + _intervalSeconds;
            return CartUpkeepAction.Nothing;
        }

        if (nowSeconds < _nextAt)
        {
            return CartUpkeepAction.Nothing;
        }

        if (distanceMetres > _faceWithinMetres)
        {
            return CartUpkeepAction.WalkToCart;
        }

        if (distanceMetres > _walkWithinMetres)
        {
            return CartUpkeepAction.FaceCart;
        }

        _nextAt = nowSeconds + _intervalSeconds;
        Swings++;
        return CartUpkeepAction.PlayHammerOnce;
    }

    /// <summary>Forgets the timer, because the world it was counting in has
    /// gone.</summary>
    public void Reset()
    {
        _armed = false;
        _nextAt = 0d;
    }
}
