using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>Every engine write the Thorstein figure makes, in one file, so the
/// repository's runtime audit (#400) can pin each one by its exact spelling.
///
/// <b>All of it is local.</b> Each object touched here is either a piece of the
/// presentation figure, which is never networked or saved, or an inactive
/// holder that exists for the length of one method. Nothing here reaches a
/// vanilla object in the world, a player, a piece or a network view. The
/// figure itself is extracted by Concerned NPC (<c>NpcPresentationBody</c>),
/// which instantiates the game's character under an inactive holder and
/// destroys everything it does not keep before it returns.</summary>
internal static class FigureParts
{
    /// <summary>An inactive holder. Anything instantiated under it stays dark
    /// until it is deliberately moved into the live figure.</summary>
    internal static GameObject Holder()
    {
        var holder = new GameObject("CF_ThorsteinHarvest");
        holder.SetActive(false);
        return holder;
    }

    /// <summary>One attachment mesh, cloned dark under <paramref name="holder"/>.
    /// Only ever an item's <c>attach</c> child, never the item itself.</summary>
    internal static GameObject Harvest(GameObject source, GameObject holder) =>
        UnityEngine.Object.Instantiate(source, holder.transform);

    /// <summary>Destroys at once something that has not woken, or a component
    /// on it. Null is a no-op, so a <c>finally</c> can call it
    /// unconditionally.</summary>
    internal static void Discard(UnityEngine.Object? target)
    {
        if (target != null)
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>Destroys at the end of the frame: the finished figure, a piece
    /// the fit check took off, or the body's own material instance.</summary>
    internal static void Drop(UnityEngine.Object? target)
    {
        if (target != null)
        {
            UnityEngine.Object.Destroy(target);
        }
    }

    /// <summary>Parents a piece the way the game does, then zeroes its local
    /// pose. <c>SetParent</c>'s default keeps the piece's own world scale under
    /// a joint that is not at unit scale.</summary>
    internal static void Seat(Transform piece, Transform parent)
    {
        piece.SetParent(parent);
        piece.localPosition = Vector3.zero;
        piece.localRotation = Quaternion.identity;
    }

    /// <summary>The optional <c>equipoffset</c> nudge the game applies after
    /// parenting. A prefab asset sits at the origin, so its child's world pose
    /// is its local one.</summary>
    internal static void Offset(GameObject prefab, Transform attached)
    {
        Transform offset = prefab.transform.Find("equipoffset");
        if (offset == null)
        {
            return;
        }

        attached.localPosition += offset.position;
        attached.localRotation *= offset.rotation;
    }

    /// <summary>Stands the finished figure at home. The figure has no collider
    /// and no rigidbody, so this is a picture being hung, not a body being
    /// moved.</summary>
    internal static void Place(Transform figure, Vector3 spot, Quaternion facing)
    {
        figure.position = spot;
        figure.rotation = facing;
    }
}
