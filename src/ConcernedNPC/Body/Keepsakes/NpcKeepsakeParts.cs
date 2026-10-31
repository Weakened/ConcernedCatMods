using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Keepsakes;

/// <summary>Every engine write that builds, places or removes a keepsake, in
/// one file, so the repository's audit can pin each by its exact spelling
/// (#295 S2; the pattern is Concerned Cartographer's Broken Compass, lifted so
/// that no product keeps another copy).
///
/// <b>Internal.</b> A consumer reaches a keepsake only through
/// <see cref="NpcKeepsake"/>, whose members take the role's spec and a place,
/// never an object.
///
/// <b>All of it is local.</b> A keepsake is a fresh object holding meshes
/// borrowed by reference from a game prefab: no network view, so no saved data
/// and nothing replicated; no item, so nothing enters anybody's inventory. The
/// prefab itself is instantiated only under an <b>inactive</b> holder, read,
/// and destroyed before anything in it can run.</summary>
internal static class NpcKeepsakeParts
{
    /// <summary>An inactive holder: whatever is cloned under it never wakes.</summary>
    internal static GameObject Holder(string name)
    {
        var holder = new GameObject(name);
        holder.SetActive(false);
        return holder;
    }

    /// <summary>A dark clone of a game prefab, to read its meshes from.</summary>
    internal static GameObject Harvest(GameObject prefab, GameObject holder)
    {
        GameObject clone = UnityEngine.Object.Instantiate(prefab, holder.transform);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        return clone;
    }

    /// <summary>The keepsake's own root, empty, at the given scale.</summary>
    internal static GameObject Root(string name, float scale)
    {
        var root = new GameObject(name);
        root.transform.localScale = Vector3.one * scale;
        return root;
    }

    /// <summary>One borrowed mesh under the root. Shared materials only: reading
    /// <c>materials</c> would copy them, and writing one would edit the asset
    /// every other object is drawn with.</summary>
    internal static void Part(
        GameObject root, Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var part = new GameObject("part");
        part.transform.SetParent(root.transform, worldPositionStays: false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation;
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterials = materials;
    }

    /// <summary>The hover and use behaviour, on the root.</summary>
    internal static NpcKeepsakeBehaviour Behaviour(GameObject root) => root.AddComponent<NpcKeepsakeBehaviour>();

    /// <summary>A small collider to be looked at, not walked into: put on a
    /// non-solid layer, or made a trigger when no such layer exists.</summary>
    internal static SphereCollider Collider(GameObject root)
    {
        SphereCollider collider = root.AddComponent<SphereCollider>();
        collider.radius = 0.4f;
        collider.center = new Vector3(0f, 0.2f, 0f);
        return collider;
    }

    /// <summary>Puts the keepsake and everything under it on one layer.</summary>
    internal static void Layer(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            Layer(child.gameObject, layer);
        }
    }

    /// <summary>Stands the keepsake on the ground. It has no rigidbody.</summary>
    internal static void Place(Transform keepsake, Vector3 spot, Quaternion facing)
    {
        keepsake.position = spot;
        keepsake.rotation = facing;
    }

    /// <summary>Destroys at once something that never woke. Null is a no-op.</summary>
    internal static void Discard(UnityEngine.Object? target)
    {
        if (target != null)
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>Removes the keepsake at the end of the frame.</summary>
    internal static void Drop(UnityEngine.Object? target)
    {
        if (target != null)
        {
            UnityEngine.Object.Destroy(target);
        }
    }
}
