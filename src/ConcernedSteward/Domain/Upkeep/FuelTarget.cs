using System;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>Which fire, within one run of the world.
///
/// <b>Opaque here on purpose.</b> This layer never interprets the text; it
/// remembers it and compares it for equality, exactly as
/// <c>Designation.ContainerKey</c> does. The adapter decides what goes in it.
///
/// <b>And it is only good for one world load.</b> <c>ZDO.Load</c> opens with
/// <c>m_uid.SetID(++ZDOID.m_loadID)</c>, so every persisted object is handed a
/// fresh id in load order and a key written before a reload names nothing after
/// it — and, because the new ids are dense from one, it is likely to name some
/// <i>other</i> object. That is why a key carries the epoch it was minted in
/// and why <see cref="IsFrom"/> exists: a key from a previous epoch resolves to
/// nothing at all rather than to a guess.
///
/// Feeding the wrong fire is a small harm. Taking wood out of a chest because a
/// stale key happened to match it is not, and both follow from the same
/// mistake, so both are refused the same way.</summary>
internal readonly struct FuelTargetKey : IEquatable<FuelTargetKey>
{
    public FuelTargetKey(string value, string epoch)
    {
        Value = value ?? string.Empty;
        Epoch = epoch ?? string.Empty;
    }

    public string Value { get; }

    /// <summary>Which run of the world <see cref="Value"/> means anything in.
    /// </summary>
    public string Epoch { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value) || string.IsNullOrEmpty(Epoch);

    /// <summary>True when this key was minted in <paramref name="epoch"/> and
    /// therefore still names what it named. An empty current epoch resolves
    /// nothing: without knowing which run we are in, a key cannot be told from
    /// a stale one, and the safe answer is that none of them are current.
    /// </summary>
    public bool IsFrom(string? epoch) =>
        !IsEmpty
        && !string.IsNullOrEmpty(epoch)
        && string.Equals(Epoch, epoch, StringComparison.Ordinal);

    public bool Equals(FuelTargetKey other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal)
        && string.Equals(Epoch, other.Epoch, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FuelTargetKey other && Equals(other);

    public override int GetHashCode() => unchecked(
        (StringComparer.Ordinal.GetHashCode(Value ?? string.Empty) * 397)
        ^ StringComparer.Ordinal.GetHashCode(Epoch ?? string.Empty));

    public override string ToString() => IsEmpty ? "<no fire>" : Value + "@" + Epoch;
}

/// <summary>Everything the adapter could establish about one fire, at one
/// instant.
///
/// A plain snapshot with no behaviour, because it crosses the boundary between
/// the game and a layer that must be testable without it. Every field is
/// something the adapter <i>read</i>; nothing here is inferred, and the
/// domain's job is to decide what the combination means.</summary>
internal readonly struct FuelTargetObservation
{
    public FuelTargetObservation(
        FuelTargetKey key,
        SitePoint position,
        string fuelItemName,
        float fuel,
        float maxFuel,
        bool canRefill,
        bool infiniteFuel,
        bool ownedHere,
        bool accessGranted)
    {
        Key = key;
        Position = position;
        FuelItemName = fuelItemName ?? string.Empty;
        Fuel = fuel;
        MaxFuel = maxFuel;
        CanRefill = canRefill;
        InfiniteFuel = infiniteFuel;
        OwnedHere = ownedHere;
        AccessGranted = accessGranted;
    }

    public FuelTargetKey Key { get; }

    public SitePoint Position { get; }

    /// <summary><b>What this fire burns, according to the fire.</b>
    /// <c>Fireplace.m_fuelItem.m_itemData.m_shared.m_name</c>, read from the
    /// target rather than assumed to be wood. Vanilla's own acceptance test is
    /// a comparison against this string, so anything we do with a different one
    /// would be measuring a different question from the one the game answers.
    /// </summary>
    public string FuelItemName { get; }

    /// <summary>The current fuel level. A float: vanilla stores and decays it
    /// as one, so <c>9.5</c> is a real and common value.</summary>
    public float Fuel { get; }

    public float MaxFuel { get; }

    /// <summary><c>Fireplace.m_canRefill</c>. False for fires that take no
    /// fuel at all.</summary>
    public bool CanRefill { get; }

    /// <summary><c>Fireplace.m_infiniteFuel</c>. Never needs anything.</summary>
    public bool InfiniteFuel { get; }

    /// <summary>True only when this process owns the fire's network object
    /// <i>now</i>.
    ///
    /// This is the sharpest rule in the whole product and the audit
    /// (<c>VALHEIM_FIRE_API_AUDIT.md</c> section 3) is why. When a fire's ZDO
    /// is owned by nobody, vanilla's <c>UseItem</c> removes the wood from the
    /// user's inventory and then <c>RPC_AddFuel</c>'s own owner gate discards
    /// the fuel: the item is destroyed. When it is owned by another peer the
    /// mutation is routed away and nothing local is observable, which is the
    /// same as not knowing whether it happened.
    ///
    /// Both are refused before anything is withdrawn.</summary>
    public bool OwnedHere { get; }

    /// <summary>True when the ward check answered <c>Granted</c>. Anything else
    /// — denied, or unanswerable — is false, because an unestablished
    /// permission is not a permission.</summary>
    public bool AccessGranted { get; }

    /// <summary>How far below a full load this fire is, in whole units a person
    /// would recognise. Never negative.</summary>
    public int Deficit => Math.Max(0, (int)Math.Ceiling(MaxFuel) - (int)Math.Ceiling(Fuel));
}

/// <summary>Why a fire is or is not worth walking to. <see cref="Unavailable"/>
/// is zero, so a verdict nobody computed never reads as "go ahead".</summary>
internal enum FuelTargetStatus
{
    /// <summary>Not established. Refuse.</summary>
    Unavailable = 0,

    /// <summary>Owned here, refillable, inside the settlement, and it would
    /// accept at least one unit.</summary>
    Eligible = 1,

    /// <summary>Already as full as vanilla will let it get. Costs no wood: the
    /// check happens before any withdrawal.</summary>
    AlreadyFuelled = 2,

    /// <summary>This process does not own the fire's network object.</summary>
    NotOwnedHere = 3,

    /// <summary>The fire takes no fuel (<c>m_canRefill</c> is false).</summary>
    CannotRefill = 4,

    /// <summary>The fire never runs out and never accepts anything.</summary>
    InfiniteFuel = 5,

    /// <summary>Outside the marked settlement area.</summary>
    OutsideSettlement = 6,

    /// <summary>Somebody's ward covers it, or the check could not be answered.
    /// </summary>
    AccessDenied = 7,

    /// <summary>Its key belongs to a previous run of the world, so it names
    /// nothing now.</summary>
    StaleIdentity = 8,

    /// <summary>It burns something the designated depot does not stock. Not a
    /// fault: a Steward with a wood chest simply has nothing to offer a fire
    /// that wants something else.</summary>
    WrongFuel = 9,
}

/// <summary>Vanilla's own fuel arithmetic, reproduced exactly.
///
/// <b>Every number here is a decompiled fact, not a convention.</b> Getting one
/// of them subtly wrong is how a worker withdraws wood for a fire that then
/// refuses it — which is not a crash, is not visible in a log, and quietly
/// leaves items in the wrong place.</summary>
internal static class FuelMath
{
    /// <summary>The smallest fuel change that counts as one.
    ///
    /// Vanilla stores fuel as a <c>float</c> and moves it in steps of one, so
    /// anything at this scale is float noise rather than a mutation. It lives
    /// here so the adapter that measures a feed and the tests that assert on
    /// one use the same number — a tolerance defined twice is a tolerance that
    /// disagrees with itself.</summary>
    public const float Epsilon = 1e-3f;

    /// <summary>Would vanilla accept one more unit right now?
    ///
    /// The test is <c>Mathf.CeilToInt(fuel) &gt;= m_maxFuel</c> — a
    /// <b>ceiling</b>, not a plain comparison. At <c>m_maxFuel = 10</c> a fire
    /// at <c>9.5</c> is full and refuses, while one at <c>9.0</c> accepts and
    /// lands on <c>10.0</c>. Writing <c>fuel &lt; maxFuel</c> here would make
    /// the Steward fetch a log for a fire that will not take it.</summary>
    public static bool AcceptsOneUnit(float fuel, float maxFuel) =>
        Math.Ceiling((double)fuel) < maxFuel;

    /// <summary>What one accepted unit is actually worth, given the clamp in
    /// <c>RPC_AddFuel</c>: <c>Clamp(Clamp(fuel, 0, max) + 1, 0, max)</c>.
    ///
    /// Usually 1.0, and less at the top of the range — a fire at <c>9.4</c> of
    /// <c>10</c> gains <c>0.6</c>. The Steward never <i>predicts</i> with this;
    /// it measures. The value exists so a test can state the expected delta and
    /// so the evidence sentence can say what should have happened beside what
    /// did.</summary>
    public static float ExpectedGain(float fuel, float maxFuel)
    {
        if (!AcceptsOneUnit(fuel, maxFuel))
        {
            return 0f;
        }

        float from = Clamp(fuel, 0f, maxFuel);
        return Clamp(from + 1f, 0f, maxFuel) - from;
    }

    /// <summary>How many whole units it would take to fill this fire from here,
    /// stepping the way vanilla does rather than dividing.
    ///
    /// Dividing would be wrong at the top of the range: a fire at <c>9.4</c> of
    /// <c>10</c> has <c>0.6</c> missing, which is not "one unit" by division but
    /// is exactly one unit by vanilla's own loop. Bounded by
    /// <paramref name="ceiling"/>, because an unbounded count is a number this
    /// layer promised not to produce.</summary>
    public static int UnitsToFill(float fuel, float maxFuel, int ceiling)
    {
        if (ceiling < 1)
        {
            return 0;
        }

        int units = 0;
        float level = fuel;
        while (units < ceiling && AcceptsOneUnit(level, maxFuel))
        {
            level = Clamp(Clamp(level, 0f, maxFuel) + 1f, 0f, maxFuel);
            units++;
        }

        return units;
    }

    private static float Clamp(float value, float low, float high) =>
        value < low ? low : value > high ? high : value;

    /// <summary>A fuel level as a player would read it, for evidence lines.
    /// </summary>
    public static string Describe(float fuel, float maxFuel) =>
        fuel.ToString("0.##", CultureInfo.InvariantCulture) + "/" +
        maxFuel.ToString("0.##", CultureInfo.InvariantCulture);
}
