using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>One line of the player's cargo filter for a chest-to-chest haul
/// (#381 item 4): an item named exactly, what one unit of it weighs and how many
/// share a slot - both read from the game - how many the player asked for, and
/// how many the source chest held when the haul was planned.</summary>
internal readonly struct HaulCargo
{
    public HaulCargo(string item, float unitKilograms, int stackSize, int? unitsRequested, int unitsInSource)
    {
        Item = item ?? string.Empty;
        UnitKilograms = unitKilograms;
        StackSize = stackSize;
        UnitsRequested = unitsRequested;
        UnitsInSource = unitsInSource > 0 ? unitsInSource : 0;
    }

    /// <summary>The item, by the prefab name the game uses.</summary>
    public string Item { get; }

    public float UnitKilograms { get; }

    public int StackSize { get; }

    /// <summary>What the player asked for; <c>null</c> for "all of it the
    /// source holds now".</summary>
    public int? UnitsRequested { get; }

    public int UnitsInSource { get; }

    /// <summary>What this line can move: what was asked for, never more than
    /// the source holds.</summary>
    public int UnitsToHaul
    {
        get
        {
            int wanted = UnitsRequested ?? UnitsInSource;
            return wanted < UnitsInSource ? (wanted > 0 ? wanted : 0) : UnitsInSource;
        }
    }

    /// <summary>What was asked for and the source does not hold.</summary>
    public int UnitsShort => UnitsRequested.HasValue && UnitsRequested.Value > UnitsInSource
        ? UnitsRequested.Value - UnitsInSource
        : 0;
}

/// <summary>How much one on-foot tour may carry: the weight left under the D17
/// carry limit and the inventory slots left free, both read from the game. A
/// default (zero) capacity carries nothing, so an unreadable game plans no
/// tour rather than an unlimited one.</summary>
internal readonly struct HaulCapacity
{
    public HaulCapacity(float freeKilograms, int freeSlots)
    {
        FreeKilograms = float.IsNaN(freeKilograms) || float.IsInfinity(freeKilograms) || freeKilograms < 0f
            ? 0f
            : freeKilograms;
        FreeSlots = freeSlots > 0 ? freeSlots : 0;
    }

    public float FreeKilograms { get; }

    public int FreeSlots { get; }
}

/// <summary>One item and a count within one tour.</summary>
internal readonly struct HaulLoad
{
    public HaulLoad(string item, int units)
    {
        Item = item ?? string.Empty;
        Units = units;
    }

    public string Item { get; }

    public int Units { get; }
}

/// <summary>One planned trip: what he takes at the source, in loading order.
/// </summary>
internal sealed class HaulTourPlan
{
    public HaulTourPlan(int index, IReadOnlyList<HaulLoad> loads, double kilograms, int slots)
    {
        Index = index;
        Loads = loads ?? Array.Empty<HaulLoad>();
        Kilograms = kilograms;
        Slots = slots;
        int units = 0;
        for (int at = 0; at < Loads.Count; at++)
        {
            units += Loads[at].Units;
        }

        Units = units;
    }

    /// <summary>Which trip, from zero.</summary>
    public int Index { get; }

    /// <summary>What is taken at the source, in this order, and unloaded at the
    /// destination in the same order.</summary>
    public IReadOnlyList<HaulLoad> Loads { get; }

    public double Kilograms { get; }

    public int Slots { get; }

    public int Units { get; }
}

internal enum HaulPlanRefusal
{
    None = 0,

    /// <summary><see cref="HaulRequestGate"/> refused the haul: no source or
    /// destination, the same container twice, a container not marked for its
    /// half, nothing matching the filter, or no room at all. Its own detail
    /// says which.</summary>
    NotAdmitted = 9,

    /// <summary>No item was named. He never hauls "everything".</summary>
    NoCargo = 1,

    /// <summary>More lines than <see cref="HaulManifestPlanner.MostCargoLines"/>.
    /// </summary>
    TooManyCargo = 2,

    /// <summary>A name that cannot be an item, or a count that is not one.
    /// </summary>
    CargoNotNamed = 3,

    /// <summary>The same item twice: two lines of one item would plan the same
    /// units twice.</summary>
    DuplicateCargo = 4,

    /// <summary>A weight or a stack size the game did not answer. Nothing
    /// unreadable is planned around.</summary>
    CargoUnreadable = 5,

    /// <summary>The source holds none of what was asked for.</summary>
    NothingInSource = 6,

    /// <summary>Some line does not fit even one unit in an empty tour.</summary>
    NoCarryRoom = 7,

