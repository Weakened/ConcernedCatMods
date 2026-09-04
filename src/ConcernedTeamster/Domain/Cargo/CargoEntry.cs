namespace TheConcernedCat.ConcernedTeamster.Domain.Cargo;

/// <summary>One immutable cargo line (CT-006): an item stack in the cart's
/// container. Weights come from the game's own quality-scaled accessors, so
/// the line weight is the number vanilla itself would charge — never a
/// recomputation. Unknown data is flagged, not defaulted.</summary>
public sealed class CargoEntry
{
    private CargoEntry(
        string itemId,
        string displayNameToken,
        int count,
        float unitWeight,
        float lineWeight,
        bool weightKnown)
    {
        ItemId = itemId;
        DisplayNameToken = displayNameToken;
        Count = count;
        UnitWeight = unitWeight;
        LineWeight = lineWeight;
        WeightKnown = weightKnown;
    }

    /// <summary>Stable identity — the item's prefab name when readable,
    /// otherwise a caller-supplied fallback. Never null.</summary>
    public string ItemId { get; }

    /// <summary>The game's localization token (for example "$item_stone");
    /// may be empty for broken modded items — display falls back via
    /// <see cref="EffectiveDisplayName"/>.</summary>
    public string DisplayNameToken { get; }

    public int Count { get; }

    /// <summary>Per-unit weight (quality-scaled). Meaningful only when
    /// <see cref="WeightKnown"/>.</summary>
    public float UnitWeight { get; }

    /// <summary>The stack's weight as the game computes it. Meaningful only
    /// when <see cref="WeightKnown"/>; unknown lines contribute 0 to totals
    /// and are counted separately instead of silently skewing them.</summary>
    public float LineWeight { get; }

    public bool WeightKnown { get; }

    /// <summary>Name fallback chain for display: token → item id →
    /// "unknown item".</summary>
    public string EffectiveDisplayName
    {
        get
        {
            if (DisplayNameToken.Length > 0)
            {
                return DisplayNameToken;
            }

            return ItemId.Length > 0 ? ItemId : "unknown item";
        }
    }

    public static CargoEntry Create(
        string? itemId,
        string? displayNameToken,
        int count,
        float unitWeight,
        float lineWeight,
        bool weightKnown)
    {
        return new CargoEntry(
            itemId ?? string.Empty,
            displayNameToken ?? string.Empty,
            count,
            weightKnown ? unitWeight : 0f,
            weightKnown ? lineWeight : 0f,
            weightKnown);
    }

    /// <summary>An explicit marker for a container slot whose item could not
    /// be read at all (broken modded item): zero count, unknown weight, and
    /// a slot-unique id so ordering stays deterministic.</summary>
    public static CargoEntry CreateUnreadable(int slotIndex)
    {
        return new CargoEntry(
            "unreadable-slot-" + slotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Empty,
            0,
            0f,
            0f,
            weightKnown: false);
    }
}
