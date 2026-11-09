using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>The nine floor trapdoors (CL-004 #485, slice 2): each is a Jötunn clone of its pinned native door under
/// Jötunn's inactive prefab container, keeping the donor's root <c>Piece</c>, <c>ZNetView</c>, <c>WearNTear</c>,
/// <c>Door</c> and <c>Animator</c> so state, replication, persistence, wards, damage, repair and refunds stay native.
/// Every donor child but the animated leaf is removed, the leaf keeps only its transform, and the frame and lid are
/// built from <see cref="TrapdoorBlueprint"/>. Only inactive CLONES are edited; no native prefab is changed.
/// Installed only when <see cref="TrapdoorSettings"/> is on (off by default). The lid does not move yet (slice 3).</summary>
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
            Piece piece = prefab.GetComponent<Piece>();
            WearNTear wear = prefab.GetComponent<WearNTear>();
            Door door = prefab.GetComponent<Door>();
            if (piece == null || wear == null || door == null || prefab.GetComponent<ZNetView>() == null || prefab.GetComponent<Animator>() == null)
                throw new InvalidOperationException("The clone lost a root component the donor had.");
            IReadOnlyList<string> recipe = TrapdoorBlueprint.RequirementMismatches(family, Requirements(piece));
            if (recipe.Count > 0) throw new InvalidOperationException("The clone's requirements are not the pinned cost: " + string.Join("; ", recipe));

            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) throw new InvalidOperationException("The game's piece collision layer is absent.");
            Strip(prefab);
            prefab.layer = layer;
            door.m_name = "$" + family.TrapdoorToken;

            // Presentation roots by wear state: two where the donor has a worn material (m_broken shares the worn root;
            // stave_gate's own broken root used the same worn material), one (all three references the same object)
            // where the donor itself uses one root.
            GameObject fresh = FrameRoot(prefab.transform, TrapdoorBlueprint.NewRoot, frame, layer);
            GameObject worn = frameWorn == null ? fresh : FrameRoot(prefab.transform, TrapdoorBlueprint.WornRoot, frameWorn, layer);
            wear.m_new = fresh;
            wear.m_worn = worn;
            wear.m_broken = worn;
            if (worn != fresh) worn.SetActive(false);
            // Every other WearNTear reference into the donor hierarchy is cleared: the snow overlays and the three
            // destruction fragment roots (flametal, hexagonal, ashwood) were donor children that no longer exist. An
            // empty fragment list is the native whole-object path six of the nine donors already use.
            wear.m_wet = null;
            wear.m_snow = null;
            wear.m_snowWorn = null;
            wear.m_snowBroken = null;
            wear.m_nonSolidRenderers = new List<Renderer>();
            wear.m_fragmentRoots = Array.Empty<GameObject>();
            // The tall gates' centre of mass sits 2 m up their 4 m leaf; a floor hatch's is at its own centre.
            wear.m_comOffset = Vector3.zero;

            foreach (TrapdoorPart part in TrapdoorBlueprint.FrameColliders) Collider(prefab.transform, part, layer);

            var hinge = new GameObject(TrapdoorBlueprint.Hinge) { layer = layer };
            hinge.transform.SetParent(prefab.transform, false);
            hinge.transform.localPosition = Vector(TrapdoorBlueprint.HingePosition);
            hinge.transform.localRotation = Quaternion.identity;
            // Kinematic, as the native door leaves are: it carries the lid's collider for the runtime to turn, and
            // keeps the lid out of WearNTear's support bounds and out of the build ray (both skip rigidbody colliders).
            Rigidbody body = hinge.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            Collider(hinge.transform, TrapdoorBlueprint.LidCollider, layer);
            Visual(hinge.transform, TrapdoorBlueprint.LidVisual, lid, layer);
            var strapMaterial = new Material(lid) { name = lid.name + "_tcc_hinge" };
            if (strapMaterial.HasProperty("_Color")) strapMaterial.color *= new Color(0.35f, 0.35f, 0.35f, 1f);
            foreach (TrapdoorPart strap in TrapdoorBlueprint.HingeStraps) Visual(hinge.transform, strap, strapMaterial, layer);

            foreach (ClimbPoint corner in HatchGeometry.CornerSnaps) Snap(prefab.transform, HatchGeometry.CornerSnapToken, Vector(corner), layer);
            Verify(prefab, wear);

            if (!Application.isBatchMode)
            {
                try
                {
                    Sprite rendered = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
                    if (rendered == null || rendered.texture == null || rendered.rect.width <= 0f || rendered.rect.height <= 0f)
                        throw new InvalidOperationException("Jotunn returned no usable trapdoor icon.");
                    piece.m_icon = rendered;
                }
                catch (Exception exception)
                {
                    // The donor's door icon stays. The piece is still a stable world prefab.
                    _log("Trapdoor icon fallback for " + id + ": " + SafeFailure.Brief(exception) + ". The donor's icon is shown.");
                }
            }

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

    private static DonorFacts Read(GameObject donor)
    {
        Piece piece = donor.GetComponent<Piece>();
        WearNTear wear = donor.GetComponent<WearNTear>();
        Door door = donor.GetComponent<Door>();
        return new DonorFacts
        {
            HasPiece = piece != null,
            HasWearNTear = wear != null,
            HasDoor = door != null,
            HasNetView = donor.GetComponent<ZNetView>() != null,
            HasRootAnimator = donor.GetComponent<Animator>() != null,
            HasLeaf = donor.transform.Find(TrapdoorBlueprint.LeafChild) != null,
            Station = piece != null && piece.m_craftingStation != null ? piece.m_craftingStation.name : null,
            Requirements = piece != null ? Requirements(piece) : Array.Empty<ObservedRequirement>(),
            Health = wear != null ? wear.m_health : float.NaN,
            Material = wear != null ? (int)wear.m_materialType : -1,
            Burnable = wear != null && wear.m_burnable,
            DoorName = door != null ? door.m_name : null,
            HasKeyItem = door != null && door.m_keyItem != null,
            CanNotBeClosed = door != null && door.m_canNotBeClosed,
            ChecksGuardStone = door != null && door.m_checkGuardStone,
            HasOpenEnable = door != null && door.m_openEnable != null,
            OpenEffect = door != null ? Effects(door.m_openEffects) : null,
            CloseEffect = door != null ? Effects(door.m_closeEffects) : null,
        };
    }

    private static IReadOnlyList<ObservedRequirement> Requirements(Piece piece)
    {
        Piece.Requirement[] resources = piece.m_resources ?? Array.Empty<Piece.Requirement>();
        var observed = new ObservedRequirement[resources.Length];
        for (int index = 0; index < resources.Length; ++index)
        {
            Piece.Requirement entry = resources[index];
            observed[index] = entry == null
                ? new ObservedRequirement(null, 0, false)
                : new ObservedRequirement(entry.m_resItem != null ? entry.m_resItem.name : null, entry.m_amount, entry.m_recover);
        }

        return observed;
    }

    /// <summary>The effect prefabs' names joined by commas, so a list of two never reads as its first entry.</summary>
    private static string? Effects(EffectList? effects)
    {
        if (effects?.m_effectPrefabs == null) return null;
        return string.Join(",", effects.m_effectPrefabs.Select(effect => effect?.m_prefab != null ? effect.m_prefab.name : "<none>"));
    }

    private static Material DonorMaterial(GameObject donor, string name)
    {
        Material? found = donor.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(candidate => candidate != null && (candidate.name == name || candidate.name == name + " (Instance)"));
        // Borrowed read-only: the shared native material is assigned, never changed.
        return found ?? throw new InvalidOperationException("Donor material unavailable: " + name + " on " + donor.name);
    }

    /// <summary>DOOR_DONOR_MATRIX.md section 4. DestroyImmediate is confined to the inactive clone, so RenderManager
    /// and the placement ghost never see donor geometry.</summary>
    private static void Strip(GameObject prefab)
    {
        foreach (LODGroup lod in prefab.GetComponents<LODGroup>()) UnityEngine.Object.DestroyImmediate(lod);
        foreach (Collider collider in prefab.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
        foreach (Renderer renderer in prefab.GetComponents<Renderer>()) UnityEngine.Object.DestroyImmediate(renderer);
        foreach (MeshFilter filter in prefab.GetComponents<MeshFilter>()) UnityEngine.Object.DestroyImmediate(filter);
        Transform? leaf = null;
        for (int index = prefab.transform.childCount - 1; index >= 0; --index)
        {
            Transform child = prefab.transform.GetChild(index);
            if (leaf == null && child.name == TrapdoorBlueprint.LeafChild) { leaf = child; continue; }
            UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        if (leaf == null) throw new InvalidOperationException("The clone has no '" + TrapdoorBlueprint.LeafChild + "' leaf.");
        for (int index = leaf.childCount - 1; index >= 0; --index) UnityEngine.Object.DestroyImmediate(leaf.GetChild(index).gameObject);
        // The leaf keeps only its transform, which the hidden native animator writes and nothing reads. Its colliders
        // and renderers go first, then its kinematic body.
        foreach (Component component in leaf.GetComponents<Component>())
        {
            if (component is Transform || component is Rigidbody) continue;
            UnityEngine.Object.DestroyImmediate(component);
        }

        foreach (Rigidbody body in leaf.GetComponents<Rigidbody>()) UnityEngine.Object.DestroyImmediate(body);
    }

    /// <summary>Refuses the clone if anything of the donor's survived or a wear reference points outside it.</summary>
    private static void Verify(GameObject prefab, WearNTear wear)
    {
        foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            if (!IsBlueprintPart(prefab.transform, collider.transform)) throw new InvalidOperationException("A donor collider survived on " + collider.name + ".");
        }

        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsBlueprintPart(prefab.transform, renderer.transform)) throw new InvalidOperationException("A donor renderer survived on " + renderer.name + ".");
        }

        foreach (GameObject state in new[] { wear.m_new, wear.m_worn, wear.m_broken })
        {
            if (state == null || state.transform.parent != prefab.transform)
                throw new InvalidOperationException("A wear state does not point at the hatch's own presentation root.");
        }

        Transform? leaf = prefab.transform.Find(TrapdoorBlueprint.LeafChild);
        if (leaf == null || leaf.childCount != 0 || leaf.GetComponents<Component>().Length != 1)
            throw new InvalidOperationException("The animated leaf is not a bare transform.");
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

    private static GameObject FrameRoot(Transform parent, string name, Material material, int layer)
    {
        var root = new GameObject(name) { layer = layer };
        root.transform.SetParent(parent, false);
        foreach (TrapdoorPart part in TrapdoorBlueprint.FrameVisuals) Visual(root.transform, part, material, layer);
        return root;
    }

    private static void Visual(Transform parent, TrapdoorPart part, Material material, int layer)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
        box.name = part.Name;
        box.layer = layer;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = Vector(part.Centre);
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = Vector(part.Size);
        box.GetComponent<Renderer>().sharedMaterial = material;
    }

    /// <summary>A collider on its own child, never on the root: the placement ghost's blocked-by-player test skips the
    /// ghost root's colliders (observed with the ladders).</summary>
    private static void Collider(Transform parent, TrapdoorPart part, int layer)
    {
        var holder = new GameObject(part.Name) { layer = layer };
        holder.transform.SetParent(parent, false);
        BoxCollider box = holder.AddComponent<BoxCollider>();
        box.center = Vector(part.Centre);
        box.size = Vector(part.Size);
    }

    private static void Snap(Transform parent, string name, Vector3 position, int layer)
    {
        var point = new GameObject(name) { tag = "snappoint", layer = layer };
        point.transform.SetParent(parent, false);
        point.transform.localPosition = position;
    }

    private static Vector3 Vector(ClimbPoint point) => new Vector3(point.X, point.Y, point.Z);

    public void Dispose()
    {
        if (!_subscribed) return;
        PrefabManager.OnVanillaPrefabsAvailable -= Register;
        _subscribed = false;
    }
}