    /// <summary>The haul would take more trips than one order may.</summary>
    TooManyTours = 8,
}

/// <summary>A chest-to-chest haul worked out before anything moves (#381 item
/// 4): what will be moved, in how many on-foot tours, what each tour takes and
/// in what order, and how many tours a cart of the given room would need.
///
/// <b>A plan, never a promise.</b> Every take and every unload is still
/// measured by its pinned port at the moment of the move; the manifest only
/// decides what is asked for. What actually arrives is what the haul reports.
/// </summary>
internal sealed class HaulManifest
{
    internal HaulManifest(
        HaulPlanRefusal refusal,
        string detail,
        IReadOnlyList<HaulCargo> cargo,
        IReadOnlyList<HaulTourPlan> tours,
        HaulCapacity onFoot,
        int? cartTours)
    {
        Refusal = refusal;
        Detail = detail ?? string.Empty;
        Cargo = cargo ?? Array.Empty<HaulCargo>();
        Tours = tours ?? Array.Empty<HaulTourPlan>();
        OnFoot = onFoot;
        CartTours = cartTours;
    }

    public HaulPlanRefusal Refusal { get; }

    /// <summary>Which line a refusal is about, when it is about one.</summary>
    public string Detail { get; }

    public bool IsPlanned => Refusal == HaulPlanRefusal.None && Tours.Count > 0;

    /// <summary>The cargo lines in loading order: <see cref="HaulRequestGate"/>'s,
    /// heaviest first and then by name.</summary>
    public IReadOnlyList<HaulCargo> Cargo { get; }

    public IReadOnlyList<HaulTourPlan> Tours { get; }

    public HaulCapacity OnFoot { get; }

    /// <summary>How many tours the same cargo would need in a cart with the room
    /// given, or <c>null</c> when no cart was considered. Informational: a
    /// manual haul runs on foot (see `DECISIONS.md`, the manual-haul lead
    /// default).</summary>
    public int? CartTours { get; }

    /// <summary>Every unit the plan asks for, across every tour.</summary>
    public int UnitsPlanned
    {
        get
        {
            int total = 0;
            for (int index = 0; index < Tours.Count; index++)
            {
                total += Tours[index].Units;
            }

            return total;
        }
    }

    /// <summary>What the plan asks for of one item, across every tour.</summary>
    public int PlannedOf(string item)
    {
        int total = 0;
        for (int index = 0; index < Tours.Count; index++)
        {
            IReadOnlyList<HaulLoad> loads = Tours[index].Loads;
            for (int at = 0; at < loads.Count; at++)
            {
                if (string.Equals(loads[at].Item, item, StringComparison.Ordinal))
                {
                    total += loads[at].Units;
                }
            }
        }

        return total;
    }

    public string Describe()
    {
        if (!IsPlanned)
        {
            return HaulManifestPlanner.Describe(Refusal, Detail);
        }

        var said = new StringBuilder();
        said.Append(UnitsPlanned.ToString(CultureInfo.InvariantCulture)).Append(" unit(s) in ")
            .Append(Tours.Count.ToString(CultureInfo.InvariantCulture)).Append(" tour(s) on foot (each up to ")
            .Append(OnFoot.FreeKilograms.ToString("0.#", CultureInfo.InvariantCulture)).Append(" kg and ")
            .Append(OnFoot.FreeSlots.ToString(CultureInfo.InvariantCulture)).Append(" slot(s)); loading order ");
        for (int index = 0; index < Cargo.Count; index++)
        {
            said.Append(index == 0 ? string.Empty : ", ").Append(Cargo[index].Item).Append(' ')
                .Append(PlannedOf(Cargo[index].Item).ToString(CultureInfo.InvariantCulture));
            if (Cargo[index].UnitsShort > 0)
            {
                said.Append(" (the source holds ")
                    .Append(Cargo[index].UnitsShort.ToString(CultureInfo.InvariantCulture))
                    .Append(" fewer than you asked for)");
            }
        }

        said.Append("; first tour: ");
        IReadOnlyList<HaulLoad> first = Tours[0].Loads;
        for (int at = 0; at < first.Count; at++)
        {
            said.Append(at == 0 ? string.Empty : ", ").Append(first[at].Units.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(first[at].Item);
        }

        if (CartTours.HasValue)
        {
            said.Append("; a cart of that room would need ")
                .Append(CartTours.Value.ToString(CultureInfo.InvariantCulture)).Append(" tour(s)");
        }

        return said.Append('.').ToString();
    }
}

/// <summary>Works out a chest-to-chest haul (#381 item 4): on-foot capacity,
/// the number of tours and the loading order, and the cart figure for
/// comparison. Game-free: every number it uses was read from the game by the
/// adapter, and it moves nothing.
///
/// <b>Who decides what.</b> Whether the haul may start at all, and its loading
/// order (heaviest first, then by name), are <see cref="HaulRequestGate"/>'s;
/// this only splits the admitted lines into tours. The shared runtime's
/// <c>TourPartitioner</c> is not used: it is internal to ConcernedNPC, and it
/// answers a different question - indivisible targets at places, bin-packed by
/// distance - where a chest-to-chest haul has one place and divisible cargo.
///
/// <b>How a tour is filled.</b> Lines are taken in loading order. Each tour takes as much of the first line
/// still owed as fits under both limits - the weight left under the carry limit
/// and the free slots, a line of <c>n</c> units taking <c>ceil(n / stack)</c>
/// slots because he starts the haul holding none of the cargo - and then as
/// much of the next as still fits, and so on. Because units are divisible,
/// filling in order wastes at most one partly filled slot per line per tour, so
/// the tour count is the least the two limits allow to within that slot.
///
/// <b>Bounded.</b> At most <see cref="MostCargoLines"/> lines and
/// <see cref="MostTours"/> tours; a larger haul is refused and says so rather
/// than walking for ever. A line that does not fit even one unit in an empty
/// tour refuses the whole haul: planning around it would leave it behind
/// silently.</summary>
internal static class HaulManifestPlanner
{
    /// <summary>The most items one haul may name.</summary>
    public const int MostCargoLines = 4;

