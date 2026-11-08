using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>The trapdoor setting (CL-004 #485, slice 2). Off by default: with it off no trapdoor is cloned, no token
/// is added and the hammer menu is exactly as before. The lid runtime (slice 3) and the ladder integration (slice 4)
/// are not built yet, so a placed trapdoor's lid stays shut while its native door state still toggles.</summary>
internal static class TrapdoorSettings
{
    internal const string Section = "Trapdoors";

    internal const string Key = "Enabled";

    internal const bool EnabledByDefault = false;

    internal const string Description =
        "UNFINISHED (CL-004): adds the nine floor trapdoors to the hammer's Building tab. Their lids do not open yet. " +
        "Leave this off. Turning it off again, or uninstalling, makes the game delete every placed trapdoor the next " +
        "time a world loads, with no refund. Restart after changing it.";
}

/// <summary>What a trapdoor part is for. Visual parts carry a renderer and no collider; collider parts carry a box
/// collider and no renderer, so a wear-state swap never changes collision.</summary>
internal enum TrapdoorPartRole
{
    FrameVisual,
    FrameCollider,
    LidVisual,
    LidCollider,
    HingeStrap,
}

/// <summary>One box of the hatch, in its parent's local metres (the parent is the piece root for the frame and the
/// hinge pivot for the lid).</summary>
internal readonly struct TrapdoorPart
{
    internal TrapdoorPart(string name, TrapdoorPartRole role, ClimbPoint centre, ClimbPoint size)
    {
        Name = name;
        Role = role;
        Centre = centre;
        Size = size;
    }

    internal string Name { get; }

    internal TrapdoorPartRole Role { get; }

    internal ClimbPoint Centre { get; }

    internal ClimbPoint Size { get; }
}

/// <summary>The donor's own materials the hatch borrows, by name, read from the donor's renderers before anything is
/// stripped. <see cref="FrameWorn"/> is null exactly where the donor points <c>m_new</c>, <c>m_worn</c> and
/// <c>m_broken</c> at one object (iron, hexagonal, flametal): those trapdoors keep one presentation root too.</summary>
internal readonly struct TrapdoorMaterials
{
    internal TrapdoorMaterials(string lid, string frame, string? frameWorn)
    {
        Lid = lid;
        Frame = frame;
        FrameWorn = frameWorn;
    }

    /// <summary>The donor leaf's material, for the lid.</summary>
    internal string Lid { get; }

    /// <summary>The donor's new-state frame material, for the frame.</summary>
    internal string Frame { get; }

    /// <summary>The donor's worn-state material, or null when the donor has one presentation root.</summary>
    internal string? FrameWorn { get; }
}

/// <summary>One resource requirement as read from a <c>Piece.m_resources</c> entry (the item's object name, which is
/// <c>JVLmock_</c> + the prefab name on a Jötunn clone until Jötunn resolves its mocks).</summary>
internal readonly struct ObservedRequirement
{
    internal ObservedRequirement(string? item, int amount, bool recover)
    {
        Item = item;
        Amount = amount;
        Recover = recover;
    }

    internal string? Item { get; }

    internal int Amount { get; }

    internal bool Recover { get; }
}

/// <summary>What the installed donor prefab says, read by the piece adapter before it clones anything. Game-free so
/// the comparison against the pinned matrix is tested.</summary>
internal sealed class DonorFacts
{
    internal bool HasPiece { get; set; }

    internal bool HasWearNTear { get; set; }

    internal bool HasDoor { get; set; }

    internal bool HasNetView { get; set; }

    /// <summary>An <c>Animator</c> on the donor root itself: <c>Door.Awake</c> takes the first one in its children,
    /// and the root's must be that one.</summary>
    internal bool HasRootAnimator { get; set; }

    /// <summary>The direct child the donor's animator drives (<see cref="TrapdoorBlueprint.LeafChild"/>).</summary>
    internal bool HasLeaf { get; set; }

    internal string? Station { get; set; }

    internal IReadOnlyList<ObservedRequirement> Requirements { get; set; } = Array.Empty<ObservedRequirement>();

    internal float Health { get; set; }

    internal int Material { get; set; }

    internal bool Burnable { get; set; }

    internal string? DoorName { get; set; }

    internal bool HasKeyItem { get; set; }

    internal bool CanNotBeClosed { get; set; }

    internal bool ChecksGuardStone { get; set; }

    internal bool HasOpenEnable { get; set; }

    internal string? OpenEffect { get; set; }

    internal string? CloseEffect { get; set; }
}

/// <summary>The floor trapdoor's build plan (CL-004 #485, slice 2): which donor children go, which names the hatch's
/// own parts carry, and every box of frame, lid and hinge hardware, all from <see cref="HatchGeometry"/>. The piece
/// adapter (<c>Pieces/TrapdoorPieceRegistration</c>) only walks this plan, so the shape is tested here. Design
/// defaults, not in-game observations: every placed-piece row is OWNER GO-AROUND PENDING.</summary>
internal static class TrapdoorBlueprint
{
    /// <summary>The donor's animated leaf. It stays as a bare transform for the hidden native animator to write;
    /// every other donor child is removed (DOOR_DONOR_MATRIX.md section 4).</summary>
    internal const string LeafChild = "door";

    /// <summary>The new-state presentation root (<c>WearNTear.m_new</c>).</summary>
    internal const string NewRoot = "new";

    /// <summary>The worn-state presentation root (<c>m_worn</c> and <c>m_broken</c>), present only when the donor has a
    /// worn material.</summary>
    internal const string WornRoot = "worn";

    /// <summary>The lid's pivot: on the hinge axis, carrying the kinematic body, the lid's collider and its visuals.
    /// The lid runtime (slice 3) turns this one transform about local x.</summary>
    internal const string Hinge = "lid_hinge";

    /// <summary>Jötunn's prefix for a mocked prefab reference (<c>MockManager.JVLMockPrefix</c> in Jötunn 2.30.2).</summary>
    internal const string MockPrefix = "JVLmock_";

    /// <summary>The usage tags every trapdoor gets, replacing the donor's (Jötunn sets <c>Piece.m_usage</c> from these).</summary>
    internal static readonly IReadOnlyList<string> Usage = new[] { "Building", "Doors" };

    /// <summary>The hinge pivot in piece space: on the lid's top rear edge (<see cref="HatchGeometry.HingeY"/>,
    /// <see cref="HatchGeometry.HingeZ"/>).</summary>
    internal static ClimbPoint HingePosition => new ClimbPoint(0f, HatchGeometry.HingeY, HatchGeometry.HingeZ);

    /// <summary>Thickness of the hinge straps that sit on the lid's top at the hinge edge. They are the ghost's hinge
    /// marker (the hinge side is always the piece's rear) and carry no collider.</summary>
    internal const float StrapThickness = 0.012f;

    internal const float StrapWidth = 0.08f;

    internal const float StrapLength = 0.36f;

    /// <summary>Strap centres on x, inboard of the lid's sides.</summary>
    internal const float StrapX = 0.5f;

    private static readonly string[] FrameNames = { "frame_front", "frame_rear", "frame_left", "frame_right" };

    /// <summary>The four frame bands as visual boxes in piece space (one set under each presentation root).</summary>
    internal static readonly IReadOnlyList<TrapdoorPart> FrameVisuals = BuildFrame(TrapdoorPartRole.FrameVisual);

    /// <summary>The four frame bands as static box colliders in piece space. They never move, and they are the
    /// colliders <c>WearNTear</c> measures support with (it skips colliders on a rigidbody, so not the lid).</summary>
    internal static readonly IReadOnlyList<TrapdoorPart> FrameColliders = BuildFrame(TrapdoorPartRole.FrameCollider);

    /// <summary>The lid's box in hinge space: hanging forward (-z) and down from the hinge, so turning the hinge by
    /// <c>Quaternion.Euler(a, 0, 0)</c> gives exactly <see cref="HatchGeometry.Lid"/>(a).</summary>
    internal static TrapdoorPart LidCollider => Lid("lid_collider", TrapdoorPartRole.LidCollider);

    internal static TrapdoorPart LidVisual => Lid("lid", TrapdoorPartRole.LidVisual);

    /// <summary>Two straps on the lid's top at the hinge edge, in hinge space.</summary>
    internal static readonly IReadOnlyList<TrapdoorPart> HingeStraps = new[]
    {
        Strap("hinge_strap_left", -StrapX),
        Strap("hinge_strap_right", StrapX),
    };

    internal const string DescriptionToken = "cl_trapdoor_description";

    /// <summary>The English localization the trapdoors add (tokens without <c>$</c>): one Title Case name per family
    /// and the shared description. Open/close hover text is the game's own Door text and is not duplicated.</summary>
    internal static Dictionary<string, string> English()
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DescriptionToken] = "A 2 m floor hatch with a hinged lid, flush with a wood floor. Opens and closes like its door.",
        };
        foreach (DoorFamily family in DoorFamilies.All) { english[family.TrapdoorToken] = family.EnglishTrapdoor; }
        return english;
    }

    /// <summary>The donor materials each family borrows, read from census.json's renderer lists (Valheim 1.0.21).</summary>
    internal static TrapdoorMaterials MaterialsFor(DoorFamily family)
    {
        switch (family.Donor)
        {
            case "wood_door": return new TrapdoorMaterials("door_wood", "woodwall", "woodwall_worn");
            case "wood_gate": return new TrapdoorMaterials("door_wood", "woodwall", "woodwall_worn");
            case "wood_fence_gate": return new TrapdoorMaterials("fence_gate_mat", "fence_gate_mat", "fence_gate_worn_mat");
            case "iron_grate": return new TrapdoorMaterials("fi_village_metal", "metalwall", null);
            case "darkwood_gate": return new TrapdoorMaterials("DarkWoodGate_door", "DarkWoodGate_mat", "DarkWoodGate_worn");
            case "stave_gate": return new TrapdoorMaterials("TimberGate_mat", "TimberGate_mat", "TimberGate_worn_mat");
            case "piece_hexagonal_door": return new TrapdoorMaterials("Hexagonal_mat", "Hexagonal_mat", null);
            case "ashwood_door": return new TrapdoorMaterials("ashwood_door_mat", "ashwood_door_mat", "ashwood_door_worn_mat");
            case "flametal_gate": return new TrapdoorMaterials("flametalgate_mat", "flametalgate_mat", null);
            default: throw new ArgumentException("No pinned materials for donor " + family.Donor, nameof(family));
        }
    }

    /// <summary>The prefab name behind an object name: Jötunn's mock prefix removed, nothing else changed.</summary>
    internal static string? PrefabName(string? objectName) =>
        objectName != null && objectName.StartsWith(MockPrefix, StringComparison.Ordinal)
            ? objectName.Substring(MockPrefix.Length)
            : objectName;

    /// <summary>Every way the observed requirements differ from the family's pinned cost, in order. Empty means equal:
    /// the same items, amounts and order, every one recovered on deconstruction. An empty observed list never
    /// matches (Jötunn keeps the donor's requirements when <c>PieceConfig.Requirements</c> is empty, so emptiness is
    /// not a way to clear them).</summary>
    internal static IReadOnlyList<string> RequirementMismatches(DoorFamily family, IReadOnlyList<ObservedRequirement>? observed)
    {
        var problems = new List<string>();
        if (observed == null)
        {
            problems.Add("requirements unreadable");
            return problems;
        }

        if (observed.Count != family.Costs.Count)
        {
            problems.Add("requirement count " + observed.Count + ", pinned " + family.Costs.Count);
        }

        int shared = Math.Min(observed.Count, family.Costs.Count);
        for (int index = 0; index < shared; ++index)
        {
            ObservedRequirement seen = observed[index];
            DoorCost pinned = family.Costs[index];
            string? item = PrefabName(seen.Item);
            if (!string.Equals(item, pinned.Item, StringComparison.Ordinal))
            {
                problems.Add("requirement " + index + " item " + (item ?? "<none>") + ", pinned " + pinned.Item);
            }

            if (seen.Amount != pinned.Amount)
            {
                problems.Add("requirement " + index + " amount " + seen.Amount + ", pinned " + pinned.Amount);
            }

            if (!seen.Recover)
            {
                problems.Add("requirement " + index + " not recovered on deconstruction");
            }
        }

        return problems;
    }

    /// <summary>Every way the installed donor differs from the pinned matrix row. Empty means the clone may be built;
    /// anything else refuses the family (logged, never registered) instead of guessing.</summary>
    internal static IReadOnlyList<string> DonorMismatches(DoorFamily family, DonorFacts? facts)
    {
        var problems = new List<string>();
        if (facts == null)
        {
            problems.Add("donor prefab " + family.Donor + " not found");
            return problems;
        }

        if (!facts.HasPiece) { problems.Add("no Piece"); }
        if (!facts.HasWearNTear) { problems.Add("no WearNTear"); }
        if (!facts.HasDoor) { problems.Add("no Door"); }
        if (!facts.HasNetView) { problems.Add("no ZNetView"); }
        if (!facts.HasRootAnimator) { problems.Add("no Animator on the root"); }
        if (!facts.HasLeaf) { problems.Add("no '" + LeafChild + "' child"); }
        if (!string.Equals(PrefabName(facts.Station), family.Station, StringComparison.Ordinal))
        {
            problems.Add("station " + (facts.Station ?? "<none>") + ", pinned " + family.Station);
        }

        problems.AddRange(RequirementMismatches(family, facts.Requirements));
        if (!(Math.Abs(facts.Health - family.Health) <= 0.001f))
        {
            problems.Add("health " + facts.Health + ", pinned " + family.Health);
        }

        if (facts.Material != (int)family.Material) { problems.Add("material " + facts.Material + ", pinned " + (int)family.Material); }
        if (facts.Burnable != family.Burnable) { problems.Add("burnable " + facts.Burnable + ", pinned " + family.Burnable); }
        if (!string.Equals(facts.DoorName, family.DonorToken, StringComparison.Ordinal))
        {
            problems.Add("door name " + (facts.DoorName ?? "<none>") + ", pinned " + family.DonorToken);
        }

        if (facts.HasKeyItem) { problems.Add("door needs a key"); }
        if (facts.CanNotBeClosed) { problems.Add("door cannot be closed"); }
        if (!facts.ChecksGuardStone) { problems.Add("door ignores guard stones"); }
        if (facts.HasOpenEnable) { problems.Add("door enables an object when open"); }
        if (!string.Equals(facts.OpenEffect, family.OpenEffect, StringComparison.Ordinal))
        {
            problems.Add("open effect " + (facts.OpenEffect ?? "<none>") + ", pinned " + family.OpenEffect);
        }

        if (!string.Equals(facts.CloseEffect, family.CloseEffect, StringComparison.Ordinal))
        {
            problems.Add("close effect " + (facts.CloseEffect ?? "<none>") + ", pinned " + family.CloseEffect);
        }

        return problems;
    }

    private static IReadOnlyList<TrapdoorPart> BuildFrame(TrapdoorPartRole role)
    {
        var parts = new TrapdoorPart[HatchGeometry.Frame.Count];
        for (int index = 0; index < parts.Length; ++index)
        {
            HatchBox band = HatchGeometry.Frame[index];
            parts[index] = new TrapdoorPart(FrameNames[index], role, band.Centre, band.Size);
        }

        return parts;
    }

    private static TrapdoorPart Lid(string name, TrapdoorPartRole role) =>
        new TrapdoorPart(name, role,
            new ClimbPoint(0f, -HatchGeometry.LidThickness / 2f, -HatchGeometry.HalfLid),
            new ClimbPoint(HatchGeometry.LidLength, HatchGeometry.LidThickness, HatchGeometry.LidLength));

    private static TrapdoorPart Strap(string name, float x) =>
        new TrapdoorPart(name, TrapdoorPartRole.HingeStrap,
            new ClimbPoint(x, StrapThickness / 2f, -StrapLength / 2f),
            new ClimbPoint(StrapWidth, StrapThickness, StrapLength));
}
