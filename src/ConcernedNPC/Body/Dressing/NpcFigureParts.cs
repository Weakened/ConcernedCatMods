using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Dressing;

/// <summary>Every engine write that dresses, stands or takes down a
/// presentation figure, in one file, so the repository's audit can pin each
/// one by its exact spelling (lifted from Concerned Foreman's
/// <c>FigureParts</c>, #295, so that no product keeps a copy).
///
/// <b>Internal, and that is the confinement across assemblies.</b> These
/// helpers take any object or transform. A consumer cannot reach them at all:
/// the only public way in is <see cref="NpcFigureDresser"/>, whose members take
/// the figure handle the extraction returned, never an arbitrary object. Inside
/// this package the validator confines who may call them.
///
/// <b>All of it is local.</b> Each object touched here is a piece of a
/// presentation figure, which is never networked or saved, or an inactive
/// holder that exists for the length of one method. Nothing here reaches a
/// vanilla object in the world, a player, a piece or a network view.</summary>
internal static class NpcFigureParts
{
    /// <summary>An inactive holder. Anything instantiated under it stays dark
    /// until it is deliberately moved into the live figure.</summary>
    internal static GameObject Holder(string name)
    {
        var holder = new GameObject(name);
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

    /// <summary>Stands the finished figure. The figure has no collider and no
    /// rigidbody, so this is a picture being hung, not a body being
    /// moved.</summary>
    internal static void Place(Transform figure, Vector3 spot, Quaternion facing)
    {
        figure.position = spot;
        figure.rotation = facing;
    }

    /// <summary>Gives the figure's own body renderer the source character's
    /// other body, the way the game's equipment component does when the
    /// character screen's toggle changes. The renderer is the figure's own
    /// extracted copy; the mesh is an asset and is not written.</summary>
    internal static void Reshape(SkinnedMeshRenderer body, Mesh mesh)
    {
        body.sharedMesh = mesh;
    }

    /// <summary>A place to talk to the figure (#295 S3): a child of the
    /// figure's root carrying a body-sized capsule and the game's own hover and
    /// use. The collider is for looking at, never walking into: it is put on a
    /// layer characters walk through, or made a trigger when no such layer
    /// exists. Only the child's layer changes; the figure's own renderers keep
    /// theirs.</summary>
    internal static NpcFigureTalk TalkPoint(GameObject figure, int nonSolidLayer)
    {
        var point = new GameObject("talk");
        point.transform.SetParent(figure.transform, worldPositionStays: false);
        point.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        CapsuleCollider collider = point.AddComponent<CapsuleCollider>();
        collider.height = 1.8f;
        collider.radius = 0.35f;
        if (nonSolidLayer >= 0)
        {
            point.layer = nonSolidLayer;
        }
        else
        {
            collider.isTrigger = true;
        }

        return point.AddComponent<NpcFigureTalk>();
    }
}
