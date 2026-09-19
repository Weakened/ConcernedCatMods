using System;
using System.Collections.Generic;
using System.Text;

namespace TheConcernedCat.Settlement.Housing;

/// <summary>Who, if anybody, a bed already belongs to.
///
/// Three-way rather than two, because <c>Bed.Interact</c> in the installed
/// 1.0.14 build branches three ways and the middle one is easy to miss:
///
/// <list type="bullet">
/// <item><c>owner == 0</c> — unclaimed; vanilla checks exposure and claims
/// it;</item>
/// <item><c>IsMine() &amp;&amp; IsCurrent()</c> — you sleep here;</item>
/// <item><c>IsMine() &amp;&amp; !IsCurrent()</c> — <b>yours, but not where you
/// sleep</b>; vanilla checks exposure and simply moves your spawn point back to
/// it;</item>
/// <item>anything else — somebody else's, and vanilla refuses with no message at
/// all.</item>
/// </list>
///
/// The middle case matters because <b>nothing in vanilla ever clears
/// <c>s_owner</c></b> — <c>Bed.RPC_SetOwner</c> only ever sets it, and claiming a
/// second bed calls <c>SetCustomSpawnPoint</c> without touching the first. So a
/// player who has slept in four beds over a playthrough owns four beds forever.
/// Reading ownership as a plain "claimed or not" would report every one of them
/// as somebody else's bed and the settlement's capacity as zero, permanently,
/// with no way for the player to undo it short of demolishing them.</summary>
internal enum BedClaim
{
    /// <summary><c>s_owner == 0</c>. Nobody has ever slept here.</summary>
    Unclaimed = 0,

    /// <summary>Yours, and your current spawn point. You sleep here.</summary>
    YoursAndCurrent = 1,

    /// <summary>Yours, but not your spawn point any more. Vanilla lets you take
    /// it straight back, so it is not a bed in use.</summary>
    YoursButNotCurrent = 2,

    /// <summary>Owned by a player id that is not yours.</summary>
    SomebodyElses = 3,
}

/// <summary>Why a bed does or does not house somebody.
///
/// <b>Which rules are vanilla's, and which are ours.</b> The distinction is
/// worth stating exactly, because the value of CF-SET-009 (#286) rests on
/// capacity being <i>measured</i> rather than asserted, and a rule invented here
/// and presented as the game's would be an assertion wearing a measurement's
/// clothes:
///
/// <list type="bullet">
/// <item><see cref="NoRoof"/> and <see cref="TooExposed"/> are
/// <c>Bed.CheckExposure</c> (<c>$msg_bedneedroof</c>,
/// <c>$msg_bedtooexposed</c>), which vanilla applies on <b>every</b> path —
/// claiming a bed and sleeping in one;</item>
/// <item><see cref="NoFire"/> is <c>Bed.CheckFire</c>
/// (<c>$msg_bednofire</c>), which vanilla applies on the <b>sleep</b> path only.
/// It is included because a resident is expected to sleep, not merely to hold a
/// spawn point;</item>
/// <item><see cref="AlreadyClaimed"/> and <see cref="YourOwnBed"/> come from
/// <c>ZDOVars.s_owner</c>, which is vanilla's field, but the decision to treat an
/// owned bed as unavailable is <b>ours</b>: vanilla shows no message at all for
/// somebody else's bed;</item>
/// <item><see cref="OutsideSettlement"/> and <see cref="NotMeasured"/> are
/// entirely ours.</item>
/// </list>
///
/// Three of vanilla's own bed refusals are deliberately absent, because all three
/// are properties of the player at one moment rather than of the building:
/// <c>CheckWet</c> (the player's Wet status), <c>CheckEnemies</c>
/// (<c>Player.IsSensed</c>) and <c>EnvMan.CanSleep()</c> (it is daytime). A
/// capacity that fell to zero at noon would not be a capacity.</summary>
internal enum HousingRefusal
{
    /// <summary>It houses somebody.</summary>
    None = 0,

    /// <summary>Not under a roof. Vanilla: <c>$msg_bedneedroof</c>.</summary>
    NoRoof = 2,

    /// <summary>Under a roof but less than 80 % covered. Vanilla:
    /// <c>$msg_bedtooexposed</c>.</summary>
    TooExposed = 3,

