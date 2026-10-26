using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>Original box-mesh rails and rungs, with loaded game materials.
/// Only inactive CLONES are edited. All persisted identity belongs to these
/// three prefabs, never to a changed vanilla prefab or a renamed old product.</summary>
internal sealed class LadderPieceRegistration : IDisposable
{
    private readonly Action<string> _log;
    private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.Ordinal);
    private bool _subscribed;
    private bool _failed;

    internal bool IsReady => !_failed && _registered.Count == 3;

    internal LadderPieceRegistration(Action<string> log) => _log = log;

    internal void Install()
    {
        if (_subscribed || _failed || IsReady) return;
        LadderLocalization.Register();
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        _subscribed = true;
    }

    internal void Register()
    {
        // Stable IDs are a save contract. Names and recipes may change in a
        // later version, but never change these identifiers after placement.
        Add("tcc_ladder_wood", "woodwall", "woodwall", "woodwall", 200f,
            "$cl_ladder_wood", "piece_workbench", new[] { new RequirementConfig("Wood", 4, 0, true) });
        Add("tcc_ladder_copper", "woodiron_pole", "Copper", "copper", 400f,
            "$cl_ladder_copper", "forge", new[] { new RequirementConfig("Copper", 2, 0, true), new RequirementConfig("Wood", 1, 0, true) });
        Add("tcc_ladder_iron", "woodiron_pole", "woodiron_pole", "Ironbeam_mat", 800f,
            "$cl_ladder_iron", "forge", new[] { new RequirementConfig("Iron", 2, 0, true), new RequirementConfig("Wood", 1, 0, true) });
        if (_registered.Count == 3) Dispose();
    }

    private void Add(string id, string donorName, string materialPrefab, string materialName,
        float health, string token, string station, RequirementConfig[] cost)
    {
        if (_failed || _registered.Contains(id)) return;
        GameObject? prefab = null;
        try
        {
            var config = new PieceConfig
            {
                Name = token,
                Description = "$cl_ladder_description",
                PieceTable = "Hammer",
                Category = "Building",
                // These names are public Jotunn 2.30.2 usage tags. Explicitly
                // annotate the new menu; do not use obsolete table categories.
                Usage = new[] { "Building", "Stairs" },
                CraftingStation = station,
                Requirements = cost
            };
            var custom = new CustomPiece(id, donorName, config);
            prefab = custom.PiecePrefab;
            if (prefab == null || prefab.activeInHierarchy)
                throw new InvalidOperationException("Expected an inactive-parent custom prefab clone.");
            var piece = prefab.GetComponent<Piece>();
            var wear = prefab.GetComponent<WearNTear>();
            var view = prefab.GetComponent<ZNetView>();
            if (piece == null || wear == null || view == null)
                throw new InvalidOperationException("The structural donor is missing Piece, WearNTear or ZNetView.");

            Material material = FindMaterial(materialPrefab, materialName);
            // DestroyImmediate is confined to the inactive prefab clone. A
            // deferred Destroy would let RenderManager see the donor geometry.
            foreach (var lod in prefab.GetComponentsInChildren<LODGroup>(true))
                UnityEngine.Object.DestroyImmediate(lod);
            foreach (var collider in prefab.GetComponents<Collider>())
                UnityEngine.Object.DestroyImmediate(collider);
            for (int i = prefab.transform.childCount - 1; i >= 0; --i)
                UnityEngine.Object.DestroyImmediate(prefab.transform.GetChild(i).gameObject);
            foreach (var renderer in prefab.GetComponents<Renderer>())
                UnityEngine.Object.DestroyImmediate(renderer);
            foreach (var filter in prefab.GetComponents<MeshFilter>())
                UnityEngine.Object.DestroyImmediate(filter);

            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) throw new InvalidOperationException("The game's piece collision layer is absent.");
            prefab.layer = layer;
            prefab.transform.localScale = Vector3.one;
            wear.m_health = health;
            // Both metal variants intentionally inherit the iron-pole support
            // class. No support bypass, no roof-wear override, no zero-cost path.
            wear.m_new = Visual(prefab.transform, "new", material, 1f, layer);
            wear.m_worn = Visual(prefab.transform, "worn", material, 0.82f, layer);
            wear.m_broken = Visual(prefab.transform, "broken", material, 0.65f, layer);
            // These are real serialized WearNTear fields in the installed game.
            // No references into the removed donor hierarchy may survive on the clone.
            wear.m_wet = null;
            wear.m_snow = null;
            wear.m_snowWorn = null;
            wear.m_snowBroken = null;
            wear.m_nonSolidRenderers = new List<Renderer>();
            wear.m_fragmentRoots = new[] { wear.m_new };
            wear.m_worn.SetActive(false);
            wear.m_broken.SetActive(false);

            // Colliders survive damage-state visibility changes. They also
            // provide the native support/hammer interaction geometry. The
            // collider lives on a child, as on native walls: the placement
            // ghost's "blocked by player" test skips colliders on the ghost's
            // root object, so a root collider let a ladder be placed into a
            // character (observed in game).
            var collision = new GameObject("collision");
            collision.layer = layer;
            collision.transform.SetParent(prefab.transform, false);
            BoxCollider body = collision.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 1f, 0.16f);
            body.size = new Vector3(0.8f, 2f, 0.16f);

            // The native Cycle Snap message prints "$msg_snapping " + the snap point's name through the
            // localizer: the game's own Top/Bottom tokens. Positions are the unchanged placement contract.
            Snap(prefab.transform, "$hud_snappoint_bottom", Vector3.zero, layer);
            Snap(prefab.transform, "$hud_snappoint_top", new Vector3(0f, 2f, 0f), layer);
            // Only the end-to-end centre points. Owner decision 2026-10-26: corner modes are out of scope; floors and
            // walls are handled by centring on their edges (LadderSnapAlignment / LadderEdgeSnap), the way walls snap
            // to floors. Former corner points also cross-matched other ladders and floors (reproduced in game).

            LadderSegment segment = prefab.AddComponent<LadderSegment>();
            segment.LocalBottom = Vector3.zero;
            segment.LocalTop = new Vector3(0f, 2f, 0f);
            segment.LocalOutward = Vector3.forward;
            segment.Width = 0.8f;
            segment.RungPitch = 1f / 3f;
            segment.DisplayName = token;
            segment.FrontFaceDepth = body.center.z + body.size.z / 2f;
            segment.BackFaceDepth = body.center.z - body.size.z / 2f;

            // Dedicated servers cannot render icons. Keep the donor's existing
            // icon there; render an exact ladder thumbnail on graphical clients.
            if (!Application.isBatchMode)
            {
                try
                {
                    Sprite rendered = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
                    if (rendered == null || rendered.texture == null ||
                        rendered.rect.width <= 0f || rendered.rect.height <= 0f)
                        throw new InvalidOperationException("Jotunn returned no usable ladder icon.");
                    piece.m_icon = rendered;
                }
                catch (Exception exception)
                {
                    // Icon cache/graphics failure must not remove a stable
                    // world prefab. Keep the donor icon, report it, and fail
                    // the graphical menu acceptance row until repaired.
                    _log("Ladder icon fallback for " + id + ": " + SafeFailure.Brief(exception) +
                        ". The piece remains registered; verify the hammer icon before release.");
                }
            }
            if (!PieceManager.Instance.AddPiece(custom))
                throw new InvalidOperationException("Jotunn rejected the custom piece registration.");
            _registered.Add(id);
            _log("Registered Concerned Ladders prefab " + id + ".");
        }
        catch (Exception exception)
        {
            // Jotunn's AssetManager remembers a clone before AddPiece. Retrying
            // its stable ID after a partial edit would return an existing,
            // incomplete asset. Fail this session explicitly; restart after
            // repair instead of registering a different ID or claiming success.
            _failed = true;
            Dispose();
            _log("Concerned Ladders registration FAILED for " + id + ": " + SafeFailure.Brief(exception) +
                ". Restart required after repair. Do not open or save a world containing this mod until all three pieces register.");
        }
    }

    private static Material FindMaterial(string prefabName, string materialName)
    {
        GameObject source = PrefabManager.Instance.GetPrefab(prefabName);
        if (source == null) throw new InvalidOperationException("Material donor prefab unavailable: " + prefabName);
        Material? original = source.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(candidate => candidate != null &&
                (candidate.name == materialName || candidate.name == materialName + " (Instance)"));
        if (original == null)
            throw new InvalidOperationException("Named material unavailable: " + materialName + " on " + prefabName);
        // Borrow read-only here. Visual creates exactly one private copy for
        // each durability state, so no unused intermediate Material leaks.
        return original;
    }

    private static GameObject Visual(Transform parent, string name, Material basis, float shade, int layer)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.layer = layer;
        var material = new Material(basis) { name = basis.name + "_" + name };
        if (material.HasProperty("_Color")) material.color *= new Color(shade, shade, shade, 1f);
        Bar(root.transform, "left_rail", new Vector3(-0.34f, 1f, 0.16f), new Vector3(0.12f, 2f, 0.14f), material, layer);
        Bar(root.transform, "right_rail", new Vector3(0.34f, 1f, 0.16f), new Vector3(0.12f, 2f, 0.14f), material, layer);
        for (int i = 0; i < RungShape.PerSection; ++i)
            Bar(root.transform, "rung_" + i, new Vector3(0f, (i + 0.5f) / 3f, RungShape.Plane),
                new Vector3(2f * RungShape.HalfWidth, 2f * RungShape.HalfHeight, 2f * RungShape.HalfDepth), material, layer);
        return root;
    }

    private static void Bar(Transform parent, string name, Vector3 centre, Vector3 size, Material material, int layer)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.transform.SetParent(parent, false);
        bar.name = name;
        bar.layer = layer;
        bar.transform.localPosition = centre;
        bar.transform.localScale = size;
        UnityEngine.Object.DestroyImmediate(bar.GetComponent<Collider>());
        bar.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Snap(Transform parent, string name, Vector3 position, int layer)
    {
        var point = new GameObject(name) { tag = "snappoint", layer = layer };
        point.transform.SetParent(parent, false);
        point.transform.localPosition = position;
    }

    public void Dispose()
    {
        if (!_subscribed) return;
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
        _subscribed = false;
    }
}
