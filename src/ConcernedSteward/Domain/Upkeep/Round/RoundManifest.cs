using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>What a player has allowed one container to be used for.
///
/// Flags, and <see cref="None"/> is zero: a container nobody has spoken about is
/// closed, and a defaulted value is a refusal rather than a permission. The
/// vocabulary is deliberately the one Concerned NPC's container leaf settled on
/// — off, take, deposit, both — so that when the Steward adopts the shared
/// permission book this maps across without a player re-marking anything.
/// </summary>
[Flags]
internal enum SupplyAccess
{
    /// <summary>Not approved for anything. The default.</summary>
    None = 0,

    /// <summary>She may take fuel out of it.</summary>
    Take = 1,

    /// <summary>She may put surplus fuel back into it.</summary>
    Deposit = 2,

    /// <summary>Both, which is what a single designated depot means today.
    /// </summary>
    Both = Take | Deposit,
}

/// <summary>How much of one item one approved container was observed to hold.
///
/// <b>Observed, and that word is doing work.</b> These are counts read at the
/// moment the round was planned, not a promise about what will be there when she
/// opens the chest. Nothing is withdrawn on the strength of them: the plan says
/// which chest and how much, and the measured transfer is what actually moves an
/// item and what records that it may or may not have happened.</summary>
internal readonly struct SupplyLine
{
    internal SupplyLine(string? item, int units)
    {
        Item = item ?? string.Empty;
        Units = units;
    }

    public string Item { get; }

    public int Units { get; }

    public bool IsValid => Item.Length != 0 && Units > 0;

    public override string ToString() => Units + " " + Item;
}

/// <summary>One approved container and what it was seen to hold.</summary>
internal readonly struct SupplySighting
{
    private readonly SupplyLine[]? _lines;

    internal SupplySighting(
        string? key, string? describe, SitePoint position, SupplyAccess access,
        IEnumerable<SupplyLine>? lines)
    {
        Key = key ?? string.Empty;
        Describe = string.IsNullOrEmpty(describe) ? "that container" : describe!;
        Position = position;
        Access = access;

        if (lines == null)
        {
            _lines = null;
            return;
        }

        var kept = new List<SupplyLine>();
        foreach (SupplyLine line in lines)
        {
            if (line.IsValid)
            {
                kept.Add(line);
            }
        }

        _lines = kept.Count == 0 ? null : kept.ToArray();
    }

    /// <summary>The container's own identity, opaque here.</summary>
    public string Key { get; }

    /// <summary>What to call it in a sentence shown to a player.</summary>
    public string Describe { get; }

    public SitePoint Position { get; }

    public SupplyAccess Access { get; }

    public IReadOnlyList<SupplyLine> Lines => _lines ?? Array.Empty<SupplyLine>();

    public bool CanTake => (Access & SupplyAccess.Take) == SupplyAccess.Take && Key.Length != 0;

    public bool CanDeposit => (Access & SupplyAccess.Deposit) == SupplyAccess.Deposit && Key.Length != 0;

    /// <summary>How many units of one item it was seen to hold. Summed rather
    /// than first-wins: two lines for one item mean both, and dropping one
    /// under-reports the chest and makes a job look impossible that is not.
    /// </summary>
    public int UnitsOf(string? item)
    {
        if (string.IsNullOrEmpty(item))
        {
            return 0;
        }

        int total = 0;
        foreach (SupplyLine line in Lines)
        {
            if (string.Equals(line.Item, item, StringComparison.Ordinal))
            {
                total += line.Units;
            }
        }

        return total;
    }

    public override string ToString() => Key.Length == 0 ? "<no container>" : Key;
}

/// <summary>One line of a round's requirement.</summary>
internal readonly struct RoundManifestLine
{
    internal RoundManifestLine(string? item, int units)
    {
        Item = item ?? string.Empty;
        Units = units;
    }

    public string Item { get; }

    public int Units { get; }

    public bool IsValid => Item.Length != 0 && Units > 0;

    public override string ToString() => Units + " " + Item;
}

