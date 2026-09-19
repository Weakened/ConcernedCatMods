using TheConcernedCat.Settlement.Worker;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime;

/// <summary>The one place a Unity <see cref="Vector3"/> becomes a
/// <see cref="SitePoint"/> and back.
///
/// <b>Why it is its own file.</b> <c>SitePoint</c> lives in the shared settlement
/// layer, which must contain no Unity type, so the conversion cannot live beside
/// it. Left to the adapters, each one wrote its own pair: there were three
/// identical copies across this product and the Steward before this existed, and
/// a fourth was about to be added by the housing survey. Nothing here needs
/// anything but the two types it converts, which is also what lets a test project
/// compile it without dragging a product's whole engine surface along.</summary>
internal static class SitePoints
{
    public static SitePoint ToSitePoint(Vector3 point) => new SitePoint(point.x, point.y, point.z);

    public static Vector3 ToVector3(SitePoint point) => new Vector3(point.X, point.Y, point.Z);
}
