using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;
using static TheConcernedCat.ConcernedLadders.Pieces.DoorPieceKit;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>The nine sloped cellar doors (CL-005 #486, slice 2): each is a Jötunn clone of its pinned native door
/// (the same donor as the family's trapdoor, <see cref="CellarDoors"/>), checked against CL-004's matrix and stripped
/// exactly as the trapdoors are (<see cref="DoorPieceKit"/>, shared, not copied), so state, replication, persistence,
/// wards, damage, repair and refunds stay native. The frame is four convex prisms, each one mesh used by its renderer and
/// its convex mesh collider; the leaves are boxes on two hinge pivots in the slope transform, each pivot on a kinematic
/// body (<see cref="CellarBlueprint"/>). Only inactive CLONES are edited; no native prefab is changed. Installed only
/// when <see cref="CellarDoorSettings"/> is on (off by default). No leaf runtime yet: the leaves stay shut. No RPC, ZDO
/// field or ownership claim is added.</summary>
internal sealed class CellarDoorPieceRegistration : IDisposable
{
    private readonly Action<string> _log;
    private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _refused = new List<string>();
    private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
    private bool _subscribed;
    private bool _attempted;

    internal CellarDoorPieceRegistration(Action<string> log) => _log = log;

    internal int RegisteredCount => _registered.Count;

    internal void Install()
    {
        if (_subscribed || _attempted) return;
        CellarDoorLocalization.Register();
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        _subscribed = true;
    }

    internal void Register()
    {
        // One attempt per session, as the trapdoors': a refused or failed family is never retried, because Jötunn's
        // AssetManager remembers a clone before AddPiece and a retry under the same stable ID would find it unfinished.
        if (_attempted) return;
        _attempted = true;
        Dispose();
        foreach (CellarDoorFamily cellar in CellarDoors.All) { Add(cellar); }
        _log("Concerned Ladders cellar doors: " + _registered.Count + " of " + CellarDoors.All.Count + " registered" +
             (_refused.Count == 0 ? "." : "; refused " + string.Join(", ", _refused) + "."));
    }

    private void Add(CellarDoorFamily cellar)
    {
        DoorFamily family = cellar.Door;
        string id = cellar.Prefab;
        try
        {
            GameObject donor = PrefabManager.Instance.GetPrefab(family.Donor);
            IReadOnlyList<string> problems = TrapdoorBlueprint.DonorMismatches(family, donor == null ? null : Read(donor));
            if (problems.Count > 0) { Refuse(cellar, "the installed donor differs from the pinned matrix: " + string.Join("; ", problems)); return; }

            // The donor's own materials, read before anything is cloned or stripped: the leaf's for the leaves, the
            // frame's for the frame, as the family's trapdoor borrows them.
            TrapdoorMaterials names = TrapdoorBlueprint.MaterialsFor(family);
            Material leafMaterial = DonorMaterial(donor!, names.Lid);
            Material frame = DonorMaterial(donor!, names.Frame);
            Material? frameWorn = names.FrameWorn == null ? null : DonorMaterial(donor!, names.FrameWorn);

            // Explicit requirements, always: Jötunn keeps the donor's when the array is empty, so they are set from
            // the cellar door's own cost and then read back from the clone below.
            var cost = new RequirementConfig[cellar.Costs.Count];
            for (int index = 0; index < cost.Length; ++index)
            {
                cost[index] = new RequirementConfig(cellar.Costs[index].Item, cellar.Costs[index].Amount, 0, true);
            }

            var config = new PieceConfig
            {
                Name = "$" + cellar.Token,
                Description = "$" + CellarDoors.DescriptionToken,
                PieceTable = "Hammer",
                Category = "Building",
                // Public Jötunn 2.30.2 usage tag names; Jötunn replaces the donor's m_usage with exactly these.
                Usage = CellarBlueprint.Usage.ToArray(),
                CraftingStation = family.Station,
                Requirements = cost
            };
            var custom = new CustomPiece(id, family.Donor, config);
            GameObject prefab = custom.PiecePrefab;
            if (prefab == null || prefab.activeInHierarchy)
                throw new InvalidOperationException("Expected an inactive-parent custom prefab clone.");
            RequireRoot(prefab, out Piece piece, out WearNTear wear, out Door door);
            IReadOnlyList<string> recipe = TrapdoorBlueprint.RequirementMismatches(cellar.Costs, Requirements(piece));
            if (recipe.Count > 0) throw new InvalidOperationException("The clone's requirements are not the cellar door's cost: " + string.Join("; ", recipe));

            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) throw new InvalidOperationException("The game's piece collision layer is absent.");
            Strip(prefab);
            prefab.layer = layer;
            door.m_name = "$" + cellar.Token;

            GameObject fresh = FrameRoot(prefab.transform, TrapdoorBlueprint.NewRoot, frame, layer);
            GameObject worn = frameWorn == null ? fresh : FrameRoot(prefab.transform, TrapdoorBlueprint.WornRoot, frameWorn, layer);
            AdoptPresentation(wear, fresh, worn);

            // The fixed frame's colliders: one convex mesh collider per prism, from the very mesh its renderers show.
            // Static (no body), so these are the colliders WearNTear measures support with.
            foreach (CellarMesh shape in CellarBlueprint.FrameMeshes)
            {
                var holder = new GameObject(shape.Name) { layer = layer };
                holder.transform.SetParent(prefab.transform, false);
                MeshCollider collider = holder.AddComponent<MeshCollider>();
                collider.convex = true;
                collider.sharedMesh = FrameMesh(shape);
            }

            var slope = new GameObject(CellarBlueprint.SlopeRoot) { layer = layer };
            slope.transform.SetParent(prefab.transform, false);
            slope.transform.localPosition = Vector(CellarBlueprint.SlopePosition);
            slope.transform.localRotation = Quaternion.Euler(CellarBlueprint.SlopeEulerX, 0f, 0f);
            Material markMaterial = HingeMaterial(leafMaterial);
            foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
            {
                var hinge = new GameObject(CellarBlueprint.Hinge(leaf)) { layer = layer };
                hinge.transform.SetParent(slope.transform, false);
                hinge.transform.localPosition = Vector(CellarBlueprint.HingePosition(leaf));
                // Closed. Nothing turns it in this slice: the leaf runtime is the next one.
                hinge.transform.localRotation = Quaternion.identity;
                KinematicBody(hinge);
                CellarPart box = CellarBlueprint.LeafCollider(leaf);
                ColliderBox(hinge.transform, box.Name, box.Centre, box.Size, layer);
                CellarPart visual = CellarBlueprint.LeafVisual(leaf);
                VisualBox(hinge.transform, visual.Name, visual.Centre, visual.Size, leafMaterial, layer);
                foreach (CellarPart mark in CellarBlueprint.HingeMarks(leaf)) VisualBox(hinge.transform, mark.Name, mark.Centre, mark.Size, markMaterial, layer);
            }

            foreach (ClimbPoint corner in CellarGeometry.CornerSnaps) Snap(prefab.transform, CellarGeometry.CornerSnapToken, Vector(corner), layer);
            Verify(prefab, wear);

            Icon(prefab, piece, id, "cellar door", _log);

            if (!PieceManager.Instance.AddPiece(custom))
                throw new InvalidOperationException("Jotunn rejected the custom piece registration.");
            _registered.Add(id);
            _log("Registered Concerned Ladders prefab " + id + " from " + family.Donor + ".");
        }
        catch (Exception exception)
        {
            Refuse(cellar, SafeFailure.Brief(exception));
        }
    }

    private void Refuse(CellarDoorFamily cellar, string reason)
    {
        _refused.Add(cellar.Prefab);
        _log("Concerned Ladders cellar door " + cellar.Prefab + " NOT registered: " + reason +
             ". Placed " + cellar.English + "s are deleted by the game if a world is loaded in this session; restart after repair.");
    }

    /// <summary>One shared mesh per prism for every family: the frame's shape is the same nine times, only its
    /// material differs. Kept referenced by the clones, which live under Jötunn's persistent prefab container.</summary>
    private Mesh FrameMesh(CellarMesh shape)
    {
        if (_meshes.TryGetValue(shape.Name, out Mesh? existing) && existing != null) return existing;
        // Never swept by an unused-asset unload, whatever holds it: four small meshes for the whole session.
        var mesh = new Mesh { name = "tcc_cellardoor_" + shape.Name, hideFlags = HideFlags.DontUnloadUnusedAsset };
        mesh.vertices = shape.Vertices.Select(Vector).ToArray();
        mesh.normals = shape.Normals.Select(Vector).ToArray();
        mesh.uv = shape.Uvs.Select(uv => new Vector2(uv.U, uv.V)).ToArray();
        mesh.triangles = shape.Triangles.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        _meshes[shape.Name] = mesh;
        return mesh;
    }

    private GameObject FrameRoot(Transform parent, string name, Material material, int layer)
    {
        GameObject root = PresentationRoot(parent, name, layer);
        foreach (CellarMesh shape in CellarBlueprint.FrameMeshes)
        {
            var part = new GameObject(shape.Name) { layer = layer };
            part.transform.SetParent(root.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = FrameMesh(shape);
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        return root;
    }

    /// <summary>Refuses the clone if anything of the donor's survived (<see cref="DoorPieceKit.VerifyStripped"/>), a
    /// frame collider is not a convex mesh collider with its mesh, a leaf pivot is missing, is not on a kinematic body
    /// or lacks its box collider, or a trapdoor lid runtime is on it (a cellar door is not a trapdoor: the ladder
    /// integration must not treat it as one).</summary>
    private static void Verify(GameObject prefab, WearNTear wear)
    {
        VerifyStripped(prefab, wear, part => IsBlueprintPart(prefab.transform, part));

        foreach (CellarMesh shape in CellarBlueprint.FrameMeshes)
        {
            Transform? holder = prefab.transform.Find(shape.Name);
            MeshCollider? collider = holder != null ? holder.GetComponent<MeshCollider>() : null;
            if (collider == null || !collider.convex || collider.sharedMesh == null || collider.isTrigger || holder!.GetComponent<Rigidbody>() != null)
                throw new InvalidOperationException("The frame collider " + shape.Name + " is missing or not a static convex mesh.");
        }

        Transform? slope = prefab.transform.Find(CellarBlueprint.SlopeRoot);
        if (slope == null) throw new InvalidOperationException("The cellar door has no slope transform.");
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            Transform? hinge = slope.Find(CellarBlueprint.Hinge(leaf));
            Rigidbody? body = hinge != null ? hinge.GetComponent<Rigidbody>() : null;
            Transform? box = hinge != null ? hinge.Find(CellarBlueprint.LeafCollider(leaf).Name) : null;
            if (body == null || !body.isKinematic || body.useGravity || box == null || box.GetComponent<BoxCollider>() == null)
                throw new InvalidOperationException("The " + leaf + " leaf's pivot, body or collider is missing or misconfigured.");
        }

        if (prefab.GetComponentsInChildren<TrapdoorLid>(true).Length != 0)
            throw new InvalidOperationException("A cellar door must not carry the trapdoor lid runtime.");
    }

    /// <summary>True for a part this adapter built: a frame collider directly under the root, a visual under one of
    /// the piece's own presentation roots (directly under the root), or a leaf part under one of the two hinge
    /// pivots of the slope transform (directly under the root).</summary>
    private static bool IsBlueprintPart(Transform root, Transform part)
    {
        Transform? parent = part.parent;
        if (parent == null) return false;
        if (parent == root) return CellarBlueprint.FrameMeshes.Any(shape => shape.Name == part.name);
        if (parent.parent == root) return parent.name == TrapdoorBlueprint.NewRoot || parent.name == TrapdoorBlueprint.WornRoot;
        Transform? slope = parent.parent;
        return slope != null && slope.name == CellarBlueprint.SlopeRoot && slope.parent == root &&
               CellarBlueprint.Leaves.Any(leaf => CellarBlueprint.Hinge(leaf) == parent.name);
    }

    public void Dispose()
    {
        if (!_subscribed) return;
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
        _subscribed = false;
    }
}
