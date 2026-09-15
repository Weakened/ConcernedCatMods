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
    internal Designation(
        DesignationKind kind,
        SitePoint centre,
        float radius,
        string? containerKey,
        string? identityEpoch = null)
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

        if (kind != DesignationKind.SupplyContainer && identityEpoch != null)
        {
            throw new ArgumentException(
                "Only a supply container has an identity that can go stale.", nameof(identityEpoch));
        }

        Kind = kind;
        Centre = centre;
        Radius = radius;
        ContainerKey = containerKey;
        IdentityEpoch = identityEpoch;
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

    /// <summary>Which run of the world the <see cref="ContainerKey"/> means
    /// anything in.
    ///
    /// <b>This exists because the game gives a placed object no identity that
    /// survives a save.</b> The design originally assumed it did. Decompiling
    /// the installed 1.0.12 build showed otherwise: <c>ZDO.Load</c> opens with
    /// <c>m_uid.SetID(++ZDOID.m_loadID)</c>, so every persisted object is
    /// handed a <i>fresh</i> id in load order, and <c>SetID</c> also forces the
    /// user half to a constant. A key written before a reload therefore names
    /// nothing after it — and because the new ids are dense from one, it is
    /// likely to name some <i>other</i> chest that happens to have loaded in
    /// that position.
    ///
    /// Silently resolving to the wrong chest is the worst outcome available
    /// here: a worker would draw from a container the player never designated.
    /// So the epoch is recorded alongside the key, and a key from a previous
    /// epoch resolves to <b>nothing at all</b> rather than to a guess. The
    /// player is told to mark the chest again, which is one keystroke and is
    /// honest.
    ///
    /// Null for every area designation: ground does not have this problem,
    /// because a circle is described by its own coordinates rather than by a
    /// reference to an object.</summary>
    public string? IdentityEpoch { get; }

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
        if (other == null || other.Kind != Kind)
        {
            return false;
        }

        if (Kind == DesignationKind.SupplyContainer)
        {
            // A container IS its key, within an epoch. Its centre is recorded
            // only to show the player where it stood -- the type's own comment
            // says so -- and comparing it here would mean re-marking a chest
            // that had moved a millimetre, or a chest riding a wagon, was
            // refused as "already marked differently".
            return string.Equals(other.ContainerKey, ContainerKey, StringComparison.Ordinal)
                && string.Equals(other.IdentityEpoch, IdentityEpoch, StringComparison.Ordinal);
        }

        return other.Centre.Equals(Centre) && other.Radius.Equals(Radius);
    }

    /// <summary>True when this is a container designation whose key was written
    /// in a different run of the world and therefore means nothing now.</summary>
    public bool IsStaleIdentity(string? currentEpoch)
    {
        return Kind == DesignationKind.SupplyContainer
            && !string.Equals(IdentityEpoch, currentEpoch, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Kind == DesignationKind.SupplyContainer
            ? "supply container at " + Centre
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

    /// <summary>A request to designate a chest. The epoch is stamped by the
    /// book from whichever run of the world is current, not passed in here,
    /// so a caller cannot claim an identity is fresher than it is.</summary>
    public static DesignationRequest Container(SitePoint at, string? containerKey)
    {
        return new DesignationRequest(DesignationKind.SupplyContainer, at, 0f, containerKey);
    }
}
