using System;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>The identity of one kind of item, and <b>the whole of what must
/// survive a transfer unchanged</b>.
///
/// <b>Why quality and variant are here when today's material has neither.</b>
/// The two materials the shipped settlement moves carry no quality and no
/// variant, and a contract that assumed so would be wrong the first time an NPC
/// carries a tool or a dyed cape. More to the point, a transfer that recreated
/// an item from its name alone would silently drop what the moved one carried;
/// the contract is that a transfer moves items rather than describing them, and
/// this type is the description used only to <i>count</i> them.
///
/// <b>What the name is.</b> Whatever the role uses to identify an item kind.
/// This library never parses it, never composes one, and owns none: the audit
/// over these sources refuses a string constant precisely so that no name a
/// player's save depends on can be born here.</summary>
internal readonly struct NpcMaterial : IEquatable<NpcMaterial>
{
    internal NpcMaterial(string? itemName, int quality, int variant)
    {
        ItemName = itemName ?? string.Empty;
        Quality = quality < 1 ? 1 : quality;
        Variant = variant < 0 ? 0 : variant;
    }

    /// <summary>One kind of ordinary item: quality one, no variant.</summary>
    internal static NpcMaterial Of(string? itemName) => new NpcMaterial(itemName, 1, 0);

    internal string ItemName { get; }

    internal int Quality { get; }

    internal int Variant { get; }

    /// <summary>False for a material nobody named. A ledger refuses to hold one
    /// rather than keeping a pile of nothing.</summary>
    internal bool IsNamed => !string.IsNullOrEmpty(ItemName);

    public bool Equals(NpcMaterial other) =>
        string.Equals(ItemName, other.ItemName, StringComparison.Ordinal)
        && Quality == other.Quality && Variant == other.Variant;

    public override bool Equals(object? obj) => obj is NpcMaterial other && Equals(other);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(ItemName ?? string.Empty) * 397) ^ (Quality * 31) ^ Variant);

    public override string ToString() =>
        IsNamed ? ItemName + (Quality > 1 ? " q" + Quality : string.Empty) : "<unnamed>";
}
