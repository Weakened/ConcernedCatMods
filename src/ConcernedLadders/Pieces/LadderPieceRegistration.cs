using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Families;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>Original rails and rungs with loaded game materials: box meshes for wood, procedural slim metal rails and
/// round rungs for copper and iron (<see cref="LadderStyle"/>). Only inactive CLONES are edited. All persisted
/// identity belongs to these three prefabs, never to a changed vanilla prefab or a renamed old product.</summary>
internal sealed class LadderPieceRegistration : IDisposable
{
    private readonly Action<string> _log;
    private readonly bool _sideModes;
    private readonly bool _families;
    private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
    private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.Ordinal);
    private bool _subscribed;
    private bool _failed;

    /// <summary>The three shipped ladders are registered. The optional family ladders (#558) never count: with them the
    /// set is larger, and one of them failing must not stop climbing.</summary>
    internal bool IsReady => !_failed && Array.TrueForAll(Shipped, _registered.Contains);

    /// <summary>The ladders 1.0.0 shipped: a save contract.</summary>
    internal static readonly string[] Shipped = { "tcc_ladder_wood", "tcc_ladder_copper", "tcc_ladder_iron" };

    /// <param name="log">Startup log.</param>
    /// <param name="sideModes">Add the Left side / Right side snap points. Only when the placement adapter is
    /// installed: the game alone would put a side-mode ghost 100 m above the aimed point (see
    /// <see cref="LadderSnapPoints"/>).</param>
    /// <param name="families">Also register the ladders of the vanilla building families (#558, <see cref="LadderFamilySettings"/>,
    /// off by default). They are optional: one that fails is skipped and logged, and never stops the three shipped
    /// ladders.</param>
    internal LadderPieceRegistration(Action<string> log, bool sideModes, bool families = false)
    {
        _log = log;
        _sideModes = sideModes;
        _families = families;
    }

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
        // Tints multiply the borrowed material's colour: none for wood (unchanged), warm for copper, cool and a little
        // darker for iron, so the two metals read apart even where their textures are similar.
        Add("tcc_ladder_wood", "woodwall", "woodwall", "woodwall", 200f,
            "$cl_ladder_wood", "piece_workbench", new[] { new RequirementConfig("Wood", 4, 0, true) },
            LadderStyle.Wood, Color.white);
        Add("tcc_ladder_copper", "woodiron_pole", "Copper", "copper", 400f,
            "$cl_ladder_copper", "forge", new[] { new RequirementConfig("Copper", 2, 0, true), new RequirementConfig("Wood", 1, 0, true) },
            LadderStyle.Copper, new Color(1f, 0.84f, 0.7f, 1f));
        Add("tcc_ladder_iron", "woodiron_pole", "woodiron_pole", "Ironbeam_mat", 800f,
            "$cl_ladder_iron", "forge", new[] { new RequirementConfig("Iron", 2, 0, true), new RequirementConfig("Wood", 1, 0, true) },
            LadderStyle.Iron, new Color(0.8f, 0.84f, 0.9f, 1f), BuildFamilies.Of(BuildFamily.Iron).Region);
        if (_families && !_failed)
        {
            // The vanilla families (#558): each the game's own pole of its material, keeping its recipe, station and
            // health, sampling its texture region. Optional: a family that fails is logged and skipped.
            foreach (BuildFamilyInfo family in BuildFamilies.All)
            {
                if (!family.FollowsDonor) continue;
                AddFamily(family);
            }
        }

        if (IsReady) Dispose();
    }

    /// <summary>One vanilla family's ladder (#558): its donor pole's recipe and station, read from the installed game, its
    /// health left as the donor's, the wood ladder's shape for the woods and the iron ladder's for flametal.</summary>
    private void AddFamily(BuildFamilyInfo family)
    {
        string id = "tcc_ladder_" + family.Word;
        try
        {
            Piece? donor = PrefabManager.Instance.GetPrefab(family.Donor)?.GetComponent<Piece>();
            if (donor == null) throw new InvalidOperationException("Donor piece unavailable: " + family.Donor);
            RequirementConfig[] cost = (donor.m_resources ?? Array.Empty<Piece.Requirement>())
                .Where(entry => entry?.m_resItem != null && entry.m_amount > 0)
                .Select(entry => new RequirementConfig(entry.m_resItem.name, entry.m_amount, 0, entry.m_recover)).ToArray();
            if (cost.Length == 0) throw new InvalidOperationException("The donor has no recipe.");
            string? station = donor.m_craftingStation != null ? donor.m_craftingStation.name : null;
            LadderStyle style = family.Style == RailingStyle.Detailed ? LadderStyle.Iron : LadderStyle.Wood;
            Add(id, family.Donor, family.MaterialPrefab, family.Material, null, "$cl_ladder_" + family.Word, station, cost, style,
                new Color(family.Tint.R, family.Tint.G, family.Tint.B, 1f), family.Region, essential: false);
        }
        catch (Exception exception)
        {
            _log("Concerned Ladders family ladder " + id + " NOT registered: " + SafeFailure.Brief(exception) + ". The other ladders are unaffected.");
        }
    }

    private void Add(string id, string donorName, string materialPrefab, string materialName,
        float? health, string token, string? station, RequirementConfig[] cost, LadderStyle style, Color tint,
        UvRegion region = default, bool essential = true)
    {
        if (_failed || _registered.Contains(id)) return;
        // A default region is the shipped wood and copper ladders' whole-texture mapping.
        if (!region.Tiles && region.U1 <= region.U0) region = UvRegion.Tiling;
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
            if (health.HasValue) wear.m_health = health.Value;
            // Both metal variants intentionally inherit the iron-pole support
            // class. No support bypass, no roof-wear override, no zero-cost path.
            wear.m_new = Visual(prefab.transform, "new", material, tint, 1f, style, region, layer);
            wear.m_worn = Visual(prefab.transform, "worn", material, tint, 0.82f, style, region, layer);
            wear.m_broken = Visual(prefab.transform, "broken", material, tint, 0.65f, style, region, layer);
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
            // localizer: the game's own Top/Bottom tokens, and this product's Left side / Right side. The centre
            // positions are the unchanged placement contract; the side points are mode markers far below the section
            // that the game's snap search can never pair with a floor, a wall or another ladder's centre points
            // (LadderSnapPoints). LadderSnapAlignment owns where a side-mode ghost goes.
            Snap(prefab.transform, "$hud_snappoint_bottom", Vector(LadderSnapPoints.Bottom), layer);
            Snap(prefab.transform, "$hud_snappoint_top", Vector(LadderSnapPoints.Top), layer);
            if (_sideModes)
            {
                Snap(prefab.transform, "$cl_snap_left", Vector(LadderSnapPoints.LeftSide), layer);
                Snap(prefab.transform, "$cl_snap_right", Vector(LadderSnapPoints.RightSide), layer);
            }

            LadderSegment segment = prefab.AddComponent<LadderSegment>();
            segment.LocalBottom = Vector3.zero;
            segment.LocalTop = new Vector3(0f, 2f, 0f);
            segment.LocalOutward = Vector3.forward;
            segment.Width = 0.8f;
            segment.RungPitch = 1f / 3f;
            segment.DisplayName = token;
            segment.FrontFaceDepth = body.center.z + body.size.z / 2f;
            segment.BackFaceDepth = body.center.z - body.size.z / 2f;
            segment.RungHalfHeight = style.RungHalfHeight;
            segment.RungHalfDepth = style.RungHalfDepth;
            segment.RungHalfLength = style.RungHalfLength;
            segment.RungRadius = style.RungRadius;
            segment.RailCentre = style.RailCentre;
            segment.RailWidth = style.RailWidth;

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
        catch (Exception exception) when (!essential)
        {
            // An optional family ladder (#558): logged and skipped; the shipped three are untouched.
            _log("Concerned Ladders family ladder " + id + " NOT registered: " + SafeFailure.Brief(exception) +
                 ". Placed ones are deleted by the game if a world is loaded in this session; restart after repair.");
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

    private GameObject Visual(Transform parent, string name, Material basis, Color tint, float shade, LadderStyle style, UvRegion region, int layer)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.layer = layer;
        var material = new Material(basis) { name = basis.name + "_" + name };
        if (material.HasProperty("_Color")) material.color *= new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
        if (style.RailProfile == LadderProfile.Box && style.RungProfile == LadderProfile.Box)
        {
            // Wood: the original box rails and rungs, unchanged; a family whose texture is an atlas (#558) draws the same
            // boxes as generated meshes sampling its region (#548's rule), never the whole sheet.
            Box(root.transform, "left_rail", new Vector3(-style.RailCentre, 1f, LadderStyle.RailPlane),
                new Vector3(style.RailWidth, LadderEdgeSnap.SectionHeight, style.RailDepth), material, region, layer);
            Box(root.transform, "right_rail", new Vector3(style.RailCentre, 1f, LadderStyle.RailPlane),
                new Vector3(style.RailWidth, LadderEdgeSnap.SectionHeight, style.RailDepth), material, region, layer);
            for (int i = 0; i < RungShape.PerSection; ++i)
                Box(root.transform, "rung_" + i, new Vector3(0f, LadderStyle.RungHeight(i), RungShape.Plane),
                    new Vector3(2f * style.RungHalfLength, 2f * style.RungHalfHeight, 2f * style.RungHalfDepth), material, region, layer);
            return root;
        }

        // Metal: procedural slim rails along y and round rungs along x (the rung mesh is built along y and turned).
        Mesh rail = Extruded(style.RailProfile, style.RailWidth, style.RailDepth, LadderEdgeSnap.SectionHeight, region);
        Mesh rung = Extruded(style.RungProfile, 2f * style.RungHalfHeight, 2f * style.RungHalfDepth, 2f * style.RungHalfLength, region);
        Part(root.transform, "left_rail", rail, new Vector3(-style.RailCentre, 1f, LadderStyle.RailPlane), Quaternion.identity, material, layer);
        Part(root.transform, "right_rail", rail, new Vector3(style.RailCentre, 1f, LadderStyle.RailPlane), Quaternion.identity, material, layer);
        for (int i = 0; i < RungShape.PerSection; ++i)
            Part(root.transform, "rung_" + i, rung, new Vector3(0f, LadderStyle.RungHeight(i), RungShape.Plane),
                Quaternion.Euler(0f, 0f, 90f), material, layer);
        return root;
    }

    /// <summary>A box: the built-in cube for a tiling texture (the shipped ladders, unchanged), or a region-mapped mesh.</summary>
    private static void Box(Transform parent, string name, Vector3 centre, Vector3 size, Material material, UvRegion region, int layer)
    {
        if (region.Tiles)
        {
            Bar(parent, name, centre, size, material, layer);
            return;
        }

        DoorPieceKit.VisualBox(parent, name, new ClimbPoint(centre.x, centre.y, centre.z), new ClimbPoint(size.x, size.y, size.z), material, region, layer);
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

    private static void Part(Transform parent, string name, Mesh mesh, Vector3 centre, Quaternion rotation, Material material, int layer)
    {
        var part = new GameObject(name) { layer = layer };
        part.transform.SetParent(parent, false);
        part.transform.localPosition = centre;
        part.transform.localRotation = rotation;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>A closed prism along local y, centred on the origin: the profile ring (a on x, b on z) swept over
    /// <paramref name="length"/>, with flat end caps. One mesh per shape, shared by every state and section.</summary>
    private Mesh Extruded(LadderProfile profile, float width, float depth, float length, UvRegion region)
    {
        string key = profile + ":" + width.ToString("R") + ":" + depth.ToString("R") + ":" + length.ToString("R") + ":" + region.Key;
        if (_meshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;
        IReadOnlyList<ProfileVertex> ring = LadderProfiles.Ring(profile, width, depth);
        float half = length * 0.5f;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        int count = ring.Count;
        float perimeter = ring[count - 1].Distance;
        foreach (float y in new[] { -half, half })
        {
            foreach (ProfileVertex v in ring)
            {
                vertices.Add(new Vector3(v.A, y, v.B));
                normals.Add(new Vector3(v.NormalA, 0f, v.NormalB));
                if (region.Tiles) uvs.Add(new Vector2(v.Distance * 2f, y / 2f + 0.5f));
                else
                {
                    CellarUv uv = region.Map(y, v.Distance, -half, half, 0f, perimeter);
                    uvs.Add(new Vector2(uv.U, uv.V));
                }
            }
        }

        // Sides: outward-facing quads between consecutive ring vertices (Unity's clockwise front faces).
        for (int i = 0; i + 1 < count; ++i)
        {
            if (Math.Abs(ring[i + 1].Distance - ring[i].Distance) < 1e-6f) continue; // A sharp corner's twin vertices.
            int i0 = i, j0 = i + 1, i1 = i + count, j1 = i + 1 + count;
            triangles.Add(i0); triangles.Add(i1); triangles.Add(j0);
            triangles.Add(j0); triangles.Add(i1); triangles.Add(j1);
        }

        // Caps: a fan around the centre, facing -y at the bottom and +y at the top.
        foreach (float y in new[] { -half, half })
        {
            float up = y > 0f ? 1f : -1f;
            int centre = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));
            normals.Add(new Vector3(0f, up, 0f));
            uvs.Add(region.Tiles ? new Vector2(0.5f, 0.5f) : Cap(region, 0f, 0f, width, depth));
            int first = vertices.Count;
            foreach (ProfileVertex v in ring)
            {
                vertices.Add(new Vector3(v.A, y, v.B));
                normals.Add(new Vector3(0f, up, 0f));
                uvs.Add(region.Tiles ? new Vector2(v.A * 2f + 0.5f, v.B * 2f + 0.5f) : Cap(region, v.A, v.B, width, depth));
            }

            for (int i = 0; i + 1 < count; ++i)
            {
                if (Math.Abs(ring[i + 1].Distance - ring[i].Distance) < 1e-6f) continue;
                triangles.Add(centre);
                if (up > 0f) { triangles.Add(first + i + 1); triangles.Add(first + i); }
                else { triangles.Add(first + i); triangles.Add(first + i + 1); }
            }
        }

        var mesh = new Mesh { name = "tcc_ladder_" + key };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        _meshes[key] = mesh;
        return mesh;
    }

    private static Vector2 Cap(UvRegion region, float a, float b, float width, float depth)
    {
        CellarUv uv = region.Map(a, b, -width / 2f, width / 2f, -depth / 2f, depth / 2f);
        return new Vector2(uv.U, uv.V);
    }

    private static Vector3 Vector(ClimbPoint point) => new Vector3(point.X, point.Y, point.Z);

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