    /// <summary>No fire in reach. Vanilla: <c>$msg_bednofire</c>.</summary>
    NoFire = 4,

    /// <summary>Somebody else already sleeps here. A bed another player claimed
    /// is theirs, and this never takes one.</summary>
    AlreadyClaimed = 5,

    /// <summary>Outside the settlement it would have to belong to.
    ///
    /// Foreman's own adapter filters by the designation's <c>Contains</c> while
    /// collecting, so in that product this verdict does not arise — a
    /// neighbour's bed is noise in a readout, not advice, and counting it would
    /// let their longhouse crowd out the player's own beds. The rule stays
    /// because it belongs to the rules, and a caller that hands over beds it has
    /// not filtered is owed an answer rather than a wrong one.</summary>
    OutsideSettlement = 6,

    /// <summary>The measurement could not be taken. <b>Not the same as
    /// uninhabitable</b>, and it must never be reported as though it were: a
    /// house that could not be measured is a house nobody knows about.</summary>
    NotMeasured = 7,

    /// <summary>Your own bed, and your current spawn point. It houses you, which
    /// is a different fact from a stranger having taken it — the settlement is
    /// working, and this is the one bed a player never wants advice about.
    /// </summary>
    YourOwnBed = 8,
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
        BedClaim claim)
    {
        BedKey = bedKey ?? string.Empty;
        Measured = measured;
        InsideSettlement = insideSettlement;
        UnderRoof = underRoof;
        CoverPercentage = coverPercentage;
        Warm = warm;
        Claim = claim;
    }

    /// <summary>Where the bed is, in words a player can walk to. Not a name and
    /// not an id: a bed's uid is reassigned on every world load, so a number
    /// read out here would name a different object after a reload.</summary>
    public string BedKey { get; }

    /// <summary>The adapter actually took the measurement. False means every
    /// other field is meaningless.</summary>
    public bool Measured { get; }

    public bool InsideSettlement { get; }

    /// <summary>`Cover.GetCoverForPoint`'s roof flag at the bed's spawn point.
    /// </summary>
    public bool UnderRoof { get; }

    /// <summary>`Cover.GetCoverForPoint`'s percentage, 0..1.</summary>
    public float CoverPercentage { get; }

    /// <summary>`EffectArea.IsPointInsideArea(..., Heat)` at the bed.</summary>
    public bool Warm { get; }

    /// <summary>Whose bed it is, three ways.</summary>
    public BedClaim Claim { get; }

    /// <summary>A bed the adapter could not measure. Deliberately the only
    /// constructor that is easy to reach, so a half-filled fact is hard to make
    /// by accident.</summary>
    public static HousingFacts Unmeasured(string bedKey) =>
        new HousingFacts(bedKey, false, false, false, 0f, false, BedClaim.Unclaimed);
}

/// <summary>Vanilla's own bed rules, applied to measured facts.</summary>
internal static class HousingRules
{
    /// <summary>The cover a bed needs. `Bed.CheckExposure`:
    /// <c>if (coverPercentage &lt; 0.8f) … $msg_bedtooexposed</c>.</summary>
    public const float RequiredCover = 0.8f;

    /// <summary>Why this bed does or does not house somebody.
    ///
    /// Ours are asked first, because they are not about the building and telling
    /// a player to roof somebody else's bed would be advice they must not act
    /// on. Vanilla's own order follows.</summary>
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

        if (facts.Claim == BedClaim.SomebodyElses)
        {
            return HousingRefusal.AlreadyClaimed;
        }

        if (facts.Claim == BedClaim.YoursAndCurrent)
        {
            return HousingRefusal.YourOwnBed;
        }

        // BedClaim.YoursButNotCurrent deliberately falls through to the building
        // checks. Vanilla itself hands the bed straight back to its owner
        // (`Bed.Interact`, the `IsMine() && !IsCurrent()` branch, which checks
        // exposure and re-sets the spawn point), so a stale claim of your own is
        // a bed that is free — and since nothing in vanilla ever clears
        // `s_owner`, treating it as taken would retire every bed a player has
        // ever slept in.
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
}

