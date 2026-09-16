using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Where a person would actually stand at a point.
///
/// Both the placement probe and the walk used <c>ZoneSystem.GetSolidHeight</c>
/// for this, and it answers a different question. It adds a thousand metres to
/// the height it is given and returns the FIRST solid thing below that - so
/// under a roof it returns the roof, and on a platform built over a slope it can
/// only ever return the top of whatever is highest. The companion could not be
/// placed on a platform, could not walk under a roof, and could not find
/// shelter, all for that one reason. Found in game at d430a4f, in a shelter the
/// owner built for exactly this test.
///
/// This asks the real question over a bounded window: of every solid surface
/// between <c>searchUp</c> above and <c>searchDown</c> below the point, the one
/// nearest the point's own height that has at least <see cref="Headroom"/> of
/// clear space above it. Under a roof that is the floor; on a platform it is
/// the platform; on open ground it is the ground. Moving things are ignored the
/// way <c>GetSolidHeight</c> ignores them, and triggers are not
/// surfaces.</summary>
internal static class CompanionFooting
{
    /// <summary>Clear space a surface needs above it to be somewhere a person
    /// stands.</summary>
    public const float Headroom = 1.8f;

    /// <summary>Main thread only, like everything else that touches physics
    /// here, so one shared buffer is safe and saves an allocation per
    /// probe.</summary>
    private static readonly RaycastHit[] Buffer = new RaycastHit[32];

    /// <summary>The layers <c>ZoneSystem</c> treats as solid ground, resolved by
    /// name on first use rather than in a static initialiser, so it is always
    /// read on the main thread after Unity is up.</summary>
    private static int _solidMask = -1;

    /// <summary>What can stand in his way: the same solid layers minus terrain,
    /// which the footing and the slope limit already handle. Characters are not
    /// in it, and neither is <c>piece_nonsolid</c> - he walks through the
    /// player exactly as the player walks through him, and a rug is not a
    /// wall.</summary>
    private static int _obstructionMask = -1;

    /// <summary>His body, for the obstruction sweep: from just above a normal
    /// step, so the floor and a stair are not walls, to head height.</summary>
    private const float SweepBottom = 0.55f;
    private const float SweepTop = 1.6f;
    private const float SweepRadius = 0.3f;

    /// <summary>Whether something solid - a wall, a post, a closed door, a rock,
    /// a tree - stands between his body at <paramref name="from"/> and
    /// <paramref name="to"/>.
    ///
    /// The world collision his walk never had. He is moved by setting his
    /// position, not by physics, so nothing stopped him walking through a wall
    /// but the ground-follow's step limit, and a wall taller than its search
    /// window was not even that. A collider he is already standing inside does
    /// not count against him, which is what Unity's sweep does anyway and is
    /// what keeps a companion placed against a wall from being unable to move at
    /// all.</summary>
    public static bool IsWayBlocked(Vector3 from, Vector3 to)
    {
        return TryFindObstruction(from, to, out _);
    }

    /// <summary>The same question as <see cref="IsWayBlocked"/>, with the answer
    /// named - the piece or object he would walk into, and its layer - so a
    /// walk that stops says why in the log.
    ///
    /// One thing is not in his way even though it is solid: the leaf of a door
    /// that is standing open. Swung open, it sits beside the frame, right where
    /// a route through the doorway passes, and it stopped him inside an open
    /// doorway in game at 2374768. Only the leaf - the part the game moves, the
    /// one with a rigidbody of its own - and only while the door is open; a
    /// shut door, and the frame of an open one, still stop him.</summary>
    public static bool TryFindObstruction(Vector3 from, Vector3 to, out string what)
    {
        what = string.Empty;
        EnsureObstructionMask();

        Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
        float distance = flat.magnitude;
        if (distance < 0.01f)
        {
            return false;
        }

        int count = Physics.CapsuleCastNonAlloc(
            from + (Vector3.up * SweepBottom),
            from + (Vector3.up * SweepTop),
            SweepRadius,
            flat / distance,
            SweepBuffer,
            distance,
            _obstructionMask,
            QueryTriggerInteraction.Ignore);

        Collider? blocker = null;
        float nearest = float.MaxValue;
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = SweepBuffer[index];
            if (hit.collider == null)
            {
                continue;
            }

            // Already overlapping where he stands. A single sweep never reports
            // these, and a companion placed against a wall must still be able to
            // walk away from it.
            if (hit.distance <= 0f && hit.point == Vector3.zero)
            {
                continue;
            }

            if (IsOpenDoorLeaf(hit.collider) || hit.distance >= nearest)
            {
                continue;
            }

            nearest = hit.distance;
            blocker = hit.collider;
        }

