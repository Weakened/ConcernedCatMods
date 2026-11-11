using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>The earth block (#577): one networked piece prefab, <see cref="EarthFill.Prefab"/>, that a dig places in the
/// sloping cells around a hole. Never in a build menu. A 1 m square column standing on its origin, one metre tall before
/// it is scaled: the dig scales each block to its height, and the game saves and sends that scale with the block
/// (<c>ZNetView.m_syncInitialScale</c>, read from the installed game). It has no health and no cost: the hammer removes it
/// through the game's own path for a piece without WearNTear, which drops its (empty) resources and destroys it. It wears
/// the vanilla mud pile's own material, which is mapped by world position and so does not stretch with the scale. Eight
/// corner snap points let the game's own walls and floors snap to its faces.
///
/// <para>Registered whenever excavation is on, whatever the earth fill setting says, so a player who does not fill still
/// sees the blocks another player's dig placed. No RPC and no ZDO field of its own; no vanilla prefab is changed. Not
/// observed in game.</para></summary>
internal sealed class EarthFillPieceRegistration : IDisposable
{
    private const string MaterialPrefab = "mudpile";
    private const string MaterialName = "mudpile";

    private readonly Action<string> _log;
    private bool _subscribed;
    private bool _attempted;

    internal EarthFillPieceRegistration(Action<string> log) => _log = log;

    /// <summary>The prefab, once registered; null before, or if registration failed.</summary>
    internal GameObject? Prefab { get; private set; }

    internal void Install()
    {
        if (_subscribed || _attempted) return;
        LocalizationManager.Instance.GetLocalization().AddTranslation("English",
            new Dictionary<string, string>(StringComparer.Ordinal) { [EarthFill.NameToken] = EarthFill.EnglishName });
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        _subscribed = true;
    }

    private void Register()
    {
        if (_attempted) return;
        _attempted = true;
        Dispose();
        try
        {
            Material material = FindMaterial();
            GameObject prefab = PrefabManager.Instance.CreateEmptyPrefab(EarthFill.Prefab, true);
            if (prefab == null || prefab.activeInHierarchy) throw new InvalidOperationException("Expected an inactive-parent empty prefab.");
            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) throw new InvalidOperationException("The game's piece collision layer is absent.");
            prefab.layer = layer;
            foreach (Collider collider in prefab.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Renderer renderer in prefab.GetComponents<Renderer>()) UnityEngine.Object.DestroyImmediate(renderer);
            foreach (MeshFilter filter in prefab.GetComponents<MeshFilter>()) UnityEngine.Object.DestroyImmediate(filter);

            ZNetView view = prefab.GetComponent<ZNetView>() ?? throw new InvalidOperationException("The empty prefab has no ZNetView.");
            view.m_persistent = true;
            view.m_syncInitialScale = true;
            view.m_type = ZDO.ObjectType.Default;

            Piece piece = prefab.AddComponent<Piece>();
            piece.m_name = "$" + EarthFill.NameToken;
            piece.m_description = string.Empty;
            piece.m_canBeRemoved = true;
            piece.m_resources = Array.Empty<Piece.Requirement>();
            piece.m_craftingStation = null;
            piece.m_primaryTarget = false;
            piece.m_randomTarget = false;
            piece.m_targetNonPlayerBuilt = false;

            var centre = new ClimbPoint(0f, 0.5f, 0f);
            var size = new ClimbPoint(1f, 1f, 1f);
            var visual = new GameObject("earth") { layer = layer };
            visual.transform.SetParent(prefab.transform, false);
            DoorPieceKit.VisualBox(visual.transform, "block", centre, size, material, UvRegion.Tiling, layer);
            DoorPieceKit.ColliderBox(prefab.transform, "collision", centre, size, layer);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var point = new GameObject("$hud_snappoint_corner") { tag = "snappoint", layer = layer };
                        point.transform.SetParent(prefab.transform, false);
                        point.transform.localPosition = new Vector3(x * 0.5f, y, z * 0.5f);
                    }
                }
            }

            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            Prefab = prefab;
            _log("Registered Concerned Ladders prefab " + EarthFill.Prefab + " (earth fill, " + material.name + ").");
        }
        catch (Exception exception)
        {
            Prefab = null;
            _log("Concerned Ladders earth fill NOT registered: " + SafeFailure.Brief(exception) +
                 ". Digs leave their sloping sides; placed earth blocks are deleted by the game if a world is loaded in this session.");
        }
    }

    private static Material FindMaterial()
    {
        GameObject source = PrefabManager.Instance.GetPrefab(MaterialPrefab) ?? throw new InvalidOperationException("Material donor prefab unavailable: " + MaterialPrefab);
        Material? found = source.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(candidate => candidate != null && (candidate.name == MaterialName || candidate.name == MaterialName + " (Instance)"));
        return found ?? throw new InvalidOperationException("Donor material unavailable: " + MaterialName + " on " + MaterialPrefab);
    }

    public void Dispose()
    {
        if (!_subscribed) return;
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
        _subscribed = false;
    }
}
