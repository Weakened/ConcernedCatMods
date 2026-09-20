using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A work area shape this package has never heard of, written entirely
/// outside it.
///
/// <b>This is the proof, and it is the whole reason it lives in the test
/// project.</b> The issue asks for a richer freehand or polygon area to be
/// registered from outside, without ConcernedNPC depending on the mod that draws
/// one. Nothing in <c>src/ConcernedNPC</c> mentions a polygon, and this type
/// resolves through exactly the same registry call the built-in circle uses -
/// there is no privileged path for the circle, so there is no path that could
/// quietly become a fallback for everything else.
///
/// The shape is corner pairs: x, z, x, z, and so on, at least three corners.
/// Containment is the ordinary crossing test, height ignored, like every area
/// in this repository.</summary>
internal sealed class PolygonWorkArea : INpcWorkArea
{
    private readonly IReadOnlyList<float> _corners;

    internal PolygonWorkArea(
        IReadOnlyList<float> corners, NpcPoint boundingCentre, float boundingRadius, string describe, int revision)
    {
        _corners = corners;
        BoundingCentre = boundingCentre;
        BoundingRadiusMetres = boundingRadius;
        Describe = describe;
        Revision = revision;
    }

    public NpcPoint BoundingCentre { get; }

    public float BoundingRadiusMetres { get; }

    public int Revision { get; }

    public string Describe { get; }

    public bool Contains(NpcPoint point)
    {
        if (!point.IsFinite)
        {
            return false;
        }

        bool inside = false;
        int count = _corners.Count / 2;
        for (int index = 0, previous = count - 1; index < count; previous = index++)
        {
            float xi = _corners[index * 2];
            float zi = _corners[(index * 2) + 1];
            float xj = _corners[previous * 2];
            float zj = _corners[(previous * 2) + 1];

            if (zi > point.Z != zj > point.Z
                && point.X < ((xj - xi) * (point.Z - zi) / (zj - zi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>The outside provider. Registered by a test, under an id a test
/// chose, exactly as a map mod would register its own.</summary>
internal sealed class PolygonAreaProvider : INpcWorkAreaProvider
{
    internal PolygonAreaProvider(string providerId)
    {
        ProviderId = providerId;
    }

    public string ProviderId { get; }

    public NpcWorkAreaResult Rebuild(NpcWorkAreaDescriptor descriptor)
    {
        IReadOnlyList<float> shape = descriptor.Shape;
        if (shape.Count < 6 || shape.Count % 2 != 0)
        {
            return NpcWorkAreaResult.ShapeUnreadable("a polygon is at least three corners, two numbers each");
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        for (int index = 0; index < shape.Count; index += 2)
        {
            minX = System.Math.Min(minX, shape[index]);
            maxX = System.Math.Max(maxX, shape[index]);
            minZ = System.Math.Min(minZ, shape[index + 1]);
            maxZ = System.Math.Max(maxZ, shape[index + 1]);
        }

        var centre = new NpcPoint((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
        float radius = new NpcPoint(minX, 0f, minZ).HorizontalDistanceTo(centre) + 1f;
        return NpcWorkAreaResult.Resolved(
            new PolygonWorkArea(
                shape, centre, radius, descriptor.Describe, NpcAreaRevision.ForShape(descriptor.Id.Key, shape)));
    }
}

/// <summary>A provider whose id getter throws, and one that fails while
/// rebuilding: a badly written mod must not take the registry with it.</summary>
internal sealed class BrokenAreaProvider : INpcWorkAreaProvider
{
    private readonly bool _throwOnId;

    internal BrokenAreaProvider(string providerId, bool throwOnId)
    {
        Id = providerId;
        _throwOnId = throwOnId;
    }

    private string Id { get; }

    public string ProviderId => _throwOnId
        ? throw new System.InvalidOperationException("this provider cannot say what it is called")
        : Id;

    public NpcWorkAreaResult Rebuild(NpcWorkAreaDescriptor descriptor) =>
        throw new System.InvalidOperationException("this provider fell over while rebuilding");
}

/// <summary>A provider that claims success and hands back nothing, which is the
/// shape of a bug that would otherwise become a null area inside a job.</summary>
internal sealed class EmptySuccessProvider : INpcWorkAreaProvider
{
    internal EmptySuccessProvider(string providerId)
    {
        ProviderId = providerId;
    }

    public string ProviderId { get; }

    public NpcWorkAreaResult Rebuild(NpcWorkAreaDescriptor descriptor) => default;
}

/// <summary>An area whose containment question throws, for the fail-closed
/// paths.</summary>
internal sealed class ThrowingWorkArea : INpcWorkArea
{
    public bool Contains(NpcPoint point) => throw new System.InvalidOperationException("this area cannot answer");

    public NpcPoint BoundingCentre => new NpcPoint(0f, 0f, 0f);

    public float BoundingRadiusMetres => 20f;

    public int Revision => 1;

    public string Describe => "an area that cannot answer";
}

/// <summary>A probe a test drives, which also records every point it was asked
/// about so the order can be pinned.</summary>
internal sealed class RecordingProbe : INpcAreaProbe
{
    private readonly System.Func<NpcPoint, AreaSample> _answer;

    internal RecordingProbe(System.Func<NpcPoint, AreaSample> answer)
    {
        _answer = answer;
    }

    internal List<NpcPoint> Asked { get; } = new List<NpcPoint>();

    internal static RecordingProbe Standable() =>
        new RecordingProbe(point => new AreaSample(AreaSampleVerdict.Standable, point, AreaRejection.None));

    internal static RecordingProbe Answering(AreaSampleVerdict verdict) =>
        new RecordingProbe(point => new AreaSample(
            verdict,
            point,
            verdict == AreaSampleVerdict.Rejected ? AreaRejection.TooSteep : AreaRejection.None));

    public AreaSample Probe(NpcPoint point)
    {
        Asked.Add(point);
        return _answer(point);
    }
}

/// <summary>A caller's own opinion about a standable point.</summary>
internal sealed class FixedTargetFilter : INpcTargetFilter
{
    private readonly System.Func<NpcPoint, NpcTargetVerdict> _verdict;

    internal FixedTargetFilter(System.Func<NpcPoint, NpcTargetVerdict> verdict)
    {
        _verdict = verdict;
    }

    internal static FixedTargetFilter Always(NpcTargetVerdict verdict) => new FixedTargetFilter(point => verdict);

    public NpcTargetVerdict Judge(NpcPoint ground) => _verdict(ground);
}
