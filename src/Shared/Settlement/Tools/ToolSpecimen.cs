using System;
using System.Globalization;

namespace TheConcernedCat.Settlement.Tools;

/// <summary>What a tool is for, decided by what it can <i>do</i>.
///
/// <see cref="None"/> is zero, so an unclassified item is not a tool.</summary>
internal enum ToolKind
{
    /// <summary>Not a tool this build issues. The default, and never valid.</summary>
    None = 0,

    /// <summary>Something that can fell a tree. Verified against the installed
    /// 1.0.12 build by <b>capability</b>: an axe is an item whose
    /// <c>HitData.DamageTypes.m_chop</c> is above zero. Vanilla's own gate is
    /// <c>HitData.CheckToolTier(m_minToolTier, alwaysAllowTierZero: true)</c>,
    /// comparing <c>m_toolTier</c> — so what a given axe may fell is vanilla's
    /// answer, not ours.</summary>
    Axe = 1,

    /// <summary>Something that can build.
    ///
    /// Carrying a <c>PieceTable</c> is <b>necessary but not sufficient</b>, and
    /// an earlier version of this comment said it was sufficient. Vanilla's own
    /// <c>Inventory.GetAllPieceTables(List&lt;PieceTable&gt;)</c> deliberately
    /// collects <i>several</i> distinct tables from one inventory and dedupes
    /// them — which is proof from the binary that more than one item type
    /// carries one. The hoe and the cultivator are the known others.
    ///
    /// So the classifier additionally requires the table to contain at least one
    /// piece that is a real structure rather than a terrain operation. See
    /// <c>ToolClassifier</c>.
    ///
    /// Neither kind is recognised by prefab name. A name match would break on
    /// any modded or renamed tool and would silently accept something that
    /// cannot do the job.</summary>
    Hammer = 2,
}

/// <summary>One real tool instance, as it was when the player handed it over.
///
/// <b>This is a description, not a copy.</b> The item itself stays a single
/// instance that moved from one inventory to another; this records enough to
/// show the player what they gave and to recognise the <i>kind</i> of thing it
/// was. Nothing here can be used to reconstruct a tool — by convention rather
/// than by construction, since a determined caller could still read the fields.
///
/// <b>What it cannot do, corrected:</b> it cannot tell one axe from another
/// identical axe. <see cref="ItemKey"/> is a <i>type</i> token — the adapter
/// supplies <c>m_shared.m_name</c>, which is the same field vanilla's own
/// <c>Inventory.CountItems</c> uses to count fungible stacks. An earlier
/// version of this comment claimed the record was enough "to notice if what
/// comes back is not what went in"; it is not, and a player holding two
/// identical axes may get either one back. Per-instance identity would need
/// <c>ItemData.m_customData</c>, which this type has no field for and which is
/// tracked separately.
///
/// Durability is recorded <i>at issue</i> and is never treated as current. A
/// tool wears while it is used, so "is this still usable" is a live question for
/// the adapter, and answering it from this snapshot would be exactly the
/// zero-wear infinite tool the owner notes rule out.</summary>
internal readonly struct ToolSpecimen : IEquatable<ToolSpecimen>
{
    internal ToolSpecimen(
        ToolKind kind, string itemKey, int quality, float durabilityAtIssue, int toolTier)
    {
        if (kind == ToolKind.None)
        {
            throw new ArgumentException("A tool specimen needs a kind.", nameof(kind));
        }

        if (string.IsNullOrEmpty(itemKey))
        {
            throw new ArgumentException(
                "A tool specimen needs the item's own identity.", nameof(itemKey));
        }

        if (quality < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quality), "Vanilla item quality starts at one.");
        }

        if (toolTier < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toolTier), "A tool tier is never negative.");
        }

        if (!(durabilityAtIssue >= 0f) || float.IsInfinity(durabilityAtIssue))
        {
            // The reason matters and an earlier version of it was wrong: NaN is
            // rejected NOT because it "compares false against itself" here.
            // Single.Equals(NaN, NaN) returns TRUE -- unlike operator == -- and
            // this type compares with .Equals. NaN is rejected because a
            // durability that is not a number cannot be shown to a player, cannot
            // be ordered against anything, and can only have arrived from a bug
            // worth stopping at.
            throw new ArgumentOutOfRangeException(
                nameof(durabilityAtIssue),
                "Durability at issue must be a real, non-negative number.");
        }

        Kind = kind;
        ItemKey = itemKey;
        Quality = quality;
        DurabilityAtIssue = durabilityAtIssue;
        ToolTier = toolTier;
    }

    public ToolKind Kind { get; }

    /// <summary>The item's <b>type</b> token, supplied by the adapter. Opaque
    /// here: this layer never interprets it, only remembers and compares it.
    ///
    /// Not an instance identity, and not capable of becoming one — see the type
    /// summary. Named <c>ItemKey</c> rather than <c>ItemId</c> for that
    /// reason.</summary>
    public string ItemKey { get; }

    public int Quality { get; }

    /// <summary>Durability at the moment it was handed over. A snapshot for
    /// showing and for recognition — <b>never</b> an answer to "can it still be
    /// used", which only the live item knows.</summary>
    public float DurabilityAtIssue { get; }

    /// <summary>Vanilla's <c>m_toolTier</c>, which is what
    /// <c>HitData.CheckToolTier</c> compares against a target's
    /// <c>m_minToolTier</c>. Recorded so a refusal can say "this axe is not good
    /// enough for that tree" rather than "something went wrong".</summary>
    public int ToolTier { get; }

    public bool IsEmpty => Kind == ToolKind.None;

    public bool Equals(ToolSpecimen other)
    {
        return Kind == other.Kind
            && string.Equals(ItemKey, other.ItemKey, StringComparison.Ordinal)
            && Quality == other.Quality
            && ToolTier == other.ToolTier
            && DurabilityAtIssue.Equals(other.DurabilityAtIssue);
    }

    public override bool Equals(object? obj) => obj is ToolSpecimen other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + (int)Kind;
            hash = (hash * 31) + (ItemKey == null ? 0 : StringComparer.Ordinal.GetHashCode(ItemKey));
            hash = (hash * 31) + Quality;
            hash = (hash * 31) + ToolTier;
            hash = (hash * 31) + DurabilityAtIssue.GetHashCode();
            return hash;
        }
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} \"{1}\" (quality {2}, tier {3}, {4:0.#} durability when given)",
            Kind, ItemKey, Quality, ToolTier, DurabilityAtIssue);
    }
}
