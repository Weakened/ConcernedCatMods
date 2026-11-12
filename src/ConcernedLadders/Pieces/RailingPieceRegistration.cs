using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Railings;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>The nine railings (#556): each a Jötunn clone of its tier's vanilla donor (the ladders' own: <c>woodwall</c>
/// for wood, <c>woodiron_pole</c> for copper and iron), so its health class, support, damage, repair and refunds stay
/// native. The donor's geometry is stripped from the inactive clone and replaced by the railing's one procedural mesh
/// (<see cref="RailingMesh"/>) in the tier's borrowed material, tinted as the ladder of that tier is, in each wear state; a
/// thin box collider along the slope; and bottom and top snap points on the base line, spaced as the game's stairs' side
/// snaps are, so a railing snaps along a stair's edge, a floor's edge or another railing. Installed only when
/// <see cref="RailingSettings"/> is on (off by default). No RPC, ZDO field or ownership claim is added; no native prefab is
/// changed. Not observed in game.</summary>
internal sealed class RailingPieceRegistration : IDisposable
{
    private readonly Action<string> _log;
    private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _refused = new List<string>();
    private bool _subscribed;
    private bool _attempted;

    internal RailingPieceRegistration(Action<string> log) => _log = log;

    internal int RegisteredCount => _registered.Count;

    internal void Install()
    {
        if (_subscribed || _attempted) return;
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", RailingFamilies.English());
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        _subscribed = true;
    }

    internal void Register()
    {
        // One attempt per session: Jötunn remembers a clone before AddPiece, so a retry under the same stable ID would
        // find it unfinished.
        if (_attempted) return;
        _attempted = true;
        Dispose();
        foreach (RailingPiece railing in RailingFamilies.All) Add(railing);
        _log("Concerned Ladders railings: " + _registered.Count + " of " + RailingFamilies.All.Count + " registered" +
             (_refused.Count == 0 ? "." : "; refused " + string.Join(", ", _refused) + "."));
    }

    private void Add(RailingPiece railing)
    {
        try
        {
            var config = new PieceConfig
            {
                Name = "$" + railing.Token,
                Description = "$" + RailingFamilies.DescriptionToken,
                PieceTable = "Hammer",
                Category = "Building",
                // Public Jötunn 2.30.2 usage tags, as the ladders use.
                Usage = new[] { "Building", "Stairs" },
                CraftingStation = railing.Station,
                Requirements = railing.Costs.Select(cost => new RequirementConfig(cost.Item, cost.Amount, 0, true)).ToArray(),
            };
            var custom = new CustomPiece(railing.Prefab, railing.Donor, config);
            GameObject prefab = custom.PiecePrefab;
            if (prefab == null || prefab.activeInHierarchy)
                throw new InvalidOperationException("Expected an inactive-parent custom prefab clone.");
            Piece piece = prefab.GetComponent<Piece>();
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (piece == null || wear == null || prefab.GetComponent<ZNetView>() == null)
                throw new InvalidOperationException("The structural donor is missing Piece, WearNTear or ZNetView.");

            Material basis = FindMaterial(railing.MaterialPrefab, railing.Material);
            // DestroyImmediate is confined to the inactive prefab clone, so RenderManager never sees donor geometry.
            foreach (LODGroup lod in prefab.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lod);
            foreach (Collider collider in prefab.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
            for (int i = prefab.transform.childCount - 1; i >= 0; --i) UnityEngine.Object.DestroyImmediate(prefab.transform.GetChild(i).gameObject);
            foreach (Renderer renderer in prefab.GetComponents<Renderer>()) UnityEngine.Object.DestroyImmediate(renderer);
            foreach (MeshFilter filter in prefab.GetComponents<MeshFilter>()) UnityEngine.Object.DestroyImmediate(filter);

            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) throw new InvalidOperationException("The game's piece collision layer is absent.");
            prefab.layer = layer;
            prefab.transform.localScale = Vector3.one;
            wear.m_health = railing.Health;

            Mesh mesh = DoorPieceKit.UnityMesh("railing_" + railing.Tier + "_" + railing.Angle,
                () => RailingMesh.Build(railing.Prefab, RailingDesign.Parts(railing.Tier, railing.Angle)));
            var tint = new Color(railing.Tint.R, railing.Tint.G, railing.Tint.B, 1f);
            wear.m_new = Visual(prefab.transform, "new", mesh, basis, tint, 1f, layer);
            wear.m_worn = Visual(prefab.transform, "worn", mesh, basis, tint, 0.82f, layer);
            wear.m_broken = Visual(prefab.transform, "broken", mesh, basis, tint, 0.65f, layer);
            // No references into the removed donor hierarchy may survive on the clone.
            wear.m_wet = null;
            wear.m_snow = null;
            wear.m_snowWorn = null;
            wear.m_snowBroken = null;
            wear.m_nonSolidRenderers = new List<Renderer>();
            wear.m_fragmentRoots = new[] { wear.m_new };
            wear.m_comOffset = Vector3.zero;
            wear.m_worn.SetActive(false);
            wear.m_broken.SetActive(false);

            // One thin box along the slope, on its own child (the placement ghost's blocked-by-player test skips the
            // ghost root's colliders): what keeps a player from walking off the stair's side, and the support and hammer
            // geometry WearNTear measures with.
            var collision = new GameObject("collision") { layer = layer };
            collision.transform.SetParent(prefab.transform, false);
            collision.transform.localRotation = Quaternion.Euler(-RailingGeometry.Degrees(railing.Angle), 0f, 0f);
            (Point3 centre, Point3 size) = RailingGeometry.Collider(railing.Angle);
            BoxCollider box = collision.AddComponent<BoxCollider>();
            box.center = new Vector3(centre.X, centre.Y, centre.Z);
            box.size = new Vector3(size.X, size.Y, size.Z);

            Snap(prefab.transform, "$hud_snappoint_bottom", RailingGeometry.BottomSnap, layer);
            Snap(prefab.transform, "$hud_snappoint_top", RailingGeometry.TopSnap(railing.Angle), layer);

            DoorPieceKit.Icon(prefab, piece, railing.Prefab, "railing", _log);
            if (!PieceManager.Instance.AddPiece(custom))
                throw new InvalidOperationException("Jotunn rejected the custom piece registration.");
            _registered.Add(railing.Prefab);
            _log("Registered Concerned Ladders prefab " + railing.Prefab + " from " + railing.Donor + ".");
        }
        catch (Exception exception)
        {
            _refused.Add(railing.Prefab);
            _log("Concerned Ladders railing " + railing.Prefab + " NOT registered: " + SafeFailure.Brief(exception) +
                 ". Placed " + railing.English + "s are deleted by the game if a world is loaded in this session; restart after repair.");
        }
    }

    /// <summary>One wear state's presentation: the railing mesh in a tinted, shaded copy of the borrowed material (the
    /// shared native material is never edited), with no collider.</summary>
    private static GameObject Visual(Transform parent, string name, Mesh mesh, Material basis, Color tint, float shade, int layer)
    {
        var root = new GameObject(name) { layer = layer };
        root.transform.SetParent(parent, false);
        var material = new Material(basis) { name = basis.name + "_tcc_railing_" + name };
        if (material.HasProperty("_Color")) material.color = material.color * new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        return root;
    }

    private static void Snap(Transform parent, string name, Point3 position, int layer)
    {
        var point = new GameObject(name) { tag = "snappoint", layer = layer };
        point.transform.SetParent(parent, false);
        point.transform.localPosition = new Vector3(position.X, position.Y, position.Z);
    }

    private static Material FindMaterial(string prefabName, string materialName)
    {
        GameObject source = PrefabManager.Instance.GetPrefab(prefabName);
        if (source == null) throw new InvalidOperationException("Material donor prefab unavailable: " + prefabName);
        Material? found = source.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(candidate => candidate != null && (candidate.name == materialName || candidate.name == materialName + " (Instance)"));
        return found ?? throw new InvalidOperationException("Donor material unavailable: " + materialName + " on " + prefabName);
    }

    public void Dispose()
    {
        if (!_subscribed) return;
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
        _subscribed = false;
    }
}
