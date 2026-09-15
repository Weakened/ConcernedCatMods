using TheConcernedCat.Settlement.Tools;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Decides what a real item is, from what the game says it can do.
///
/// <b>Never by prefab name.</b> A name match breaks on any renamed or modded
/// tool and — worse — silently accepts something that cannot do the job, which
/// is how a worker ends up holding a "hammer" it can build nothing with.
///
/// Verified against the installed 1.0.12 <c>assembly_valheim.dll</c> (SHA256
/// <c>27a766a8…c393a84</c>, the same binary every other Foreman document
/// audits):
///
/// <list type="bullet">
/// <item><c>ItemDrop.ItemData.SharedData.m_damages</c> is a
/// <c>HitData.DamageTypes</c>, which carries <c>public float m_chop</c>. Chop
/// damage is what fells a tree, and <c>TreeBase.RPC_Damage</c> consumes it
/// through the ordinary damage path.</item>
/// <item><c>ItemDrop.ItemData.SharedData.m_buildPieces</c> is a
/// <c>PieceTable</c>. Carrying one is <b>necessary but not sufficient</b> for a
/// hammer — see <c>BuildsStructures</c>, which is where that distinction is
/// actually enforced.</item>
/// <item><c>ItemDrop.ItemData.SharedData.m_toolTier</c> is the integer
/// <c>HitData.CheckToolTier</c> compares against a target's
/// <c>m_minToolTier</c>. We record it so a refusal can name the reason; we do
/// not reimplement the comparison.</item>
/// </list>
///
/// <b>Neither marker is sufficient on its own, and the first version of this
/// class treated both as if they were.</b> A weapon may also chop, so the axe
/// test additionally requires chop to be the item's <i>largest</i> damage
/// component — which keeps a sword out without hardcoding a list of swords. More
/// than one item type carries a piece table, so the hammer test additionally
/// requires the table to offer a real structure rather than only terrain
/// operations.
///
/// Where a classifier still admits something unintended, the consequence is
/// bounded and visible rather than silent: the worker holds it, vanilla's own
/// tier check governs what it may actually fell, and nothing here grants an item
/// damage or capability it does not have.
///
/// <b>Not verified from the binary:</b> whether any non-axe vanilla item has
/// dominant chop damage. Item statistics live in asset bundles, not in the
/// assembly, so decompilation cannot answer it — this is an assumption, and a
/// disposable-world check is the only thing that can settle it.</summary>
internal static class ToolClassifier
{
    /// <summary>What this item is, as far as this build employs tools.
    /// <see cref="ToolKind.None"/> for anything it does not recognise —
    /// including null, which is not an error worth throwing over.</summary>
    internal static ToolKind Classify(ItemDrop.ItemData? item)
    {
        ItemDrop.ItemData.SharedData? shared = item?.m_shared;
        if (shared == null)
        {
            return ToolKind.None;
        }

        // Hammer first: a building tool may also carry incidental damage.
        if (BuildsStructures(shared.m_buildPieces))
        {
            return ToolKind.Hammer;
        }

        if (IsChopper(shared))
        {
            return ToolKind.Axe;
        }

        return ToolKind.None;
    }

    /// <summary>True when this piece table builds structures rather than shaping
    /// ground.
    ///
    /// Carrying a <c>PieceTable</c> at all is <b>not</b> sufficient, and an
    /// earlier version of this classifier treated it as if it were. Vanilla's own
    /// <c>Inventory.GetAllPieceTables(List&lt;PieceTable&gt;)</c> walks an
    /// inventory collecting <i>several distinct</i> tables and deduping them,
    /// which is proof from the binary that more than one item type carries one —
    /// the hoe and the cultivator being the obvious others. A worker handed a hoe
    /// as its "hammer" would be ready to build and able to build nothing.
    ///
    /// The discriminator is structural rather than a name or a data value: a
    /// building table offers at least one piece that is a real
    /// <c>Piece</c> and is not a <c>TerrainOp</c>. Ground-shaping tables offer
    /// only terrain operations.</summary>
    private static bool BuildsStructures(PieceTable? table)
    {
        if (table == null || table.m_pieces == null)
        {
            return false;
        }

        foreach (GameObject piece in table.m_pieces)
        {
            if (piece == null)
            {
                continue;
            }

            if (piece.GetComponent<TerrainOp>() != null)
            {
                continue;
            }

            if (piece.GetComponent<Piece>() != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when chopping is what this item is mainly for.
    ///
    /// Chop above zero alone would admit anything that happens to cut wood at
    /// all. Requiring chop to be the dominant component keeps a sword out
    /// without hardcoding a list of swords.</summary>
    private static bool IsChopper(ItemDrop.ItemData.SharedData shared)
    {
        HitData.DamageTypes damage = shared.m_damages;
        if (!(damage.m_chop > 0f))
        {
            return false;
        }

        return damage.m_chop >= damage.m_damage
            && damage.m_chop >= damage.m_blunt
            && damage.m_chop >= damage.m_slash
            && damage.m_chop >= damage.m_pierce
            && damage.m_chop >= damage.m_pickaxe;
    }

    /// <summary>Describes a real item as a <see cref="ToolSpecimen"/>, or
    /// returns false when it is not a tool this build issues.
    ///
    /// The specimen is a <b>description</b>. The item itself is never copied —
    /// <c>ItemDrop.ItemData.Clone()</c> is deliberately not used anywhere in
    /// the handover path, because a clone is a second axe.</summary>
    internal static bool TryDescribe(ItemDrop.ItemData? item, out ToolSpecimen specimen)
    {
        specimen = default;

        ToolKind kind = Classify(item);
        if (kind == ToolKind.None || item == null)
        {
            return false;
        }

        ItemDrop.ItemData.SharedData shared = item.m_shared;
        string key = shared.m_name;
        if (string.IsNullOrEmpty(key))
        {
            // Nothing to recognise it by later. Refusing beats recording a tool
            // we could not tell apart from another one on the way back.
            return false;
        }

        float durability = item.m_durability;
        if (float.IsNaN(durability) || float.IsInfinity(durability) || durability < 0f)
        {
            return false;
        }

        int quality = item.m_quality < 1 ? 1 : item.m_quality;

        specimen = new ToolSpecimen(kind, key, quality, durability, shared.m_toolTier);
        return true;
    }

    /// <summary>True when this item is still worth swinging.
    ///
    /// Asked of the <b>live</b> item, never of the specimen recorded at issue:
    /// a tool wears while it is used, and answering from the snapshot is the
    /// zero-wear infinite tool the design rules out. An item that cannot take
    /// damage at all is always usable — that is vanilla's own rule, not a
    /// loophole.</summary>
    internal static bool IsStillUsable(ItemDrop.ItemData? item)
    {
        if (item == null)
        {
            return false;
        }

        return !item.m_shared.m_useDurability || item.m_durability > 0f;
    }
}
