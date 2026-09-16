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
