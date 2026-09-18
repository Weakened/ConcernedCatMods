using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>What the companion director will say about a companion to another
/// Concerned Cat mod. Four facts and a position, all of them things this
/// product already knows and none of them a decision the asking side gets to
/// influence.</summary>
internal readonly struct CompanionPresenceFacts
{
    internal CompanionPresenceFacts(bool known, bool present, bool visible, bool free, Vector3? position)
    {
        Known = known;
        Present = present;
        Visible = visible;
        Free = free;
        Position = position;
    }

    /// <summary>The player has met him and the feature gate is open. Access is
    /// monotonic (#264), so this never goes back to false once it is true.
    /// </summary>
    public bool Known { get; }

    /// <summary>A body exists in the world right now.</summary>
    public bool Present { get; }

    /// <summary>Presentation is switched on. Hiding him never revokes
    /// anything, but it does mean he is not here to help.</summary>
    public bool Visible { get; }

    /// <summary>Present, shown, and not in the middle of something.</summary>
    public bool Free { get; }

    /// <summary>Where the body actually is. Null when there is none.</summary>
    public Vector3? Position { get; }
}
