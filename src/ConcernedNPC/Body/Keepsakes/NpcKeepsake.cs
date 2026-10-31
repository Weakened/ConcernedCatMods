using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Keepsakes;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Keepsakes;

/// <summary>One companion keepsake in the world: a small object a player walks
/// up to, hovers and examines to start a companion's introduction (#295 S2,
/// owner decision 2026-10-30). Shared by every product with a companion -
/// Thorstein's carpenter's square first, Gunnar's buckle (#457) and Sunniva's
/// flint next. Concerned Cartographer's Broken Compass predates this and stays
/// its own.
///
/// <b>Local in the strongest sense available.</b> No network view: no saved
/// data, nothing replicated, and a peer without the mod sees nothing. Not an
/// item: examining it calls the role back and touches no inventory. Its
/// collider is for looking at, on a layer characters walk through (or a
/// trigger), so it never blocks anybody. It is never saved, so the role places
/// it again each session while the introduction is unfinished and takes it
/// away once it is.
///
/// <b>Why no member takes an object.</b> As with the dressers, a library member
/// that built on, moved or destroyed whatever it was handed would be a way
/// round a consumer's own audit. So a keepsake is built from the role's spec and
/// a place, and acts only on the object it built.</summary>
public sealed class NpcKeepsake
{
    private readonly GameObject _root;
    private readonly NpcKeepsakeBehaviour _behaviour;
    private bool _removed;

    private NpcKeepsake(GameObject root, NpcKeepsakeBehaviour behaviour, string source, bool onNonSolidLayer)
    {
        _root = root;
        _behaviour = behaviour;
        Source = source;
        OnNonSolidLayer = onNonSolidLayer;
    }

    /// <summary>The game prefab whose meshes it borrowed.</summary>
    public string Source { get; }

    /// <summary>True on a non-solid layer; false means a trigger collider,
    /// which the game's hover reaches less reliably.</summary>
    public bool OnNonSolidLayer { get; }

    /// <summary>True once the game's own hover has reached it.</summary>
    public bool HoverObserved => !_removed && _behaviour != null && _behaviour.HoverObserved;

    /// <summary>True while it is in the world.</summary>
    public bool IsPlaced => !_removed && _root != null;

    /// <summary>Builds and places a keepsake, or refuses and says why.</summary>
    /// <param name="spec">The role's keepsake.</param>
    /// <param name="spot">Where it lies.</param>
    /// <param name="facing">Which way it lies.</param>
    /// <param name="rootName">The object's name, the role's own.</param>
    /// <param name="holderName">The inactive holder's name, the role's own.</param>
    /// <param name="onExamine">Called when the player uses it.</param>
    public static NpcKeepsake? TryPlace(
        NpcKeepsakeSpec spec, Vector3 spot, Quaternion facing, string rootName, string holderName, Action onExamine,
        Action<string>? log, out string refusal)
    {
        refusal = string.Empty;
        if (spec == null || onExamine == null || string.IsNullOrWhiteSpace(rootName)
            || string.IsNullOrWhiteSpace(holderName))
        {
            refusal = "the role did not say what or where";
            return null;
        }

        foreach (string candidate in spec.PrefabCandidates)
        {
            GameObject? root = null;
            try
            {
                GameObject? prefab = FindPrefab(candidate);
                if (prefab == null)
                {
                    continue;
                }

                root = Build(prefab, rootName, holderName, spec.Scale);
                if (root == null)
                {
                    continue;
                }

                NpcKeepsakeBehaviour behaviour = NpcKeepsakeParts.Behaviour(root);
                behaviour.Configure(spec.HoverName, spec.HoverVerb, onExamine, log);
                SphereCollider collider = NpcKeepsakeParts.Collider(root);
                int layer = NonSolidLayer();
                if (layer >= 0)
                {
                    NpcKeepsakeParts.Layer(root, layer);
                }
                else
                {
                    collider.isTrigger = true;
                }

                NpcKeepsakeParts.Place(root.transform, spot, facing);
                var keepsake = new NpcKeepsake(root, behaviour, candidate, layer >= 0);
                root = null;
                return keepsake;
            }
            catch (Exception exception)
            {
                log?.Invoke("Could not build a keepsake from \"" + candidate + "\" (" + exception.GetType().Name +
                    "); trying the next.");
            }
            finally
            {
                NpcKeepsakeParts.Discard(root);
            }
        }

        refusal = "none of its look's game prefabs is in this build";
        return null;
    }

    /// <summary>Takes it out of the world. Idempotent.</summary>
    public void Remove()
    {
        if (_removed)
        {
            return;
        }

        _removed = true;
        NpcKeepsakeParts.Drop(_root);
    }

    private static GameObject? Build(GameObject prefab, string rootName, string holderName, float scale)
    {
        GameObject holder = NpcKeepsakeParts.Holder(holderName);
        GameObject? clone = null;
        try
        {
            clone = NpcKeepsakeParts.Harvest(prefab, holder);
            var meshes = new List<MeshFilter>();
            foreach (MeshFilter filter in clone.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (meshes.Count >= 24)
                {
                    break;
                }

                if (filter != null && filter.sharedMesh != null)
                {
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.sharedMaterials != null)
                    {
                        meshes.Add(filter);
                    }
                }
            }

            if (meshes.Count == 0)
            {
                return null;
            }

            GameObject root = NpcKeepsakeParts.Root(rootName, scale);
            foreach (MeshFilter filter in meshes)
            {
                Transform source = filter.transform;
                NpcKeepsakeParts.Part(
                    root,
                    filter.sharedMesh,
                    filter.GetComponent<MeshRenderer>().sharedMaterials,
                    clone.transform.InverseTransformPoint(source.position),
                    Quaternion.Inverse(clone.transform.rotation) * source.rotation,
                    source.lossyScale);
            }

            return root;
        }
        finally
        {
            NpcKeepsakeParts.Discard(clone);
            NpcKeepsakeParts.Discard(holder);
        }
    }

    private static GameObject? FindPrefab(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        try
        {
            if (ObjectDB.instance != null && ObjectDB.instance.TryGetItemPrefab(name, out GameObject item)
                && item != null)
            {
                return item;
            }
        }
        catch (Exception)
        {
            // Fall through to the scene's prefabs.
        }

        try
        {
            return ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A layer the game treats as non-solid for characters, or -1.</summary>
    private static int NonSolidLayer()
    {
        foreach (string name in new[] { "piece_nonsolid", "item" })
        {
            try
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    return layer;
                }
            }
            catch (Exception)
            {
                // Try the next name.
            }
        }

        return -1;
    }
}
