using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Builds a purely visual copy of a vanilla prefab.
///
/// The audit ruled out the obvious approach. Instantiating a game prefab and
/// stripping its components afterwards does not work, because components
/// register themselves the moment they wake — <c>BaseAI</c> puts itself in a
/// process-wide static list, <c>ZNetView</c> creates a ZDO — and by the time
/// anything could be stripped, the registration has already happened.
///
/// So nothing from the source prefab is ever allowed to wake. The clone is
/// made underneath an <b>inactive</b> holder, which is what keeps Unity from
/// running any <c>Awake</c>; its meshes and materials are copied out by
/// reference; and the clone is destroyed before the holder is ever enabled.
/// What the caller gets back is a fresh object carrying nothing but
/// <c>MeshFilter</c> and <c>MeshRenderer</c> — no networking, no physics, no
/// save participation, and no shared asset was written to.
///
/// Every failure mode ends in a visible object of some kind, because the
/// introduction must not depend on art: a missing prefab falls through the
/// candidate chain, and an empty chain still produces a primitive. It never
/// returns a silent nothing.</summary>
internal static class LocalVisual
{
    /// <summary>Hard cap on how many renderers are copied out of one prefab.
    /// A prop is a handful; anything claiming hundreds is not a prop and is
    /// not worth the frame time.</summary>
    private const int MaxRenderers = 24;

    internal sealed class Result
    {
        public Result(GameObject root, string source)
        {
            Root = root;
            Source = source;
        }

        public GameObject Root { get; }

        /// <summary>Which prefab (or fallback) the visuals came from. Logged
        /// and reported by the console tool, so what a player is looking at is
        /// always identifiable.</summary>
        public string Source { get; }
    }

    /// <summary>Builds a render-only object, trying each candidate prefab name
    /// in order and falling back to a primitive.</summary>
    /// <param name="nameHint">Object name in the scene hierarchy. Prefixed so
    /// anything this mod created is obvious in a scene dump.</param>
    public static Result Build(
        string nameHint,
        IReadOnlyList<string> candidatePrefabs,
        float scale,
        ManualLogSource log)
    {
        foreach (string candidate in candidatePrefabs)
        {
            try
            {
                GameObject? prefab = FindPrefab(candidate);
                if (prefab == null)
                {
                    continue;
                }

                GameObject? built = TryHarvest(prefab, nameHint, scale, log);
                if (built != null)
                {
                    return new Result(built, candidate);
                }
            }
            catch (Exception exception)
            {
                log.LogInfo(
                    $"Could not build a local visual from \"{candidate}\", trying the next candidate: " +
                    SafeLogText.Brief(exception));
            }
        }

        log.LogInfo(
            $"No vanilla prefab matched for \"{nameHint}\"; using a plain shape instead. " +
            "The introduction is unaffected.");
        return new Result(BuildPrimitive(nameHint, scale), "primitive");
    }

    /// <summary>Looks a prefab up without instantiating anything. Both stores
    /// are tried because items live in one and world objects in the other.</summary>
    public static GameObject? FindPrefab(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        try
        {
            if (ObjectDB.instance != null &&
                ObjectDB.instance.TryGetItemPrefab(name, out GameObject item) &&
                item != null)
            {
                return item;
            }
        }
        catch
        {
            // Fall through to the scene store.
        }

        try
        {
            if (ZNetScene.instance != null)
            {
                GameObject scene = ZNetScene.instance.GetPrefab(name);
                if (scene != null)
                {
                    return scene;
                }
            }
        }
        catch
        {
            // Neither store had it; the caller tries the next candidate.
        }

        return null;
    }

    /// <summary>Searches the live prefab table for a name containing
    /// <paramref name="fragment"/>. Used so a build (or another mod) that
    /// happens to ship a better-matching prop is preferred over our hard-coded
    /// candidates, rather than assuming a name exists.</summary>
    public static string? FindPrefabNameContaining(string fragment)
    {
        try
        {
            if (ZNetScene.instance == null)
            {
                return null;
            }

            List<string> names = ZNetScene.instance.GetPrefabNames();
            if (names == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(name) &&
                    name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return name;
                }
            }
        }
        catch
        {
            // An unavailable prefab table is not an error here.
        }