/// <summary>How many people a settlement can house, and why the rest of its beds
/// cannot.
///
/// <b>Counted, never asserted.</b> #286's rule is that capacity comes from the
/// real building and that a blueprint alone houses nobody, so this has no input
/// except beds that were actually measured. A settlement with a drawn cottage
/// and no bed in it has a capacity of zero, and says so.
///
/// <b>"Nobody looked" is not "nothing is there."</b> A survey that never ran, a
/// survey that found no beds, a survey cut short at its own budget, and a survey
/// over ground that is only partly loaded are four different answers. Collapsing
/// any of them into the affirmative sentence "it houses nobody" is exactly the
/// confusion <see cref="HousingRefusal.NotMeasured"/> exists to prevent, and a
/// player told their settlement houses nobody will go and tear down a house that
/// was fine.</summary>
internal readonly struct HousingCapacity
{
    private readonly IReadOnlyList<KeyValuePair<string, HousingRefusal>>? _beds;

    /// <summary>Counted once, here, over the very list this value stores.
    ///
    /// They were computed properties, which read like fields at every call site
    /// and each walked the whole list — so one readout walked it eight times,
    /// and the first per-frame caller to ask "is there a free bed?" would have
    /// paid for a survey with nothing at the call site to warn it. Counting in
    /// the only constructor keeps the invariant that made them properties (a
    /// count cannot disagree with <see cref="Beds"/>, because nothing can build
    /// one from a different list) and costs a single pass.</summary>
    private HousingCapacity(
        bool surveyed,
        bool truncated,
        bool groundIncomplete,
        List<KeyValuePair<string, HousingRefusal>>? beds)
    {
        Surveyed = surveyed;
        Truncated = truncated;
        GroundIncomplete = groundIncomplete;
        _beds = beds;

        int habitable = 0;
        int occupied = 0;
        int unmeasured = 0;
        if (beds != null)
        {
            for (int i = 0; i < beds.Count; i++)
            {
                switch (beds[i].Value)
                {
                    case HousingRefusal.None:
                        habitable++;
                        break;
                    case HousingRefusal.YourOwnBed:
                    case HousingRefusal.AlreadyClaimed:
                        occupied++;
                        break;
                    case HousingRefusal.NotMeasured:
                        unmeasured++;
                        break;
                }
            }
        }

        Habitable = habitable;
        Occupied = occupied;
        Unmeasured = unmeasured;
    }

    /// <summary>A survey actually ran. False is <see cref="NotSurveyed"/>, and
    /// every count below is then meaningless rather than zero.</summary>
    public bool Surveyed { get; }

    /// <summary>The survey hit its own budget and stopped early, so the counts
    /// are a floor rather than a total.</summary>
    public bool Truncated { get; }

    /// <summary>Part of the settlement's ground was not loaded, so beds there
    /// were not merely unmeasured — they were never seen at all. This is the
    /// only way that fact can reach a player, because an object in an unloaded
    /// zone does not exist to be counted.</summary>
    public bool GroundIncomplete { get; }

    /// <summary>Every bed considered, with its verdict, in the order given.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, HousingRefusal>> Beds =>
        _beds ?? Array.Empty<KeyValuePair<string, HousingRefusal>>();

    /// <summary>Beds somebody new could be admitted to. This is the spare
    /// capacity, not the total — see <see cref="Occupied"/>.
    ///
    /// Counted in the constructor from the list below, so it cannot disagree
    /// with <see cref="Beds"/> and reading it is free.</summary>
    public int Habitable { get; }

    /// <summary>Beds somebody already sleeps in, yours included. Reported beside
    /// <see cref="Habitable"/> rather than folded into it, because "no room"
    /// and "no housing" look identical in a single number and only one of them
    /// is a reason to build.</summary>
    public int Occupied { get; }

    /// <summary>Beds that could not be checked. Kept apart from the refusals
    /// because "we could not look" and "it is not good enough" are different
    /// answers, and reporting the first as the second is how a player is told
    /// their house is uninhabitable when it is merely unreadable.</summary>
    public int Unmeasured { get; }

    /// <summary>Nobody looked. The answer when there is no settlement to
    /// measure, or when the measurement itself could not be taken.</summary>
    public static HousingCapacity NotSurveyed => default;

    /// <param name="truncated">The surveyor stopped at its own budget with beds
    /// left unexamined.</param>
    /// <param name="groundIncomplete">Part of the settlement was not loaded, so
    /// beds there could not appear in <paramref name="beds"/> at all.</param>
    public static HousingCapacity Measure(
        IEnumerable<HousingFacts>? beds,
        bool truncated = false,
        bool groundIncomplete = false)
    {
        if (beds == null)
        {
            return NotSurveyed;
        }

        var verdicts = beds is ICollection<HousingFacts> known
            ? new List<KeyValuePair<string, HousingRefusal>>(known.Count)
            : new List<KeyValuePair<string, HousingRefusal>>();
        foreach (HousingFacts facts in beds)
        {
            verdicts.Add(new KeyValuePair<string, HousingRefusal>(
                facts.BedKey, HousingRules.Judge(facts)));
        }

        return new HousingCapacity(true, truncated, groundIncomplete, verdicts);
    }

    /// <summary>What `cf_settle housing` prints.</summary>
    public string Describe() => HousingSentences.For(this);
}

