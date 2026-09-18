using System;
using System.Collections.Generic;

namespace TheConcernedCat.Settlement.Housing;

/// <summary>Why a bed does or does not house somebody.
///
/// Every value is one of <b>vanilla's own</b> refusals, read from
/// <c>Bed.Interact</c> in the installed 1.0.14 build, not a rule invented here:
/// <c>CheckExposure</c> is the roof and the cover, <c>CheckFire</c> is the
/// warmth, and the owner is <c>ZDOVars.s_owner</c>. That matters because
/// CF-SET-009 (#286) asks for capacity <i>measured</i> from the finished
/// building rather than asserted — and measuring it against a reimplementation
/// of the game's rules would be asserting it with extra steps.</summary>
internal enum HousingRefusal
{
    /// <summary>It houses somebody.</summary>
    None = 0,

    /// <summary>No bed there any more.</summary>
    NoBed = 1,

    /// <summary>Not under a roof. Vanilla: <c>$msg_bedneedroof</c>.</summary>
    NoRoof = 2,

    /// <summary>Under a roof but less than 80 % covered. Vanilla:
    /// <c>$msg_bedtooexposed</c>.</summary>
    TooExposed = 3,

    /// <summary>No fire in reach. Vanilla: <c>$msg_bednofire</c>.</summary>
    NoFire = 4,

    /// <summary>Somebody already sleeps here. A bed a player claimed is theirs,
    /// and this never takes one.</summary>
    AlreadyClaimed = 5,

    /// <summary>Outside the settlement it would have to belong to.</summary>
    OutsideSettlement = 6,

    /// <summary>The measurement could not be taken — an unloaded chunk, a ward
    /// that would not answer. <b>Not the same as uninhabitable</b>, and it must
    /// never be reported as though it were: a house that could not be measured
    /// is a house nobody knows about.</summary>
    NotMeasured = 7,
}

/// <summary>What was actually observed at one bed. Every field is something an
/// adapter had to establish; none has a default that means "probably fine".
/// </summary>
internal readonly struct HousingFacts
{
    public HousingFacts(
        string bedKey,
        bool measured,
        bool insideSettlement,
        bool underRoof,
        float coverPercentage,
        bool warm,
        bool claimed)
    {
        BedKey = bedKey ?? string.Empty;
        Measured = measured;
        InsideSettlement = insideSettlement;
        UnderRoof = underRoof;
        CoverPercentage = coverPercentage;
        Warm = warm;
        Claimed = claimed;
    }

    /// <summary>Identity within one world load. Not a name.</summary>
    public string BedKey { get; }

    /// <summary>The adapter actually took the measurement. False means every
    /// other field is meaningless.</summary>
    public bool Measured { get; }

    public bool InsideSettlement { get; }

    /// <summary>`Cover.IsUnderRoof` at the bed's spawn point.</summary>
    public bool UnderRoof { get; }

    /// <summary>`Cover.GetCoverForPoint`'s percentage, 0..1.</summary>
    public float CoverPercentage { get; }

    /// <summary>`EffectArea.IsPointInsideArea(..., Heat)` at the bed.</summary>
    public bool Warm { get; }

    /// <summary>`ZDOVars.s_owner` is set to somebody.</summary>
    public bool Claimed { get; }

    /// <summary>A bed the adapter could not measure. Deliberately the only
    /// constructor that is easy to reach, so a half-filled fact is hard to make
    /// by accident.</summary>
    public static HousingFacts Unmeasured(string bedKey) =>
        new HousingFacts(bedKey, false, false, false, 0f, false, false);
}

/// <summary>Vanilla's own bed rules, applied to measured facts.</summary>
internal static class HousingRules
{
    /// <summary>The cover a bed needs. `Bed.CheckExposure`:
    /// <c>if (coverPercentage &lt; 0.8f) … $msg_bedtooexposed</c>.</summary>
    public const float RequiredCover = 0.8f;