    /// <summary>The most tours one haul may plan.</summary>
    public const int MostTours = 32;

    /// <summary>Plans the haul <paramref name="admitted"/> describes. The
    /// request is <see cref="HaulRequestGate"/>'s - the one answer to whether a
    /// manual haul may start, which also decides the loading order (heaviest
    /// first, then by name) - and <paramref name="cargo"/> carries what the gate
    /// does not: each item's stack size and what the player asked for.
    /// </summary>
    public static HaulManifest Plan(
        HaulRequest? admitted, IReadOnlyList<HaulCargo>? cargo, HaulCapacity onFoot, CartCapacity cart)
    {
        IReadOnlyList<HaulCargo> given = cargo ?? Array.Empty<HaulCargo>();
        HaulPlanRefusal refusal = CheckLines(given, out string detail);
        if (refusal != HaulPlanRefusal.None)
        {
            return Refused(refusal, detail, given, onFoot);
        }

        if (admitted == null || admitted.IsRefused)
        {
            return Refused(HaulPlanRefusal.NotAdmitted, admitted == null ? string.Empty : admitted.Detail, given, onFoot);
        }

        List<HaulCargo>? lines = InLoadingOrder(admitted, given);
        if (lines == null)
        {
            return Refused(HaulPlanRefusal.NotAdmitted, "the admitted haul and the cargo named do not agree", given, onFoot);
        }

        int owed = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            owed += lines[index].UnitsToHaul;
        }

        if (owed == 0)
        {
            return Refused(HaulPlanRefusal.NothingInSource, string.Empty, lines, onFoot);
        }

        for (int index = 0; index < lines.Count; index++)
        {
            HaulCargo line = lines[index];
            if (line.UnitsToHaul > 0 && Fits(line, onFoot.FreeKilograms, onFoot.FreeSlots) < 1)
            {
                return Refused(HaulPlanRefusal.NoCarryRoom, line.Item, lines, onFoot);
            }
        }

        List<HaulTourPlan>? tours = Fill(lines, onFoot.FreeKilograms, onFoot.FreeSlots, MostTours);
        if (tours == null)
        {
            return Refused(HaulPlanRefusal.TooManyTours, string.Empty, lines, onFoot);
        }

        int? cartTours = null;
        if (cart.Assigned && cart.FreeSlots > 0)
        {
            // A cart is limited by its slots, not a weight (CartCapacity), so its
            // tours are filled by slots alone. Never more than the on-foot count.
            List<HaulTourPlan>? byCart = Fill(lines, float.PositiveInfinity, cart.FreeSlots, MostTours);
            cartTours = byCart == null ? (int?)null : byCart.Count;
        }

        return new HaulManifest(HaulPlanRefusal.None, string.Empty, lines, tours, onFoot, cartTours);
    }