/// <summary>The player-facing wording, kept off the rules and off the value the
/// way <c>CollectionSentences</c> and <c>CooperationSentences</c> are: the
/// readout shows the sentence, never the enum name, and a copy change does not
/// touch the file the verdict tests pin.</summary>
internal static class HousingSentences
{
    public static string For(HousingRefusal refusal)
    {
        switch (refusal)
        {
            case HousingRefusal.None:
                return "somebody could sleep here";
            case HousingRefusal.NoRoof:
                return "it needs a roof over it";
            case HousingRefusal.TooExposed:
                return "it is under a roof but too open to the weather";
            case HousingRefusal.NoFire:
                return "there is no fire near enough to it";
            case HousingRefusal.AlreadyClaimed:
                return "somebody else already sleeps in it";
            case HousingRefusal.YourOwnBed:
                return "you sleep here";
            case HousingRefusal.OutsideSettlement:
                return "it is outside the settlement";
            case HousingRefusal.NotMeasured:
                return "it could not be checked";
            default:
                return "a reason was recorded that this build does not know; that is a bug";
        }
    }

    public static string For(HousingCapacity capacity)
    {
        if (!capacity.Surveyed)
        {
            return "The settlement's housing could not be checked, so nothing is known about it. " +
                "That is not the same as having nowhere to live.";
        }

        // A per-bed line is a newline, two spaces, the key ("the bed at
        // -1234, 5678" is ~24), ": ", and a sentence of up to 46. The header
        // reaches ~68, and the two caveats add ~240 between them. The old
        // 160 + 56n was under all three, so it grew chunks anyway.
        var text = new StringBuilder(320 + (capacity.Beds.Count * 80));

        if (capacity.Beds.Count == 0)
        {
            text.Append(capacity.GroundIncomplete
                ? "No beds were found"
                : "No beds in the settlement, so it houses nobody.");
        }
        else
        {
            text.Append("Housing: ").Append(capacity.Habitable)
                .Append(capacity.Habitable == 1 ? " free place" : " free places");

            if (capacity.Occupied > 0)
            {
                text.Append(", ").Append(capacity.Occupied).Append(" lived in");
            }

            if (capacity.Unmeasured > 0)
            {
                text.Append(", ").Append(capacity.Unmeasured)
                    .Append(" that could not be checked");
            }

            text.Append('.');
        }

        AppendCaveats(text, capacity);

        IReadOnlyList<KeyValuePair<string, HousingRefusal>> beds = capacity.Beds;
        for (int i = 0; i < beds.Count; i++)
        {
            text.AppendLine().Append("  ").Append(beds[i].Key).Append(": ")
                .Append(For(beds[i].Value));
        }

        return text.ToString();
    }

    /// <summary>Everything that makes the count a floor rather than a total.
    /// Both caveats are said in the readout itself and not only in the log,
    /// because a number a player cannot see is qualified is a number they will
    /// act on.</summary>
    private static void AppendCaveats(StringBuilder text, HousingCapacity capacity)
    {
        if (capacity.GroundIncomplete)
        {
            text.Append(capacity.Beds.Count == 0 ? ", but part" : " Part")
                .Append(" of the settlement's ground is not loaded, and beds there cannot be seen ")
                .Append("at all — stand in the settlement and ask again.");
        }

        if (capacity.Truncated)
        {
            text.Append(" There are more beds here than one check will look at, so this is at ")
                .Append("least, not exactly, what the settlement holds.");
        }
    }
}
