using System;
using System.Globalization;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Designations;

/// <summary>What a player marked.
///
/// Each value is one of the four explicit acts CF-SET-004 exists to make
/// possible, and the kind is also the <b>key</b>: the first proof holds one
/// settlement area, one harvest area and one supply container, so there is
/// nothing else for a designation to be identified by. Giving them separate
/// names would invent structure the proof does not use, and would make
/// "mark the settlement area again" ambiguous between updating one and adding
/// a second.
///
/// <see cref="None"/> is zero so that a default-constructed designation is not
/// a real one. Every place that reads a kind treats <see cref="None"/> as a
/// refusal rather than as a settlement area.</summary>
internal enum DesignationKind
{
    /// <summary>Not a designation. The default, and never valid.</summary>
    None = 0,

    /// <summary>The ground the settlement occupies. Everything else belongs to
    /// it, and removing it removes them.</summary>
    SettlementArea = 1,

    /// <summary>The only ground a worker may fell trees on. Deliberately
    /// separate from the settlement area: trees are not usually in your
    /// base.</summary>
    HarvestArea = 2,

    /// <summary>The one chest a worker may take from. Not "the nearest chest",
    /// not "any chest in the area" — this one.</summary>
    SupplyContainer = 3,
}

/// <summary>One marked designation.
///
/// Immutable on purpose. A designation is a record of something a player did,
/// and the only ways to change one are to replace it with an explicit new act
/// or to remove it — both of which go through
/// <see cref="DesignationBook"/> so that the refusal rules cannot be
/// sidestepped by mutating a field.</summary>
internal sealed class Designation
{
    internal Designation(DesignationKind kind, SitePoint centre, float radius, string? containerKey)
    {
        if (kind == DesignationKind.None)
        {
            throw new ArgumentException("A designation needs a kind.", nameof(kind));
        }

        if (kind == DesignationKind.SupplyContainer)
        {
            if (string.IsNullOrEmpty(containerKey))
            {
                throw new ArgumentException(
                    "A supply container designation needs the container's own identity. " +
                    "A position is not enough: chests can be moved and rebuilt, and a " +
                    "designation that resolves by position would silently follow whatever " +
                    "ends up standing there.",
                    nameof(containerKey));
            }

            if (radius != 0f)
            {
                throw new ArgumentException(
                    "A supply container is one container, not an area.", nameof(radius));
            }
        }
        else
        {
            if (containerKey != null)
            {
                throw new ArgumentException(
                    "Only a supply container designation names a container.", nameof(containerKey));
            }

            if (!(radius > 0f))
            {
                throw new ArgumentException("An area designation needs a radius.", nameof(radius));
            }
        }

        Kind = kind;
        Centre = centre;
        Radius = radius;
        ContainerKey = containerKey;
    }

    public DesignationKind Kind { get; }

    /// <summary>Where it is. For a container, where it stood when it was
    /// designated — kept for showing the player, never for resolving which
    /// container is meant.</summary>
    public SitePoint Centre { get; }

    /// <summary>Zero for a supply container; strictly positive for an area.</summary>
    public float Radius { get; }

    /// <summary>The container's own identity, supplied by the adapter. Opaque
    /// here: this layer never interprets it, it only remembers it and compares
    /// it for equality.</summary>
    public string? ContainerKey { get; }

    public bool IsArea => Kind != DesignationKind.SupplyContainer;

    /// <summary>True when a point is inside this area.
    ///
    /// Horizontal only, matching <see cref="SitePoint"/>'s own convention: a
    /// tree two metres up a bank at the edge of the harvest area is in the
    /// harvest area, because that is what a person marking it meant. A
    /// container designation contains nothing — it is not an area, and
    /// answering "yes" for the point it happens to stand on would let a caller
    /// treat it as one.</summary>
    public bool Contains(SitePoint point)
    {
        return IsArea && Centre.HorizontalDistanceTo(point) <= Radius;
    }

    /// <summary>True when two designations describe the same act.
    ///
    /// This is what makes repeated marking idempotent, and it is deliberately
    /// exact rather than approximate. "Close enough" would mean a player who
    /// nudged the centre by a metre got silently told nothing had changed.</summary>
    public bool SameAs(Designation other)
    {
        return other != null
            && other.Kind == Kind
            && other.Centre.Equals(Centre)
            && other.Radius.Equals(Radius)
            && string.Equals(other.ContainerKey, ContainerKey, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Kind == DesignationKind.SupplyContainer
            ? "supply container " + ContainerKey + " at " + Centre
            : string.Format(
                CultureInfo.InvariantCulture, "{0} at {1}, radius {2:0.#} m", Kind, Centre, Radius);
    }
}

/// <summary>One player's request to mark something.
///
/// A request is not a designation: it is what the player asked for, before any
/// check has been made. Keeping the two types apart is what stops an unchecked
/// request being stored by accident, because nothing but
/// <see cref="DesignationBook"/> can turn one into the other.</summary>
internal readonly struct DesignationRequest
{
    private DesignationRequest(
        DesignationKind kind, SitePoint centre, float radius, string? containerKey)
    {
        Kind = kind;
        Centre = centre;
        Radius = radius;
        ContainerKey = containerKey;
    }

    public DesignationKind Kind { get; }
    public SitePoint Centre { get; }
    public float Radius { get; }
    public string? ContainerKey { get; }

    public static DesignationRequest Area(DesignationKind kind, SitePoint centre, float radius)
    {
        return new DesignationRequest(kind, centre, radius, null);
    }

    public static DesignationRequest Container(SitePoint at, string? containerKey)
    {
        return new DesignationRequest(DesignationKind.SupplyContainer, at, 0f, containerKey);
    }
}
