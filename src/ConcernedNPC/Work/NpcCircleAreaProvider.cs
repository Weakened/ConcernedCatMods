using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Rebuilds the built-in circle from four numbers: centre x, centre y,
/// centre z, radius in metres.
///
/// <b>Its id comes from the role, and that is not ceremony.</b> A provider id is
/// written into whatever a role persists beside the shape, and is read back
/// after a reload to decide who rebuilds it. A value invented in this package
/// would be a durable name this package owned - the one thing the architecture
/// forbids it, in the same breath as prefab names and file names - and renaming
/// it later would turn every marked area in every save into
/// <see cref="WorkAreaResolution.ProviderMissing"/>. A role that already
/// persists a source enum registers this under whatever text it already writes,
/// and its data directory does not change by a byte.
///
/// <b>Four numbers, in that order, and nothing else.</b> Three is unreadable,
/// five is unreadable. Not "the first four win": a shape with the wrong count is
/// a shape from a different provider or a different version of one, and reading
/// a prefix of it is how an area comes back with somebody else's radius.</summary>
internal sealed class NpcCircleAreaProvider : INpcWorkAreaProvider
{
    private const int NumbersInACircle = 4;

    internal NpcCircleAreaProvider(string providerId)
    {
        ProviderId = providerId ?? string.Empty;
    }

    public string ProviderId { get; }

    /// <summary>The shape a role stores for a circle, so the order of the four
    /// numbers is written down once rather than at every call site.</summary>
    internal static float[] Shape(NpcPoint centre, float radiusMetres) =>
        new[] { centre.X, centre.Y, centre.Z, radiusMetres };

    public NpcWorkAreaResult Rebuild(NpcWorkAreaDescriptor descriptor)
    {
        IReadOnlyList<float> shape = descriptor.Shape;
        if (shape.Count != NumbersInACircle)
        {
            return NpcWorkAreaResult.ShapeUnreadable(
                "a circle is four numbers - centre x, y, z and a radius - and this one has " +
                shape.Count.ToString(CultureInfo.InvariantCulture));
        }

        var centre = new NpcPoint(shape[0], shape[1], shape[2]);
        return NpcCircleWorkArea.TryCreate(centre, shape[3], descriptor.Describe, out NpcCircleWorkArea? area, out string reason)
            && area != null
            ? NpcWorkAreaResult.Resolved(area)
            : NpcWorkAreaResult.ShapeUnreadable(reason);
    }
}