        if (blocker == null)
        {
            return false;
        }

        what = Describe(blocker);
        return true;
    }

    private static readonly RaycastHit[] SweepBuffer = new RaycastHit[16];

    private static bool IsOpenDoorLeaf(Collider collider)
    {
        if (collider.attachedRigidbody == null)
        {
            return false;
        }

        Door? door = collider.GetComponentInParent<Door>();
        return door != null && CompanionDoors.StateOf(door) != 0;
    }

    private static string Describe(Collider collider)
    {
        Piece? piece = collider.GetComponentInParent<Piece>();
        string name = piece != null ? piece.gameObject.name : collider.gameObject.name;
        name = name.Replace("(Clone)", string.Empty).Trim();
        return $"{name} ({LayerMask.LayerToName(collider.gameObject.layer)}) at " +
            $"({collider.bounds.center.x:0.0}, {collider.bounds.center.y:0.0}, {collider.bounds.center.z:0.0})";
    }

    /// <summary>Whether something solid already stands where his body would be
    /// if he stood at <paramref name="grounded"/>: the same body and the same
    /// layers as <see cref="IsWayBlocked"/>, asked of one spot instead of a
    /// line.</summary>
    public static bool IsBodyObstructed(Vector3 grounded)
    {
        EnsureObstructionMask();
        return Physics.CheckCapsule(
            grounded + (Vector3.up * SweepBottom),
            grounded + (Vector3.up * SweepTop),
            SweepRadius,
            _obstructionMask,
            QueryTriggerInteraction.Ignore);
    }

    private static void EnsureObstructionMask()
    {
        if (_obstructionMask == -1)
        {
            _obstructionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
        }
    }

    public static bool TryFind(
        Vector3 point, float searchUp, float searchDown, out Vector3 grounded, out Vector3 normal)
    {
        grounded = point;
        normal = Vector3.up;

        if (_solidMask == -1)
        {
            _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        }

        int count = Physics.RaycastNonAlloc(
            point + (Vector3.up * searchUp),
            Vector3.down,
            Buffer,
            searchUp + searchDown,
            _solidMask,
            QueryTriggerInteraction.Ignore);

        int best = -1;
        float bestGap = float.MaxValue;
        for (int index = 0; index < count; index++)
        {
            RaycastHit candidate = Buffer[index];
            if (candidate.collider == null || candidate.collider.attachedRigidbody != null)
            {
                continue;
            }

            float surface = candidate.point.y;
            bool roomToStand = true;
            for (int other = 0; other < count; other++)
            {
                RaycastHit above = Buffer[other];
                if (other == index || above.collider == null || above.collider.attachedRigidbody != null)
                {
                    continue;
                }

                float rise = above.point.y - surface;
                if (rise > 0.05f && rise < Headroom)
                {
                    roomToStand = false;
                    break;
                }
            }

            float gap = Mathf.Abs(surface - point.y);
            if (roomToStand && gap < bestGap)
            {
                bestGap = gap;
                best = index;
            }
        }

        if (best < 0)
        {
            return false;
        }

        grounded = new Vector3(point.x, Buffer[best].point.y, point.z);
        normal = Buffer[best].normal;
        return true;
    }
}
