using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>How much of one item a container was seen to hold.
///
/// <b>Why this is not a <see cref="JobManifestLine"/>, which has the same two
/// fields.</b> Because a manifest line is a <i>requirement</i> and this is an
/// <i>observation</i>, and the manifest's own contract says in as many words
/// that a manifest says nothing about what is available. One type for both would
/// make "the job needs forty nails" and "the chest had forty nails" the same
/// value, and the first arithmetic mistake that mixed them would look correct.
/// Two small types that cannot be passed for one another is the cheapest
/// possible way to make that mistake fail to compile.</summary>
public readonly struct StockLine
{
    public StockLine(string? item, int units)
    {
        Item = item ?? string.Empty;
        Units = units;
    }

    /// <summary>The role's token for the thing.</summary>
    public string Item { get; }

    /// <summary>How many units of it were seen. Never negative; zero means the
    /// line should not have been written.</summary>
    public int Units { get; }

    public bool IsValid => Item.Length != 0 && Units > 0;

    public override string ToString() => Units + " " + Item;
}

/// <summary>A container and what it was observed to hold, at one instant.
///
/// <b>Observed, and that word is load-bearing.</b> <see cref="INpcContainer"/>
/// deliberately exposes no inventory - the port arrives with the custody ledger,
/// because a port with an add and no ledger is an invitation to mint items on a
/// retry. So the counts here are the role's own reading of the chest, taken when
/// the snapshot was taken, and they go stale exactly as fast as everything else
/// in a snapshot does. Nothing in this library withdraws anything on the
/// strength of them: the plan says which chest and how much, and the custody
/// layer is what actually moves an item and what records that it may or may not
/// have happened.
///
/// <b>Permission is asked at the moment of use and never cached.</b>
/// <see cref="IsUsable"/> reads <see cref="INpcContainer.Access"/> on every
/// call, exactly as the port's own contract requires, because the player can
/// walk into the chest, a ward can go up and ownership can migrate between the
/// tick that chose it and the tick that opens it.</summary>
public readonly struct SourceStock : INpcEpochScoped
{
    private readonly StockLine[]? _lines;

    public SourceStock(INpcContainer? container, IEnumerable<StockLine>? lines)
    {
        Container = container;

        if (lines == null)
        {
            _lines = null;
            return;
        }

        var kept = new List<StockLine>();
        foreach (StockLine line in lines)
        {
            if (line.IsValid)
            {
                kept.Add(line);
            }
        }

        _lines = kept.Count == 0 ? null : kept.ToArray();
    }

    /// <summary>The container itself. Null for a defaulted value, which is never
    /// usable.</summary>
    public INpcContainer? Container { get; }

    /// <summary>What it was seen to hold, in the order it was read.</summary>
    public IReadOnlyList<StockLine> Lines => _lines ?? Array.Empty<StockLine>();

    /// <summary>The container's name, or empty. Also the tie-break that makes
    /// source selection deterministic.</summary>
    public string Key => Container == null ? string.Empty : Container.Key ?? string.Empty;

    /// <summary>The world load the container's name was minted in.</summary>
    public NpcWorldEpoch Epoch => Container == null ? NpcWorldEpoch.Unknown : Container.Epoch;

    /// <summary>Where it stands, for deciding whether to walk over.</summary>
    public NpcPoint Position => Container == null ? default : Container.Position;

    /// <summary>Whether an NPC may take from it <b>right now</b>. Re-read on
    /// every call on purpose.</summary>
    public bool IsUsable => Container != null && Container.Access.CanTake && _lines != null;

    /// <summary>How many units of one item this job may plan on: what was seen,
    /// less what other jobs have already set aside.
    ///
    /// <b>The overload every planning decision goes through.</b> A raw count is
    /// what the chest holds; this is what is free, and the difference is two
    /// jobs planning the same forty nails. A null
    /// <paramref name="availability"/> is the raw count and is the caller
    /// saying so.</summary>
    internal int UnitsOf(string? item, INpcSourceAvailability? availability)
    {
        int seen = UnitsOf(item);
        if (availability == null || seen <= 0 || string.IsNullOrEmpty(item))
        {
            return seen;
        }

        int free = availability.AvailableIn(Key, item!, seen);
        if (free < 0)
        {
            return 0;
        }

        // An implementation that answered with more than is there is answering
        // about a chest nobody looked in. What was seen is the ceiling.
        return free > seen ? seen : free;
    }

    /// <summary>How many units of one item it was seen to hold. Summed rather
    /// than first-wins, because two lines for one item mean both and dropping
    /// one under-reports the chest.</summary>
    public int UnitsOf(string? item)
    {
        if (string.IsNullOrEmpty(item))
        {
            return 0;
        }

        int total = 0;
        foreach (StockLine line in Lines)
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
