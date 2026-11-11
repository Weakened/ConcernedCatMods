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
using UnityEngine.Experimental.Rendering;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>The earth block (#577): one networked piece prefab, <see cref="EarthFill.Prefab"/>, that a dig places in the
/// sloping cells around a hole. Never in a build menu. A 1 m square column standing on its origin, one metre tall before
/// it is scaled: the dig scales each block to its height, and the game saves and sends that scale with the block
/// (<c>ZNetView.m_syncInitialScale</c>, read from the installed game). It has no health and no cost: the hammer removes it
/// through the game's own path for a piece without WearNTear, which drops its (empty) resources and destroys it. Its body
/// is drawn in a copy of the vanilla mud pile's material, whose shader maps the texture by world position and so does not
/// stretch with the scale. That shader draws the generated box's upward faces black (#583, owner playtest), so the thin
/// top is drawn in a copy of the vanilla wood wall's UV-mapped piece material instead, the one the doors and railings use
/// on generated meshes. Once a world is loaded both take the terrain's own cliff rock (#579, #583,
/// <see cref="ApplyTerrainLook"/>), so the block reads as the rock of a dug hole's side, top included. Eight corner snap points let the game's own walls and floors snap to its faces.
///
/// <para>Registered whenever excavation is on, whatever the earth fill setting says, so a player who does not fill still
/// sees the blocks another player's dig placed. No RPC and no ZDO field of its own; no vanilla prefab is changed. Not
/// observed in game.</para></summary>
internal sealed class EarthFillPieceRegistration : IDisposable
{
    private const string MaterialPrefab = "mudpile";
    private const string MaterialName = "mudpile";

    /// <summary>The top's UV-mapped piece material (#583): the vanilla wood wall's, its texture replaced.</summary>
    private const string TopMaterialPrefab = "woodwall";
    private const string TopMaterialName = "woodwall";

    private readonly Action<string> _log;
    private bool _subscribed;
    private bool _attempted;
    private Material? _side;
    private Material? _top;
    private bool _looked;
    private float _nextLook;

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
            Material material = FindMaterial(MaterialPrefab, MaterialName);
            _side = new Material(material) { name = "tcc_earth_fill_side" };
            _top = new Material(FindMaterial(TopMaterialPrefab, TopMaterialName)) { name = "tcc_earth_fill_top" };
            DressTop(_top, material.mainTexture, null);
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
            float body = 1f - EarthFill.TopShare;
            var visual = new GameObject("earth") { layer = layer };
            visual.transform.SetParent(prefab.transform, false);
            DoorPieceKit.VisualBox(visual.transform, "body", new ClimbPoint(0f, body / 2f, 0f), new ClimbPoint(1f, body, 1f), _side, UvRegion.Tiling, layer);
            DoorPieceKit.VisualBox(visual.transform, "top", new ClimbPoint(0f, body + EarthFill.TopShare / 2f, 0f), new ClimbPoint(1f, EarthFill.TopShare, 1f),
                _top, UvRegion.Tiling, layer);
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

    /// <summary>Gives the blocks the terrain's own look (#579, #583): copies the cliff slice of the live terrain material's
    /// diffuse array (<see cref="EarthFill.TerrainCliffSlice"/>) into a plain texture on the GPU and gives it, with the
    /// terrain's cliff normal map, to the blocks' two materials, which every block shares. Asked every frame by the plugin, it looks at most every two seconds until a world with terrain is loaded,
    /// and succeeds or gives up once; until then, or if the terrain does not match, the blocks keep the mud pile's
    /// look.</summary>
    internal void ApplyTerrainLook()
    {
        if (_looked || _side == null || _top == null) return;
        Player player = Player.m_localPlayer;
        if (player == null || Time.realtimeSinceStartup < _nextLook) return;
        _nextLook = Time.realtimeSinceStartup + 2f;
        try
        {
            Heightmap? ground = Heightmap.FindHeightmap(player.transform.position);
            Material? terrain = ground != null ? ground.m_material : null;
            if (terrain == null) return;
            _looked = true;
            if (SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None)
                throw new InvalidOperationException("this GPU cannot copy textures");
            if (!(terrain.GetTexture("_DiffuseArrayTex") is Texture2DArray array) || array.depth <= EarthFill.TerrainCliffSlice)
                throw new InvalidOperationException("the terrain has no diffuse array with the expected slice");
            float scale = terrain.HasProperty("_UVScale") ? terrain.GetFloat("_UVScale") : 0.5f;
            Texture2D cliff = Slice(array, EarthFill.TerrainCliffSlice, "tcc_earth_fill_cliff");
            Texture? normal = terrain.GetTexture("_CliffNormal");
            Dress(_side, cliff, normal, scale);
            DressTop(_top, cliff, normal);
            _log("Earth fill wears the terrain's cliff rock (slice " + EarthFill.TerrainCliffSlice + " of " + array.name + ", " + array.width + " px).");
        }
        catch (Exception exception)
        {
            // Once only: a fault here never reaches the plugin's frame, and the blocks keep the look they have.
            _looked = true;
            _log("Earth fill keeps the mud pile's look: " + SafeFailure.Brief(exception) + ".");
        }
    }

    private static Texture2D Slice(Texture2DArray array, int slice, string name)
    {
        int mips = Math.Max(1, array.mipmapCount);
        var texture = new Texture2D(array.width, array.height, array.graphicsFormat, mips, mips > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = array.filterMode,
            anisoLevel = array.anisoLevel,
        };
        for (int mip = 0; mip < mips; mip++) Graphics.CopyTexture(array, slice, mip, texture, 0, mip);
        return texture;
    }

    private static void Dress(Material material, Texture2D diffuse, Texture? normal, float scale)
    {
        material.mainTexture = diffuse;
        material.mainTextureScale = Vector2.one;
        material.color = Color.white;
        if (material.HasProperty("_UVScale")) material.SetFloat("_UVScale", scale);
        if (normal != null && material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", normal);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f);
    }

    /// <summary>The top's texture on its UV-mapped material: one whole tile per metre, as the generated mesh's UVs run one
    /// unit per <see cref="UvRegion.UvMetres"/>, so neighbouring tops join without a seam.</summary>
    private static void DressTop(Material material, Texture? diffuse, Texture? normal)
    {
        material.mainTexture = diffuse;
        material.mainTextureScale = new Vector2(UvRegion.UvMetres, UvRegion.UvMetres);
        material.color = Color.white;
        if (material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", normal);
            if (normal == null) material.DisableKeyword("_NORMALMAP");
            else material.EnableKeyword("_NORMALMAP");
        }
    }

    private static Material FindMaterial(string prefabName, string materialName)
    {
        GameObject source = PrefabManager.Instance.GetPrefab(prefabName) ?? throw new InvalidOperationException("Material donor prefab unavailable: " + prefabName);
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