    /// <summary>The cargo in the gate's loading order. Every admitted line must
    /// be named cargo moving exactly what the cargo says it would; a line the
    /// gate dropped (nothing of it in the source) is dropped here too.</summary>
    private static List<HaulCargo>? InLoadingOrder(HaulRequest admitted, IReadOnlyList<HaulCargo> cargo)
    {
        var ordered = new List<HaulCargo>(admitted.Wanted.Count);
        for (int index = 0; index < admitted.Wanted.Count; index++)
        {
            ManifestLine wanted = admitted.Wanted[index];
            int found = -1;
            for (int at = 0; at < cargo.Count; at++)
            {
                if (string.Equals(cargo[at].Item, wanted.ItemPrefab, StringComparison.Ordinal))
                {
                    found = at;
                    break;
                }
            }

            if (found < 0 || cargo[found].UnitsToHaul != wanted.Units)
            {
                return null;
            }

            ordered.Add(cargo[found]);
        }

        return ordered;
    }

    /// <summary>The lines a manual haul offers <see cref="HaulRequestGate"/>:
    /// what each would move, at what weight.</summary>
    public static IReadOnlyList<ManifestLine> Offered(IReadOnlyList<HaulCargo>? cargo)
    {
        var lines = new List<ManifestLine>();
        if (cargo == null)
        {
            return lines;
        }

        for (int index = 0; index < cargo.Count; index++)
        {
            lines.Add(new ManifestLine(cargo[index].Item, cargo[index].UnitsToHaul, cargo[index].UnitKilograms));
        }

        return lines;
    }

    /// <summary>How many units of <paramref name="line"/> fit in the room
    /// given. A weight the game did not answer fits nothing.</summary>
    internal static int Fits(HaulCargo line, double freeKilograms, int freeSlots)
    {
        if (freeSlots < 1 || line.StackSize < 1 || !(line.UnitKilograms > 0f) || float.IsInfinity(line.UnitKilograms))
        {
            return 0;
        }

        long bySlots = (long)freeSlots * line.StackSize;
        double byWeight = double.IsPositiveInfinity(freeKilograms)
            ? double.PositiveInfinity
            : Math.Floor(freeKilograms / line.UnitKilograms);
        double fits = byWeight < bySlots ? byWeight : bySlots;
        return fits <= 0d ? 0 : fits >= int.MaxValue ? int.MaxValue : (int)fits;
    }

    private static List<HaulTourPlan>? Fill(IReadOnlyList<HaulCargo> lines, double tourKilograms, int tourSlots, int mostTours)
    {
        var owed = new int[lines.Count];
        int remaining = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            owed[index] = lines[index].UnitsToHaul;
            remaining += owed[index];
        }

        var tours = new List<HaulTourPlan>();
        while (remaining > 0)
        {
            if (tours.Count >= mostTours)
            {
                return null;
            }

            double kilograms = tourKilograms;
            int slots = tourSlots;
            double used = 0d;
            int usedSlots = 0;
            var loads = new List<HaulLoad>();
            for (int index = 0; index < lines.Count && slots > 0; index++)
            {
                if (owed[index] < 1)
                {
                    continue;
                }

                HaulCargo line = lines[index];
                int fits = Fits(line, kilograms, slots);
                int take = fits < owed[index] ? fits : owed[index];
                if (take < 1)
                {
                    continue;
                }

                int takesSlots = (take + line.StackSize - 1) / line.StackSize;
                loads.Add(new HaulLoad(line.Item, take));
                owed[index] -= take;
                remaining -= take;
                slots -= takesSlots;
                usedSlots += takesSlots;
                double weight = (double)take * line.UnitKilograms;
                used += weight;
                kilograms -= weight;
            }

            if (loads.Count == 0)
            {
                // Nothing fits in a whole empty tour: never a loop that plans
                // nothing for ever. Plan() refuses this before it gets here.
                return null;
            }

            tours.Add(new HaulTourPlan(tours.Count, loads, used, usedSlots));
        }

