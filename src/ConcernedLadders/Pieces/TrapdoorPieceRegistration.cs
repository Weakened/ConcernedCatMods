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

/// <summary>The nine floor trapdoors (CL-004 #485, slice 2): each is a Jötunn clone of its pinned native door under
/// Jötunn's inactive prefab container, keeping the donor's root <c>Piece</c>, <c>ZNetView</c>, <c>WearNTear</c>,
/// <c>Door</c> and <c>Animator</c> so state, replication, persistence, wards, damage, repair and refunds stay native.
/// Every donor child but the animated leaf is removed, the leaf keeps only its transform, and the frame and lid are
/// built from <see cref="TrapdoorBlueprint"/>. Only inactive CLONES are edited; no native prefab is changed.
/// Installed only when <see cref="TrapdoorSettings"/> is on (off by default). Each clone carries a
/// <see cref="TrapdoorLid"/> (slice 3) that turns the lid from the native door state.</summary>
internal sealed class TrapdoorPieceRegistration : IDisposable
{
    private readonly Action<string> _log;
    private readonly HashSet<string> _registered = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _refused = new List<string>();
    private bool _subscribed;
    private bool _attempted;

    internal TrapdoorPieceRegistration(Action<string> log) => _log = log;

    internal int RegisteredCount => _registered.Count;

    internal void Install()
    {
        if (_subscribed || _attempted) return;
        TrapdoorLocalization.Register();
        PrefabManager.OnVanillaPrefabsAvailable += Register;
        _subscribed = true;
    }

    internal void Register()
    {
        // One attempt per session. A refused or failed family is never retried: Jötunn's AssetManager remembers a clone
        // before AddPiece, so a retry under the same stable ID would find an existing, unfinished asset.
        if (_attempted) return;
        _attempted = true;
        Dispose();
        TrapdoorLid.Log = _log;
        if (!TrapdoorLid.ResolveLayers())
            _log("Concerned Ladders trapdoors: a collision layer the lid checks is missing, so every lid stays shut.");
        foreach (DoorFamily family in DoorFamilies.All) { Add(family); }
        _log("Concerned Ladders trapdoors: " + _registered.Count + " of " + DoorFamilies.All.Count + " registered" +
             (_refused.Count == 0 ? "." : "; refused " + string.Join(", ", _refused) + "."));
    }