        return null;
    }

    private static GameObject? TryHarvest(
        GameObject prefab, string nameHint, float scale, ManualLogSource log)
    {
        GameObject holder = new GameObject("CC_VisualHarvest");
        // Inactive BEFORE anything is parented into it. This single line is
        // what guarantees no component from the source prefab ever runs.
        holder.SetActive(false);

        GameObject? clone = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(prefab, holder.transform);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;

            var harvested = new List<(Mesh Mesh, Material[] Materials, Vector3 Position, Quaternion Rotation, Vector3 Scale)>();
            foreach (MeshFilter filter in clone.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (harvested.Count >= MaxRenderers)
                {
                    break;
                }

                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterials == null)
                {
                    continue;
                }

                Transform source = filter.transform;
                harvested.Add((
                    filter.sharedMesh,
                    renderer.sharedMaterials,
                    clone.transform.InverseTransformPoint(source.position),
                    Quaternion.Inverse(clone.transform.rotation) * source.rotation,
                    source.lossyScale));
            }

            if (harvested.Count == 0)
            {
                return null;
            }

            var root = new GameObject("CC_" + nameHint);
            root.transform.localScale = Vector3.one * scale;

            foreach ((Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation, Vector3 localScale) in harvested)
            {
                var part = new GameObject("part");
                part.transform.SetParent(root.transform, worldPositionStays: false);
                part.transform.localPosition = position;
                part.transform.localRotation = rotation;
                part.transform.localScale = localScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;

                // sharedMaterials, never `materials`: reading `materials`
                // would instantiate a private copy per renderer, and writing
                // to one would edit the vanilla asset every other object in
                // the world is drawn with.
                part.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }

            return root;
        }
        finally
        {
            if (clone != null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }

            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    /// <summary>The name the game gives its "pick me up" twinkle. Read out of
    /// the game's own prefab bundle (1.0.12): a child of exactly this name,
    /// holding a <c>ParticleSystem</c> and its renderer and nothing else, sits
    /// under 112 item prefabs - food, meads, berries, mushrooms. No code ever
    /// refers to it; it is prefab data only.</summary>
    public const string ItemSparklesChild = "fx_ItemSparkles";

    /// <summary>Gives <paramref name="root"/> the twinkle the game's own items
    /// have, by copying the <see cref="ItemSparklesChild"/> particle system off
    /// the first candidate prefab that carries it. Returns where it came from,
    /// or null when no candidate did.
    ///
    /// The first cut of this looked for any particle system on a handful of
    /// <c>Pickable_*</c> prefabs, and found nothing on every one of them: in the
    /// game's bundle those prefabs are a mesh, a LOD group and colliders, and
    /// the sparkle lives on the ITEM prefabs instead. So the child is now looked
    /// for by the name the game gives it, on items that carry it, with any
    /// particle system on the candidate as the last resort.
    ///
    /// Same discipline as the rest of this file: the copy is made under an
    /// inactive holder, any script on it is removed while nothing has run, a
    /// script that will not go refuses that candidate, and colliders are
    /// removed. What is left is a particle system and nothing else - no
    /// networking, no pickup, no physics.</summary>
    public static string? TryAttachSparkle(
        GameObject root, IReadOnlyList<string> candidatePrefabs, ManualLogSource log)
    {
        foreach (string candidate in candidatePrefabs)
        {
            GameObject? prefab = FindPrefab(candidate);
            if (prefab == null)
            {
                continue;
            }

            ParticleSystem? source = FindSparkle(prefab.transform)
                ?? prefab.GetComponentInChildren<ParticleSystem>(includeInactive: true);
            if (source == null)
            {
                continue;
            }

            GameObject holder = new GameObject("CC_SparkleHarvest");
            holder.SetActive(false);
            try
            {
                GameObject copy = UnityEngine.Object.Instantiate(source.gameObject, holder.transform);
                foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
                {
                    if (script != null)
                    {
                        UnityEngine.Object.DestroyImmediate(script);
                    }
                }

                if (copy.GetComponentsInChildren<MonoBehaviour>(includeInactive: true).Length > 0)
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                    continue;
                }

                foreach (Collider collider in copy.GetComponentsInChildren<Collider>(includeInactive: true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                copy.name = "sparkle";
                copy.transform.SetParent(root.transform, worldPositionStays: false);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                return candidate + "/" + source.gameObject.name;
            }
            catch (Exception exception)
            {
                log.LogInfo(
                    $"Could not borrow the pickable sparkle from \"{candidate}\": {SafeLogText.Brief(exception)}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        return null;
    }

    /// <summary>The game's own sparkle child anywhere under
    /// <paramref name="parent"/>, by name, including inactive children.
    /// </summary>
    private static ParticleSystem? FindSparkle(Transform parent)
    {
        foreach (ParticleSystem system in parent.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
        {
            if (system != null &&
                string.Equals(system.gameObject.name, ItemSparklesChild, StringComparison.Ordinal))
            {
                return system;
            }
        }

        return null;
    }

    /// <summary>Last resort. Unity's own primitive always exists, so this
    /// cannot fail; its collider is removed because collision is decided by
    /// the caller, never inherited from a shape.</summary>
    private static GameObject BuildPrimitive(string nameHint, float scale)
    {
        var root = new GameObject("CC_" + nameHint);
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "part";

        Collider inherited = body.GetComponent<Collider>();
        if (inherited != null)
        {
            UnityEngine.Object.DestroyImmediate(inherited);
        }

        body.transform.SetParent(root.transform, worldPositionStays: false);
        body.transform.localScale = new Vector3(0.22f, 0.04f, 0.22f);
        root.transform.localScale = Vector3.one * scale;
        return root;
    }
}