        return tours;
    }

    private static HaulPlanRefusal CheckLines(IReadOnlyList<HaulCargo> lines, out string detail)
    {
        detail = string.Empty;
        if (lines.Count == 0)
        {
            return HaulPlanRefusal.NoCargo;
        }

        if (lines.Count > MostCargoLines)
        {
            return HaulPlanRefusal.TooManyCargo;
        }

        for (int index = 0; index < lines.Count; index++)
        {
            HaulCargo line = lines[index];
            detail = line.Item;
            if (!TakeOrderGate.IsCargoName(line.Item) || (line.UnitsRequested.HasValue && line.UnitsRequested.Value < 1))
            {
                return HaulPlanRefusal.CargoNotNamed;
            }

            for (int earlier = 0; earlier < index; earlier++)
            {
                if (string.Equals(lines[earlier].Item, line.Item, StringComparison.Ordinal))
                {
                    return HaulPlanRefusal.DuplicateCargo;
                }
            }

            if (!(line.UnitKilograms > 0f) || float.IsInfinity(line.UnitKilograms) || line.StackSize < 1)
            {
                return HaulPlanRefusal.CargoUnreadable;
            }
        }

        detail = string.Empty;
        return HaulPlanRefusal.None;
    }

    private static HaulManifest Refused(
        HaulPlanRefusal refusal, string detail, IReadOnlyList<HaulCargo> lines, HaulCapacity onFoot) =>
        new HaulManifest(refusal, detail, lines, Array.Empty<HaulTourPlan>(), onFoot, null);

    public static string Describe(HaulPlanRefusal refusal, string detail = "")
    {
        string which = string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")";
        switch (refusal)
        {
            case HaulPlanRefusal.None:
                return "The haul can be planned.";
            case HaulPlanRefusal.NoCargo:
                return "Name what he should haul, exactly as the game names it (for example ct_collect haul Stone " +
                    "or ct_collect haul Stone:40 Wood:20). He never hauls everything.";
            case HaulPlanRefusal.TooManyCargo:
                return "One haul names at most " + MostCargoLines.ToString(CultureInfo.InvariantCulture) +
                    " items. Nothing was planned.";
            case HaulPlanRefusal.CargoNotNamed:
                return "Each item is a name the game uses, optionally with :count as a whole number above zero" +
                    which + ". Nothing was planned.";
            case HaulPlanRefusal.DuplicateCargo:
                return "The same item is named twice" + which + ". Name it once, with the count you want.";
            case HaulPlanRefusal.CargoUnreadable:
                return "The game has no item by that name, or did not say what it weighs or how many share a slot" +
                    which + ". Nothing was planned.";
            case HaulPlanRefusal.NothingInSource:
                return "The source chest holds none of what you asked for. Nothing was planned.";
            case HaulPlanRefusal.NoCarryRoom:
                return "He cannot carry even one" + which + " - he is carrying too much already, or has no free " +
                    "slot. Nothing was planned.";
            case HaulPlanRefusal.NotAdmitted:
                return "The haul cannot start" + (string.IsNullOrEmpty(detail) ? "." : ": " + detail + ".");
            case HaulPlanRefusal.TooManyTours:
                return "That haul would take more than " + MostTours.ToString(CultureInfo.InvariantCulture) +
                    " tours. Ask for fewer (for example Stone:200). Nothing was planned.";
            default:
                return "The haul could not be planned.";
        }
    }
}

/// <summary>The player's cargo filter, read from the command line: one to
/// <see cref="HaulManifestPlanner.MostCargoLines"/> exact item names, each
/// optionally followed by <c>:count</c>. No wildcard, no "all", no list
/// syntax beyond that.</summary>
internal static class HaulCargoFilter
{
    public static HaulPlanRefusal TryParse(
        IReadOnlyList<string>? words, int from, out List<KeyValuePair<string, int?>> lines, out string detail)
    {
        lines = new List<KeyValuePair<string, int?>>();
        detail = string.Empty;
        if (words == null || from >= words.Count)
        {
            return HaulPlanRefusal.NoCargo;
        }

        if (words.Count - from > HaulManifestPlanner.MostCargoLines)
        {
            return HaulPlanRefusal.TooManyCargo;
        }

        for (int index = from; index < words.Count; index++)
        {
            string word = words[index] ?? string.Empty;
            detail = word;
            int colon = word.IndexOf(':');
            string item = colon < 0 ? word : word.Substring(0, colon);
            int? count = null;
            if (colon >= 0)
            {
                string digits = word.Substring(colon + 1);
                if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
                {
                    return HaulPlanRefusal.CargoNotNamed;
                }

                count = parsed;
            }

            if (!TakeOrderGate.IsCargoName(item))
            {
                return HaulPlanRefusal.CargoNotNamed;
            }

            for (int earlier = 0; earlier < lines.Count; earlier++)
            {
                if (string.Equals(lines[earlier].Key, item, StringComparison.Ordinal))
                {
                    return HaulPlanRefusal.DuplicateCargo;
                }
            }

            lines.Add(new KeyValuePair<string, int?>(item, count));
        }

        detail = string.Empty;
        return HaulPlanRefusal.None;
    }
}
