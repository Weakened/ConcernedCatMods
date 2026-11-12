using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>What the floor trapdoors (CL-004 #485) and the sloped cellar doors (CL-005 #486) do the same way to a
/// Jötunn clone of a pinned native door: read the donor for the matrix check, read the clone's requirements back,
/// borrow the donor's materials, strip the donor's geometry, point the wear states at the piece's own presentation
/// roots, and refuse the clone if anything of the donor survived. One copy, so the two door pieces can never drift
/// apart on what a clone keeps (DOOR_DONOR_MATRIX.md section 4). Only inactive CLONES are edited; no native prefab is
/// changed. Nothing here adds an RPC, a ZDO field or an ownership claim.</summary>
internal static class DoorPieceKit
{
    /// <summary>What the installed donor says, for <see cref="TrapdoorBlueprint.DonorMismatches"/>.</summary>
    internal static DonorFacts Read(GameObject donor)
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

    /// <summary>A piece's requirements as read, for the pinned-cost comparison.</summary>
    internal static IReadOnlyList<ObservedRequirement> Requirements(Piece piece)
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

    internal static Material DonorMaterial(GameObject donor, string name)
    {
        Material? found = donor.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(candidate => candidate != null && (candidate.name == name || candidate.name == name + " (Instance)"));
        // Borrowed read-only: the shared native material is assigned, never changed.
        return found ?? throw new InvalidOperationException("Donor material unavailable: " + name + " on " + donor.name);
    }

    /// <summary>A darkened copy of a leaf's material for its hinge hardware, the ghost's hinge marker. A copy, so the
    /// shared native material is never changed.</summary>
    internal static Material HingeMaterial(Material leaf)
    {
        var hinge = new Material(leaf) { name = leaf.name + "_tcc_hinge" };
        if (hinge.HasProperty("_Color")) hinge.color *= new Color(0.35f, 0.35f, 0.35f, 1f);
        return hinge;
    }

    /// <summary>The root components the donor had and the clone must keep, or a refusal.</summary>
    internal static void RequireRoot(GameObject prefab, out Piece piece, out WearNTear wear, out Door door)
    {
        piece = prefab.GetComponent<Piece>();
        wear = prefab.GetComponent<WearNTear>();
        door = prefab.GetComponent<Door>();
        if (piece == null || wear == null || door == null || prefab.GetComponent<ZNetView>() == null || prefab.GetComponent<Animator>() == null)
            throw new InvalidOperationException("The clone lost a root component the donor had.");
    }

    /// <summary>DOOR_DONOR_MATRIX.md section 4: the root's own geometry and every donor child but the animated leaf
    /// go, and the leaf keeps only its transform, which the hidden native animator writes and nothing reads.
    /// DestroyImmediate is confined to the inactive clone, so RenderManager and the placement ghost never see donor
    /// geometry.</summary>
    internal static void Strip(GameObject prefab)
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
        // The leaf's colliders and renderers go first, then its kinematic body.
        foreach (Component component in leaf.GetComponents<Component>())
        {
            if (component is Transform || component is Rigidbody) continue;
            UnityEngine.Object.DestroyImmediate(component);
        }

        foreach (Rigidbody body in leaf.GetComponents<Rigidbody>()) UnityEngine.Object.DestroyImmediate(body);
    }

    /// <summary>An empty presentation root directly under the piece root, for one wear state's visuals.</summary>
    internal static GameObject PresentationRoot(Transform parent, string name, int layer)
    {
        var root = new GameObject(name) { layer = layer };
        root.transform.SetParent(parent, false);
        return root;
    }

    /// <summary>Points the wear states at the piece's own presentation roots and clears every other WearNTear
    /// reference into the donor hierarchy: two roots where the donor has a worn material (m_broken shares the worn
    /// root; stave_gate's own broken root used the same worn material), one (all three references the same object)
    /// where the donor itself uses one root. The snow overlays and the three destruction fragment roots (flametal,
    /// hexagonal, ashwood) were donor children that no longer exist; an empty fragment list is the native whole-object
    /// path six of the nine donors already use. The tall gates' centre of mass sits 2 m up their 4 m leaf; a floor
    /// piece's is at its own centre.</summary>
    internal static void AdoptPresentation(WearNTear wear, GameObject fresh, GameObject worn)
    {
        wear.m_new = fresh;
        wear.m_worn = worn;
        wear.m_broken = worn;
        if (worn != fresh) worn.SetActive(false);
        wear.m_wet = null;
        wear.m_snow = null;
        wear.m_snowWorn = null;
        wear.m_snowBroken = null;
        wear.m_nonSolidRenderers = new List<Renderer>();
        wear.m_fragmentRoots = Array.Empty<GameObject>();
        wear.m_comOffset = Vector3.zero;
    }

    /// <summary>Refuses the clone if anything of the donor's survived (every collider and renderer must be a part
    /// the adapter built, as <paramref name="isBlueprintPart"/> says), a wear reference points outside the piece's
    /// own presentation roots, or the animated leaf is more than a bare transform.</summary>
    internal static void VerifyStripped(GameObject prefab, WearNTear wear, Func<Transform, bool> isBlueprintPart)
    {
        foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            if (!isBlueprintPart(collider.transform)) throw new InvalidOperationException("A donor collider survived on " + collider.name + ".");
        }

        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!isBlueprintPart(renderer.transform)) throw new InvalidOperationException("A donor renderer survived on " + renderer.name + ".");
        }

        foreach (GameObject state in new[] { wear.m_new, wear.m_worn, wear.m_broken })
        {
            if (state == null || state.transform.parent != prefab.transform)
                throw new InvalidOperationException("A wear state does not point at the piece's own presentation root.");
        }

        Transform? leaf = prefab.transform.Find(TrapdoorBlueprint.LeafChild);
        if (leaf == null || leaf.childCount != 0 || leaf.GetComponents<Component>().Length != 1)
            throw new InvalidOperationException("The animated leaf is not a bare transform.");
    }

    /// <summary>A kinematic, gravity-free body, as the native door leaves have: it carries a moving part's collider
    /// for a runtime to turn, and keeps that part out of WearNTear's support bounds and out of the build ray (both
    /// skip rigidbody colliders).</summary>
    internal static void KinematicBody(GameObject pivot)
    {
        Rigidbody body = pivot.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    /// <summary>Every mesh the door pieces build, by content key: a few dozen small meshes for the whole session, shared
    /// by every family that has the same shape and texture region. Kept referenced by the clones, which live under
    /// Jötunn's persistent prefab container, and never swept by an unused-asset unload.</summary>
    private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);

    /// <summary>The Unity mesh of a game-free one, built once per <paramref name="key"/>.</summary>
    internal static Mesh UnityMesh(string key, Func<CellarMesh> build)
    {
        if (Meshes.TryGetValue(key, out Mesh? existing) && existing != null) return existing;
        CellarMesh shape = build();
        var mesh = new Mesh { name = "tcc_door_" + key, hideFlags = HideFlags.DontUnloadUnusedAsset };
        mesh.vertices = shape.Vertices.Select(Vector).ToArray();
        mesh.normals = shape.Normals.Select(Vector).ToArray();
        mesh.uv = shape.Uvs.Select(uv => new Vector2(uv.U, uv.V)).ToArray();
        mesh.triangles = shape.Triangles.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        Meshes[key] = mesh;
        return mesh;
    }

    /// <summary>A visual box (#548): a generated box mesh at its true size, its faces sampling only
    /// <paramref name="region"/> of the material's texture, never the built-in cube, whose faces each show the whole
    /// sheet. No collider.</summary>
    internal static void VisualBox(Transform parent, string name, ClimbPoint centre, ClimbPoint size, Material material, UvRegion region, int layer)
    {
        string key = FormattableString.Invariant($"box_{size.X:0.#####}x{size.Y:0.#####}x{size.Z:0.#####}_{region.Key}");
        Mesh mesh = UnityMesh(key, () => CellarBlueprint.Mesh(CellarBlueprint.BoxPrism(key, new ClimbPoint(0f, 0f, 0f), size), region));
        var box = new GameObject(name) { layer = layer };
        box.transform.SetParent(parent, false);
        box.transform.localPosition = Vector(centre);
        box.transform.localRotation = Quaternion.identity;
        box.AddComponent<MeshFilter>().sharedMesh = mesh;
        box.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>A leaf's visual boxes in its look (<see cref="LeafPattern"/>, #547): boards in the donor leaf's material
    /// and region, rails in the donor frame's (#548). Visual only; the leaf's collider is built separately and is the
    /// same for every look.</summary>
    internal static void LeafVisuals(Transform parent, string name, ClimbPoint centre, ClimbPoint size, LeafLook look,
        Material board, Material rail, DoorSurface surface, int layer)
    {
        foreach (LeafPiece piece in LeafPattern.Parts(name, centre, size, look))
        {
            bool isRail = piece.Role == LeafPieceRole.Rail;
            VisualBox(parent, piece.Name, piece.Centre, piece.Size, isRail ? rail : board, isRail ? surface.Frame : surface.Leaf, layer);
        }
    }

    /// <summary>A box collider on its own child, never on the root: the placement ghost's blocked-by-player test
    /// skips the ghost root's colliders (observed with the ladders).</summary>
    internal static void ColliderBox(Transform parent, string name, ClimbPoint centre, ClimbPoint size, int layer)
    {
        var holder = new GameObject(name) { layer = layer };
        holder.transform.SetParent(parent, false);
        UnityEngine.BoxCollider box = holder.AddComponent<UnityEngine.BoxCollider>();
        box.center = Vector(centre);
        box.size = Vector(size);
    }

    /// <summary>A native snap point (tagged <c>snappoint</c>, named by its HUD token).</summary>
    internal static void Snap(Transform parent, string name, Vector3 position, int layer)
    {
        var point = new GameObject(name) { tag = "snappoint", layer = layer };
        point.transform.SetParent(parent, false);
        point.transform.localPosition = position;
    }

    /// <summary>The piece's hammer icon, rendered from the finished clone; on any failure the donor's door icon stays
    /// and the piece is still a stable world prefab. Skipped in batch mode.</summary>
    internal static void Icon(GameObject prefab, Piece piece, string id, string what, Action<string> log)
    {
        if (Application.isBatchMode) return;
        try
        {
            Sprite rendered = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
            if (rendered == null || rendered.texture == null || rendered.rect.width <= 0f || rendered.rect.height <= 0f)
                throw new InvalidOperationException("Jotunn returned no usable " + what + " icon.");
            piece.m_icon = rendered;
        }
        catch (Exception exception)
        {
            log(char.ToUpperInvariant(what[0]) + what.Substring(1) + " icon fallback for " + id + ": " + SafeFailure.Brief(exception) +
                ". The donor's icon is shown.");
        }
    }

    internal static Vector3 Vector(ClimbPoint point) => new Vector3(point.X, point.Y, point.Z);
}
