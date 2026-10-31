namespace TheConcernedCat.ConcernedSteward.Domain.Retirement;

/// <summary>Whether Sunniva's worker body may leave the world. Zero is the
/// unspecified value: a verdict nobody computed never removes anything.</summary>
internal enum StewardRetireVerdict
{
    /// <summary>Nobody decided. Never a grant.</summary>
    Unspecified = 0,

    /// <summary>She holds nothing and no round is open. The body may go.
    /// </summary>
    MayRetire = 1,

    /// <summary>Her pack holds something. Removing the body would destroy it
    /// with the body - nothing is dropped - so it is refused.</summary>
    RefusedCarrying = 2,

    /// <summary>Her pack could not be read: the body is not loaded, is inert,
    /// or its last change did not reach its own object. Unknown is not empty.
    /// </summary>
    RefusedUnreadable = 3,

    /// <summary>A maintenance round is open: she is walking, carrying, has a
    /// durable plan, has an unresolved step in her record, or is stopped
    /// waiting for a person. The round has to close first.</summary>
    RefusedRoundOpen = 4,

    /// <summary>The fuel ledger says something left the chest and was not
    /// accounted for. A person looks first (<c>cs_steward resolve</c>).
    /// </summary>
    RefusedUnresolvedLoss = 5,
}

/// <summary>What the retire verb is told, constructed at the call site so every
/// combination is one literal away in a test.</summary>
internal readonly struct StewardRetireFacts
{
    internal StewardRetireFacts(
        int itemsHeld,
        bool inventoryReadable,
        int fuelCarried,
        bool roundOpen,
        bool planOpen,
        int unresolvedSteps,
        bool unresolvedLoss)
    {
        ItemsHeld = itemsHeld;
        InventoryReadable = inventoryReadable;
        FuelCarried = fuelCarried;
        RoundOpen = roundOpen;
        PlanOpen = planOpen;
        UnresolvedSteps = unresolvedSteps;
        UnresolvedLoss = unresolvedLoss;
    }

    /// <summary>Items in the body's own inventory, as its own record loaded
    /// them. Meaningless when <see cref="InventoryReadable"/> is false.</summary>
    public int ItemsHeld { get; }

    public bool InventoryReadable { get; }

    /// <summary>What the fuel ledger says she is carrying. Asked separately from
    /// the pack, because the two are independent witnesses and either one
    /// saying "something" refuses.</summary>
    public int FuelCarried { get; }

    /// <summary>The upkeep loop is mid-round or stopped for attention.</summary>
    public bool RoundOpen { get; }

    /// <summary>The durable maintenance plan has a run that has not settled.
    /// </summary>
    public bool PlanOpen { get; }

    /// <summary>Steps written down as intended and never receipted.</summary>
    public int UnresolvedSteps { get; }

    public bool UnresolvedLoss { get; }
}

/// <summary>Whether Sunniva's own worker body may be removed from the world
/// (owner decision 2026-10-28, <c>docs/mods/concerned-steward/DECISIONS.md</c>
/// S1).
///
/// <b>Why removal is refused rather than emptied.</b> Her pack lives in her own
/// network object, so removing the body destroys whatever is in it - nothing is
/// dropped on the ground. Concerned Foreman's §5a and Concerned Teamster's
/// <c>WorkerRetirement</c> decided this shape already: a deliberate removal is
/// refused while the worker carries anything. Steward needs no forcing word,
/// because unlike Gunnar she always has a way to be emptied: every round ends
/// by putting the remainder back in the marked chest, and a stopped round is
/// resolved by <c>cs_steward resolve</c>. The owner's grant covers removal of an
/// <b>empty</b> body with <b>no round open</b>, and nothing wider.
///
/// <b>Every witness refuses on its own.</b> The pack, the fuel ledger, the loop,
/// the durable plan, the record's open steps and an unresolved loss are
/// different records of the same material. Any one of them saying "something"
/// is a refusal; there is no vote.</summary>
internal static class StewardRetirement
{
    /// <summary>Decides. Ordered so the sentence a player reads names the most
    /// concrete reason first: what she is holding before what she is doing.
    /// </summary>
    public static StewardRetireVerdict Decide(in StewardRetireFacts facts)
    {
        if (!facts.InventoryReadable)
        {
            return StewardRetireVerdict.RefusedUnreadable;
        }

        // A negative (corrupt) count is not "none": unknown is never empty.
        if (facts.ItemsHeld != 0 || facts.FuelCarried != 0)
        {
            return StewardRetireVerdict.RefusedCarrying;
        }

        if (facts.UnresolvedLoss)
        {
            return StewardRetireVerdict.RefusedUnresolvedLoss;
        }

        if (facts.RoundOpen || facts.PlanOpen || facts.UnresolvedSteps != 0)
        {
            return StewardRetireVerdict.RefusedRoundOpen;
        }

        return StewardRetireVerdict.MayRetire;
    }

    /// <summary>Whether the body may actually be removed. The caller asks this
    /// rather than comparing verdicts itself, so adding a verdict can never
    /// quietly become a grant.</summary>
    public static bool Allows(StewardRetireVerdict verdict) => verdict == StewardRetireVerdict.MayRetire;

    /// <summary>One sentence a player can act on.</summary>
    public static string Describe(StewardRetireVerdict verdict, int itemsHeld)
    {
        switch (verdict)
        {
            case StewardRetireVerdict.MayRetire:
                return "She is holding nothing and no round is open.";

            case StewardRetireVerdict.RefusedCarrying:
                return "Refused: she is carrying " +
                    (itemsHeld == 1 ? "1 thing" : itemsHeld > 1 ? itemsHeld + " things" : "fuel") +
                    ", and her body cannot leave without destroying it - nothing is dropped. Let her " +
                    "finish the round and put it back in the chest first.";

            case StewardRetireVerdict.RefusedUnreadable:
                return "Refused: what she is carrying could not be read (her body is not loaded, or " +
                    "its last change was not saved), and unknown is not empty.";

            case StewardRetireVerdict.RefusedRoundOpen:
                return "Refused: a maintenance round is still open. Let it finish, or turn tending off " +
                    "and let her put back what she holds, then try again.";

            case StewardRetireVerdict.RefusedUnresolvedLoss:
                return "Refused: some fuel she took is unaccounted for. Look, run \"cs_steward resolve\", " +
                    "then try again.";

            default:
                return "Refused for a reason nobody recorded; that is a bug.";
        }
    }
}