    private void Add(DoorFamily family)
    {
        string id = family.TrapdoorPrefab;
        try
        {
            GameObject donor = PrefabManager.Instance.GetPrefab(family.Donor);
            IReadOnlyList<string> problems = TrapdoorBlueprint.DonorMismatches(family, donor == null ? null : Read(donor));
            if (problems.Count > 0) { Refuse(family, "the installed donor differs from the pinned matrix: " + string.Join("; ", problems)); return; }

            // The donor's own materials, read before anything is cloned or stripped.
            TrapdoorMaterials names = TrapdoorBlueprint.MaterialsFor(family);
            // Where each surface samples those materials' textures, most of which are atlases (#548).
            DoorSurface surface = DoorSurfaces.For(family);
            Material lid = DonorMaterial(donor!, names.Lid);
            Material frame = DonorMaterial(donor!, names.Frame);
            Material? frameWorn = names.FrameWorn == null ? null : DonorMaterial(donor!, names.FrameWorn);

            // Explicit requirements, always: Jötunn keeps the donor's when the array is empty, so they are set from
            // the pin and then read back from the clone below.
            var cost = new RequirementConfig[family.Costs.Count];
            for (int index = 0; index < cost.Length; ++index)
            {
                cost[index] = new RequirementConfig(family.Costs[index].Item, family.Costs[index].Amount, 0, true);
            }

            var config = new PieceConfig
            {
                Name = "$" + family.TrapdoorToken,
                Description = "$" + TrapdoorBlueprint.DescriptionToken,
                PieceTable = "Hammer",
                Category = "Building",
                // Public Jötunn 2.30.2 usage tag names; Jötunn replaces the donor's m_usage with exactly these.
                Usage = TrapdoorBlueprint.Usage.ToArray(),
                CraftingStation = family.Station,
                Requirements = cost
            };
            var custom = new CustomPiece(id, family.Donor, config);
            GameObject prefab = custom.PiecePrefab;
            if (prefab == null || prefab.activeInHierarchy)
                throw new InvalidOperationException("Expected an inactive-parent custom prefab clone.");
            RequireRoot(prefab, out Piece piece, out WearNTear wear, out Door door);
            IReadOnlyList<string> recipe = TrapdoorBlueprint.RequirementMismatches(family, Requirements(piece));
            if (recipe.Count > 0) throw new InvalidOperationException("The clone's requirements are not the pinned cost: " + string.Join("; ", recipe));

            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) throw new InvalidOperationException("The game's piece collision layer is absent.");
            Strip(prefab);
            prefab.layer = layer;
            door.m_name = "$" + family.TrapdoorToken;

            // Presentation roots by wear state (DoorPieceKit.AdoptPresentation): two where the donor has a worn
            // material, one where the donor itself uses one root.
            GameObject fresh = FrameRoot(prefab.transform, TrapdoorBlueprint.NewRoot, frame, surface.Frame, layer);
            GameObject worn = frameWorn == null ? fresh : FrameRoot(prefab.transform, TrapdoorBlueprint.WornRoot, frameWorn, surface.Frame, layer);
            AdoptPresentation(wear, fresh, worn);

            foreach (TrapdoorPart part in TrapdoorBlueprint.FrameColliders) Collider(prefab.transform, part, layer);

            var hinge = new GameObject(TrapdoorBlueprint.Hinge) { layer = layer };
            hinge.transform.SetParent(prefab.transform, false);
            hinge.transform.localPosition = Vector(TrapdoorBlueprint.HingePosition);
            hinge.transform.localRotation = Quaternion.identity;
            // Kinematic, as the native door leaves are: it carries the lid's collider for the runtime to turn, and
            // keeps the lid out of WearNTear's support bounds and out of the build ray (both skip rigidbody colliders).
            KinematicBody(hinge);
            Collider(hinge.transform, TrapdoorBlueprint.LidCollider, layer);
            TrapdoorPart lidVisual = TrapdoorBlueprint.LidVisual;
            LeafVisuals(hinge.transform, lidVisual.Name, lidVisual.Centre, lidVisual.Size, LeafPattern.For(family), lid, frame, surface, layer);
            Material strapMaterial = HingeMaterial(lid);
            foreach (TrapdoorPart strap in TrapdoorBlueprint.HingeStraps) Visual(hinge.transform, strap, strapMaterial, surface.Leaf, layer);
            // The lid runtime (slice 3): reads the native door state, never writes it. The swing time is the donor's.
            prefab.AddComponent<TrapdoorLid>().SwingSeconds = family.SwingSeconds;

            foreach (ClimbPoint corner in HatchGeometry.CornerSnaps) Snap(prefab.transform, HatchGeometry.CornerSnapToken, Vector(corner), layer);
            Verify(prefab, wear);

            Icon(prefab, piece, id, "trapdoor", _log);

            if (!PieceManager.Instance.AddPiece(custom))
                throw new InvalidOperationException("Jotunn rejected the custom piece registration.");
            _registered.Add(id);
            _log("Registered Concerned Ladders prefab " + id + " from " + family.Donor + ".");
        }
        catch (Exception exception)
        {
            Refuse(family, SafeFailure.Brief(exception));
        }
    }

    private void Refuse(DoorFamily family, string reason)
    {
        _refused.Add(family.TrapdoorPrefab);
        _log("Concerned Ladders trapdoor " + family.TrapdoorPrefab + " NOT registered: " + reason +
             ". Placed " + family.EnglishTrapdoor + "s are deleted by the game if a world is loaded in this session; restart after repair.");
    }

    /// <summary>Refuses the clone if anything of the donor's survived or a wear reference points outside it
    /// (<see cref="DoorPieceKit.VerifyStripped"/>), or the lid runtime, hinge or collider is missing.</summary>
    private static void Verify(GameObject prefab, WearNTear wear)
    {
        VerifyStripped(prefab, wear, part => IsBlueprintPart(prefab.transform, part));

        TrapdoorLid[] lids = prefab.GetComponentsInChildren<TrapdoorLid>(true);
        Transform? hinge = prefab.transform.Find(TrapdoorBlueprint.Hinge);
        Transform? lidCollider = hinge != null ? hinge.Find(TrapdoorBlueprint.LidCollider.Name) : null;
        if (lids.Length != 1 || lids[0].gameObject != prefab || lidCollider == null || lidCollider.GetComponent<BoxCollider>() == null ||
            !(lids[0].SwingSeconds >= HatchMotion.MinimumSwingSeconds && lids[0].SwingSeconds <= HatchMotion.MaximumSwingSeconds))
            throw new InvalidOperationException("The lid runtime, its hinge or its collider is missing or misconfigured.");
    }

    /// <summary>True for a box this adapter built: a frame collider directly under the root, or a part under one of
    /// the hatch's own presentation roots or its hinge, which sit directly under the root.</summary>
    private static bool IsBlueprintPart(Transform root, Transform part)
    {
        Transform? parent = part.parent;
        if (parent == null) return false;
        if (parent == root) return TrapdoorBlueprint.FrameColliders.Any(p => p.Name == part.name);
        string owner = parent.name;
        return parent.parent == root &&
               (owner == TrapdoorBlueprint.NewRoot || owner == TrapdoorBlueprint.WornRoot || owner == TrapdoorBlueprint.Hinge);
    }

    private static GameObject FrameRoot(Transform parent, string name, Material material, UvRegion region, int layer)
    {
        GameObject root = PresentationRoot(parent, name, layer);
        foreach (TrapdoorPart part in TrapdoorBlueprint.FrameVisuals) Visual(root.transform, part, material, region, layer);
        return root;
    }

    private static void Visual(Transform parent, TrapdoorPart part, Material material, UvRegion region, int layer) =>
        VisualBox(parent, part.Name, part.Centre, part.Size, material, region, layer);

    private static void Collider(Transform parent, TrapdoorPart part, int layer) =>
        ColliderBox(parent, part.Name, part.Centre, part.Size, layer);

    public void Dispose()
    {
        if (!_subscribed) return;
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
        _subscribed = false;
    }
}