/// <summary>Everything one maintenance round needs, totalled before a single
/// step exists.
///
/// <b>This is the difference between one storage trip and five.</b> The shipped
/// loop asks "which fire is emptiest" and fetches for that one fire, so five low
/// torches are five walks to the chest; a manifest asks "what does the whole
/// round take" and the answer is one number per item, which can be drawn once.
/// Nothing else in this product is allowed to answer that question — a step that
/// recomputed what it needed from whatever was in front of it would be the old
/// loop wearing new clothes.
///
/// <b>A requirement, never a promise.</b> It says what the round needs. It says
/// nothing about what is available, what is reserved, or what has been
/// delivered; those are the sightings and the custody ledger, separately,
/// because the moment a plan starts tracking progress it becomes a second source
/// of truth about a player's material.</summary>
internal readonly struct RoundManifest
{
    private readonly RoundManifestLine[]? _lines;

    internal RoundManifest(IEnumerable<RoundManifestLine>? lines)
    {
        if (lines == null)
        {
            _lines = null;
            return;
        }

        var kept = new List<RoundManifestLine>();
        foreach (RoundManifestLine line in lines)
        {
            if (line.IsValid)
            {
                kept.Add(line);
            }
        }

        _lines = kept.Count == 0 ? null : kept.ToArray();
    }

    /// <summary>A round that needs nothing. Not the same as a round with nothing
    /// to do.</summary>
    internal static RoundManifest Empty => default;

    /// <summary>The lines, in a deterministic order: the item name, ordinally.
    /// A manifest whose order depended on which fire the engine listed first
    /// would make two identical settlements produce two different plans.
    /// </summary>
    public IReadOnlyList<RoundManifestLine> Lines => _lines ?? Array.Empty<RoundManifestLine>();

    public bool IsEmpty => _lines == null;

    public int TotalUnits
    {
        get
        {
            int total = 0;
            foreach (RoundManifestLine line in Lines)
            {
                total += line.Units;
            }

            return total;
        }
    }

    public int UnitsOf(string? item)
    {
        if (string.IsNullOrEmpty(item))
        {
            return 0;
        }

        int total = 0;
        foreach (RoundManifestLine line in Lines)
        {
            if (string.Equals(line.Item, item, StringComparison.Ordinal))
            {
                total += line.Units;
            }
        }

        return total;
    }

    /// <summary>The manifest as a player would read it: "12 Wood and 4 Resin".
    /// </summary>
    public string Describe()
    {
        if (IsEmpty)
        {
            return "nothing";
        }

        var text = new System.Text.StringBuilder();
        for (int index = 0; index < Lines.Count; index++)
        {
            if (index > 0)
            {
                text.Append(index == Lines.Count - 1 ? " and " : ", ");
            }

            text.Append(Lines[index].Units.ToString(CultureInfo.InvariantCulture));
            text.Append(' ');
            text.Append(Lines[index].Item);
        }

        return text.ToString();
    }
}

/// <summary>Adding a round up, and saying what it is short of.</summary>
internal static class RoundManifestArithmetic
{
    /// <summary>Everything the stops between them need, one line per item.
    ///
    /// Non-stops contribute nothing, by construction rather than by a filter the
    /// caller has to remember: a <see cref="LightNeed"/> that is not a stop
    /// carries zero units.</summary>
    internal static RoundManifest Total(IReadOnlyList<LightNeed>? needs)
    {
        if (needs == null || needs.Count == 0)
        {
            return RoundManifest.Empty;
        }

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (LightNeed need in needs)
        {
            if (!need.IsAStop || need.Item.Length == 0)
            {
                continue;
            }

            totals.TryGetValue(need.Item, out int running);
            totals[need.Item] = running + need.Units;
        }

        return Build(totals);
    }

    /// <summary>What the approved TAKE containers cannot cover.
    ///
    /// <b>No free fuel.</b> Ten torches and no resin is not a round that starts
    /// and stalls four stops in; it is a round that does not start, and a
    /// sentence naming resin. Everything a container is seen to hold counts,
    /// whichever container it is, because the plan may open more than one — but
    /// only containers the player approved for taking, and a container approved
    /// only for deposits is not a source however full it is.</summary>
    internal static RoundManifest Shortfall(
        in RoundManifest wanted, IReadOnlyList<SupplySighting>? sources)
    {
        if (wanted.IsEmpty)
        {
            return RoundManifest.Empty;
        }

        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RoundManifestLine line in wanted.Lines)
        {
            int available = 0;
            if (sources != null)
            {
                foreach (SupplySighting source in sources)
                {
                    if (source.CanTake)
                    {
                        available += source.UnitsOf(line.Item);
                    }
                }
            }

            int shortBy = line.Units - available;
            if (shortBy > 0)
            {
                missing.TryGetValue(line.Item, out int running);
                missing[line.Item] = running + shortBy;
            }
        }

        return Build(missing);
    }

    /// <summary>What was fetched and not used up, as a manifest.
    ///
    /// Subtraction clamps at nothing: a round that used more of something than
    /// it fetched is a round whose custody ledger has a hole in it, and this
    /// type is not the place that would notice. Reporting a negative surplus
    /// would be inventing a number.</summary>
    internal static RoundManifest Subtract(in RoundManifest fetched, in RoundManifest used)
    {
        if (fetched.IsEmpty)
        {
            return RoundManifest.Empty;
        }

        var left = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RoundManifestLine line in fetched.Lines)
        {
            int over = line.Units - used.UnitsOf(line.Item);
            if (over > 0)
            {
                left.TryGetValue(line.Item, out int running);
                left[line.Item] = running + over;
            }
        }

        return Build(left);
    }

    private static RoundManifest Build(Dictionary<string, int> totals)
    {
        if (totals.Count == 0)
        {
            return RoundManifest.Empty;
        }

        var items = new List<string>(totals.Keys);
        items.Sort(StringComparer.Ordinal);

        var lines = new List<RoundManifestLine>(items.Count);
        foreach (string item in items)
        {
            lines.Add(new RoundManifestLine(item, totals[item]));
        }

        return new RoundManifest(lines);
    }
}