    /// <summary>Why this bed does or does not house somebody.
    ///
    /// The order is vanilla's, so the reason a player is given is the first one
    /// the game itself would give — except that "already claimed" and "outside
    /// the settlement" come first, because they are ours and they are not about
    /// the building at all.</summary>
    public static HousingRefusal Judge(in HousingFacts facts)
    {
        if (!facts.Measured)
        {
            return HousingRefusal.NotMeasured;
        }

        if (!facts.InsideSettlement)
        {
            return HousingRefusal.OutsideSettlement;
        }

        if (facts.Claimed)
        {
            return HousingRefusal.AlreadyClaimed;
        }

        if (!facts.UnderRoof)
        {
            return HousingRefusal.NoRoof;
        }

        if (facts.CoverPercentage < RequiredCover)
        {
            return HousingRefusal.TooExposed;
        }

        if (!facts.Warm)
        {
            return HousingRefusal.NoFire;
        }

        return HousingRefusal.None;
    }

    /// <summary>One sentence a player can act on.</summary>
    public static string Describe(HousingRefusal refusal)
    {
        switch (refusal)
        {
            case HousingRefusal.None:
                return "somebody could sleep here";
            case HousingRefusal.NoBed:
                return "there is no bed here any more";
            case HousingRefusal.NoRoof:
                return "it needs a roof over it";
            case HousingRefusal.TooExposed:
                return "it is under a roof but too open to the weather";
            case HousingRefusal.NoFire:
                return "there is no fire near enough to it";
            case HousingRefusal.AlreadyClaimed:
                return "somebody already sleeps in it";
            case HousingRefusal.OutsideSettlement:
                return "it is outside the settlement";
            case HousingRefusal.NotMeasured:
                return "it could not be checked from here";
            default:
                return "a reason was recorded that this build does not know; that is a bug";
        }
    }
}

/// <summary>How many people a settlement can house, and why the rest of its beds
/// cannot.
///
/// <b>Counted, never asserted.</b> #286's rule is that capacity comes from the
/// real building and that a blueprint alone houses nobody, so this has no input
/// except beds that were actually measured. A settlement with a drawn cottage
/// and no bed in it has a capacity of zero, and says so.</summary>
internal readonly struct HousingCapacity
{
    private readonly IReadOnlyList<KeyValuePair<string, HousingRefusal>> _beds;

    private HousingCapacity(
        int habitable, int unmeasured, IReadOnlyList<KeyValuePair<string, HousingRefusal>> beds)
    {
        Habitable = habitable;
        Unmeasured = unmeasured;
        _beds = beds;
    }

    /// <summary>Beds somebody could be admitted to. This is the capacity.
    /// </summary>
    public int Habitable { get; }

    /// <summary>Beds that could not be checked. Kept apart from the refusals
    /// because "we could not look" and "it is not good enough" are different
    /// answers, and reporting the first as the second is how a player is told
    /// their house is uninhabitable when it is merely out of range.</summary>
    public int Unmeasured { get; }

    /// <summary>Every bed considered, with its verdict, in the order given.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, HousingRefusal>> Beds =>
        _beds ?? Array.Empty<KeyValuePair<string, HousingRefusal>>();

    public static HousingCapacity None => default;

    public static HousingCapacity Measure(IEnumerable<HousingFacts>? beds)
    {
        if (beds == null)
        {
            return None;
        }

        var verdicts = new List<KeyValuePair<string, HousingRefusal>>();
        int habitable = 0;
        int unmeasured = 0;

        foreach (HousingFacts facts in beds)
        {
            HousingRefusal refusal = HousingRules.Judge(facts);
            verdicts.Add(new KeyValuePair<string, HousingRefusal>(facts.BedKey, refusal));

            if (refusal == HousingRefusal.None)
            {
                habitable++;
            }
            else if (refusal == HousingRefusal.NotMeasured)
            {
                unmeasured++;
            }
        }

        return new HousingCapacity(habitable, unmeasured, verdicts);
    }

    /// <summary>What `cf_settle housing` prints.</summary>
    public string Describe()
    {
        if (Beds.Count == 0)
        {
            return "No beds in the settlement, so it houses nobody.";
        }

        var text = new System.Text.StringBuilder();
        text.Append("Housing: ").Append(Habitable).Append(Habitable == 1 ? " place" : " places");
        if (Unmeasured > 0)
        {
            text.Append(", and ").Append(Unmeasured)
                .Append(Unmeasured == 1 ? " bed that could not be checked" : " beds that could not be checked");
        }

        text.Append('.');
        foreach (KeyValuePair<string, HousingRefusal> bed in Beds)
        {
            text.AppendLine().Append("  ").Append(bed.Key).Append(": ")
                .Append(HousingRules.Describe(bed.Value));
        }

        return text.ToString();
    }
}
