using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Basement;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Railings;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>The basement wall pieces (#568): each a Jötunn clone of its material's vanilla wall (<c>woodwall</c> or
/// <c>stone_wall_2x1</c>), so its health class, support, damage, repair and refunds stay native. The donor's geometry is
/// stripped from the inactive clone and replaced by the piece's boxes (<see cref="BasementPiece.Parts"/>) in the donor's own
/// material, a darker copy for the worn states, with a box collider each and the piece's snap points. Installed only when
/// <see cref="BasementSettings"/> is on (off by default). No RPC, ZDO field or ownership claim is added; no native prefab is
/// changed. Not observed in game.</summary>
internal sealed class BasementPieceRegistration : IDisposable
{
    private readonly Action<string> _log;
    private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _refused = new List<string>();
    private bool _subscribed;
    private bool _attempted;

    internal BasementPieceRegistration(Action<string> log) => _log = log;

    internal int RegisteredCount => _registered.Count;

    internal void Install()
    {
        if (_subscribed || _attempted) return;
        LocalizationManager.Instance.GetLocalization().AddTranslation("English", BasementWalls.English());
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        _subscribed = true;
    }

    internal void Register()
    {
        if (_attempted) return;
        _attempted = true;
        Dispose();
        foreach (BasementPiece piece in BasementWalls.All) Add(piece);
        _log("Concerned Ladders basement walls: " + _registered.Count + " of " + BasementWalls.All.Count + " registered" +
             (_refused.Count == 0 ? "." : "; refused " + string.Join(", ", _refused) + "."));
    }

    private void Add(BasementPiece piece)
    {
        try
        {
            var config = new PieceConfig
            {
                Name = "$" + piece.Token,
                Description = "$" + BasementWalls.DescriptionToken,
                PieceTable = "Hammer",
                Category = "Building",
                Usage = new[] { "Building" },
                CraftingStation = piece.Station,
                Requirements = new[] { new RequirementConfig(piece.Item, piece.Amount, 0, true) },
            };
            var custom = new CustomPiece(piece.Prefab, piece.Donor, config);
            GameObject prefab = custom.PiecePrefab;
            if (prefab == null || prefab.activeInHierarchy)
                throw new InvalidOperationException("Expected an inactive-parent custom prefab clone.");
            Piece native = prefab.GetComponent<Piece>();
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (native == null || wear == null || prefab.GetComponent<ZNetView>() == null)
                throw new InvalidOperationException("The structural donor is missing Piece, WearNTear or ZNetView.");

            Material basis = FindMaterial(piece.MaterialPrefab, piece.MaterialName);
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

            wear.m_new = Visual(prefab.transform, "new", piece, basis, 1f, layer);
            wear.m_worn = Visual(prefab.transform, "worn", piece, basis, 0.82f, layer);
            wear.m_broken = Visual(prefab.transform, "broken", piece, basis, 0.65f, layer);
            wear.m_wet = null;
            wear.m_snow = null;
            wear.m_snowWorn = null;
            wear.m_snowBroken = null;
            wear.m_nonSolidRenderers = new List<Renderer>();
            wear.m_fragmentRoots = new[] { wear.m_new };
            wear.m_comOffset = Vector3.zero;
            wear.m_worn.SetActive(false);
            wear.m_broken.SetActive(false);

            // One box collider per part, each on its own child (the placement ghost's blocked-by-player test skips the
            // ghost root's colliders): the support and hammer geometry WearNTear measures with.
            foreach (BasementPart part in piece.Parts())
                DoorPieceKit.ColliderBox(prefab.transform, "collision_" + part.Name, Climb(part.Centre), Climb(part.Size), layer);
            foreach (Point3 snap in piece.Snaps())
            {
                var point = new GameObject("$hud_snappoint_corner") { tag = "snappoint", layer = layer };
                point.transform.SetParent(prefab.transform, false);
                point.transform.localPosition = new Vector3(snap.X, snap.Y, snap.Z);
            }

            DoorPieceKit.Icon(prefab, native, piece.Prefab, "basement wall", _log);
            if (!PieceManager.Instance.AddPiece(custom))
                throw new InvalidOperationException("Jotunn rejected the custom piece registration.");
            _registered.Add(piece.Prefab);
            _log("Registered Concerned Ladders prefab " + piece.Prefab + " from " + piece.Donor + ".");
        }
        catch (Exception exception)
        {
            _refused.Add(piece.Prefab);
            _log("Concerned Ladders basement piece " + piece.Prefab + " NOT registered: " + SafeFailure.Brief(exception) +
                 ". Placed " + piece.English + "s are deleted by the game if a world is loaded in this session; restart after repair.");
        }
    }

    /// <summary>One wear state: the piece's boxes in a shaded copy of the donor's material (never edited itself), no collider.</summary>
    private static GameObject Visual(Transform parent, string name, BasementPiece piece, Material basis, float shade, int layer)
    {
        var root = new GameObject(name) { layer = layer };
        root.transform.SetParent(parent, false);
        var material = new Material(basis) { name = basis.name + "_tcc_basement_" + name };
        if (material.HasProperty("_Color")) material.color = material.color * new Color(shade, shade, shade, 1f);
        foreach (BasementPart part in piece.Parts())
            DoorPieceKit.VisualBox(root.transform, part.Name, Climb(part.Centre), Climb(part.Size), material, UvRegion.Tiling, layer);
        return root;
    }

    private static ClimbPoint Climb(Point3 point) => new ClimbPoint(point.X, point.Y, point.Z);

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
