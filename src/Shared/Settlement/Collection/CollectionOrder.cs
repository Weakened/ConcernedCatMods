using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection;

/// <summary>What an order may collect in this slice: the items the game gives
/// for natural loose stones and branches, and nothing else (GATHER-01, D13).
/// </summary>
internal enum CollectedResource
{
    Unspecified = 0,
    Stone = 1,
    Wood = 2,
}

internal static class CollectedResources
{
    /// <summary>Upper bound of one resource in one order.</summary>
    public const int MaxQuota = 500;

    /// <summary>The game's item prefab name for a resource.</summary>
    public static string ItemPrefabName(CollectedResource resource)
    {
        switch (resource)
        {
            case CollectedResource.Stone:
                return "Stone";
            case CollectedResource.Wood:
                return "Wood";
            default:
                throw new ArgumentOutOfRangeException(nameof(resource), "Not a collectable resource.");
        }
    }

    public static bool TryFromItemPrefabName(string? prefabName, out CollectedResource resource)
    {
        switch (prefabName)
        {
            case "Stone":
                resource = CollectedResource.Stone;
                return true;
            case "Wood":
                resource = CollectedResource.Wood;
                return true;
            default:
                resource = CollectedResource.Unspecified;
                return false;
        }
    }
}

/// <summary>How many NEW units of one resource the order must deliver.
/// Pre-existing chest or cart contents never count toward it.</summary>
internal readonly struct ResourceQuota
{
    public ResourceQuota(CollectedResource resource, int requested)
    {
        if (resource == CollectedResource.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(resource), "A quota needs a resource.");
        }

        if (requested < 1 || requested > CollectedResources.MaxQuota)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested), "A quota is 1-" + CollectedResources.MaxQuota + " units.");
        }

        Resource = resource;
        Requested = requested;
    }

    public CollectedResource Resource { get; }

    public int Requested { get; }
}

/// <summary>Where the work area of an order came from (GATHER-02, D11).</summary>
internal enum WorkScopeSource
{
    Unspecified = 0,

    /// <summary>The player's harvest designation circle, as it stood at
    /// acceptance.</summary>
    HarvestDesignation = 1,

    /// <summary>No area assigned: the previewed circle around the latest valid
    /// respawn anchor.</summary>
    DefaultCampCircle = 2,

    /// <summary>A Cartographer work area, through its capability (later).
    /// </summary>
    CartographerWorkArea = 3,
}

/// <summary>The work area an order was accepted against, frozen. A later edit
/// to the designation, the bed or the map never silently moves or widens an
/// accepted order; the order revalidates at a safe checkpoint and pauses when
/// its scope no longer holds.</summary>
internal sealed class WorkScope
{
    public const float DefaultCampRadiusMetres = 30f;
    public const float MinRadiusMetres = 4f;
    public const float MaxRadiusMetres = 48f;

    public WorkScope(
        WorkScopeSource source, SitePoint centre, float radiusMetres, string anchorDescription,
        int sourceRevision, Guid worldLoadEpoch)
    {
        if (source == WorkScopeSource.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "A scope needs a source.");
        }

        if (!(radiusMetres >= MinRadiusMetres) || radiusMetres > MaxRadiusMetres)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMetres), "A scope radius is 4-48 m.");
        }

        if (worldLoadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A scope belongs to one world load.", nameof(worldLoadEpoch));
        }

        Source = source;
        Centre = centre;
        RadiusMetres = radiusMetres;
        AnchorDescription = anchorDescription ?? string.Empty;
        SourceRevision = sourceRevision;
        WorldLoadEpoch = worldLoadEpoch;
    }

    public WorkScopeSource Source { get; }

    public SitePoint Centre { get; }

    public float RadiusMetres { get; }

    /// <summary>For the player: "your bed", "the world start", "harvest area".
    /// </summary>
    public string AnchorDescription { get; }

    /// <summary>The revision of whatever the scope was copied from (the
    /// designation register, the anchor), to detect that it has since changed.
    /// </summary>
    public int SourceRevision { get; }

    public Guid WorldLoadEpoch { get; }

    public bool Contains(SitePoint point) => Centre.HorizontalDistanceTo(point) <= RadiusMetres;
}

internal enum DeliveryKind
{
    Unspecified = 0,

    /// <summary>A container the player selected for this order.</summary>
    Container = 1,

    /// <summary>Thorstein keeps the materials until the player takes them.
    /// Reported as HoldingForPlayer, never as Delivered.</summary>
    HoldForPlayer = 2,
}

/// <summary>Where an order's materials go. A container is named by its
/// in-session key and the world-load epoch it was selected in; after a reload
/// it must be selected again. Never substituted with the nearest chest.
/// </summary>
internal readonly struct DeliveryTarget
{
    private DeliveryTarget(DeliveryKind kind, string containerKey, Guid worldLoadEpoch, SitePoint position)
    {
        Kind = kind;
        ContainerKey = containerKey;
        WorldLoadEpoch = worldLoadEpoch;
        Position = position;
    }

    public DeliveryKind Kind { get; }

    public string ContainerKey { get; }

    public Guid WorldLoadEpoch { get; }

    public SitePoint Position { get; }

    public static DeliveryTarget ToContainer(string containerKey, Guid worldLoadEpoch, SitePoint position)
    {
        if (string.IsNullOrEmpty(containerKey))
        {
            throw new ArgumentException("A container target needs its key.", nameof(containerKey));
        }

        if (worldLoadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A container target belongs to one world load.", nameof(worldLoadEpoch));
        }

        return new DeliveryTarget(DeliveryKind.Container, containerKey, worldLoadEpoch, position);
    }

    public static DeliveryTarget HoldForPlayer() =>
        new DeliveryTarget(DeliveryKind.HoldForPlayer, string.Empty, Guid.Empty, default);
}

internal enum ParticipationMode
{
    Unspecified = 0,

    /// <summary>Thorstein walks every load to the destination himself.</summary>
    Solo = 1,

    /// <summary>Gunnar and an assigned cart carry loads (COOP-02).</summary>
    WithHauler = 2,
}

internal enum CollectionOrderRefusal
{
    Unspecified = 0,
    NoAuthority = 1,
    NoQuotas = 2,
    DuplicateResource = 3,
    ScopeMissing = 4,
    DeliveryMissing = 5,
    WorkerNotRecruited = 6,
    WorkerNotReady = 7,
    AnotherOrderActive = 8,
    JournalReadOnly = 9,
    HaulerUnavailable = 10,
}

/// <summary>An accepted collection order (GATHER-01): the worker, what and how
/// much NEW material, where to collect and where to deliver. Immutable once
/// accepted; changing any of it is a new order.</summary>
internal sealed class CollectionOrderDefinition
{
    public CollectionOrderDefinition(
        OrderId order,
        WorkerId worker,
        IReadOnlyList<ResourceQuota> quotas,
        WorkScope scope,
        DeliveryTarget delivery,
        ParticipationMode participation,
        string issuedByCharacter)
    {
        if (order.IsEmpty || worker.IsEmpty)
        {
            throw new ArgumentException("An order needs an id and a worker.");
        }

        Order = order;
        Worker = worker;
        Quotas = quotas ?? throw new ArgumentNullException(nameof(quotas));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Delivery = delivery;
        Participation = participation;
        IssuedByCharacter = issuedByCharacter ?? string.Empty;
    }

    public OrderId Order { get; }

    public WorkerId Worker { get; }

    public IReadOnlyList<ResourceQuota> Quotas { get; }

    public WorkScope Scope { get; }

    public DeliveryTarget Delivery { get; }

    public ParticipationMode Participation { get; }

    public string IssuedByCharacter { get; }

    /// <summary>The shape checks that need no world: quotas present and
    /// distinct, a delivery and a participation mode chosen.</summary>
    public CollectionOrderRefusal CheckShape()
    {
        if (Quotas.Count == 0)
        {
            return CollectionOrderRefusal.NoQuotas;
        }

        var seen = new HashSet<CollectedResource>();
        foreach (ResourceQuota quota in Quotas)
        {
            if (!seen.Add(quota.Resource))
            {
                return CollectionOrderRefusal.DuplicateResource;
            }
        }

        if (Delivery.Kind == DeliveryKind.Unspecified)
        {
            return CollectionOrderRefusal.DeliveryMissing;
        }

        return Participation == ParticipationMode.Unspecified
            ? CollectionOrderRefusal.HaulerUnavailable
            : CollectionOrderRefusal.Unspecified;
    }
}

/// <summary>The life of a collection order. Zero is unspecified.</summary>
internal enum CollectionOrderState
{
    Unspecified = 0,
    Accepted = 1,
    Surveying = 2,
    Collecting = 3,
    WaitingForHauler = 4,
    Delivering = 5,

    /// <summary>Hold-for-player mode: the requested amounts are carried and
    /// wait for the player to take them.</summary>
    HoldingForPlayer = 6,

    Paused = 7,

    /// <summary>Stopped with one actionable reason and retained evidence.
    /// </summary>
    NeedsAttention = 8,

    Completed = 9,

    /// <summary>Ended by the player. Not a refund: delivered material stays
    /// delivered, carried and cart material stays where it physically is and is
    /// reported until explicitly moved.</summary>
    Cancelled = 10,
}

internal static class CollectionOrderStates
{
    private static readonly Dictionary<CollectionOrderState, CollectionOrderState[]> Legal =
        new Dictionary<CollectionOrderState, CollectionOrderState[]>
        {
            [CollectionOrderState.Accepted] = new[]
            {
                CollectionOrderState.Surveying, CollectionOrderState.Paused, CollectionOrderState.NeedsAttention,
                CollectionOrderState.Cancelled,
            },
            [CollectionOrderState.Surveying] = new[]
            {
                CollectionOrderState.Collecting, CollectionOrderState.Paused, CollectionOrderState.NeedsAttention,
                CollectionOrderState.Cancelled,
            },
            [CollectionOrderState.Collecting] = new[]
            {
                CollectionOrderState.Surveying, CollectionOrderState.WaitingForHauler, CollectionOrderState.Delivering,
                CollectionOrderState.HoldingForPlayer, CollectionOrderState.Paused,
                CollectionOrderState.NeedsAttention, CollectionOrderState.Cancelled,
            },
            [CollectionOrderState.WaitingForHauler] = new[]
            {
                CollectionOrderState.Collecting, CollectionOrderState.Delivering, CollectionOrderState.Paused,
                CollectionOrderState.NeedsAttention, CollectionOrderState.Cancelled,
            },
            [CollectionOrderState.Delivering] = new[]
            {
                CollectionOrderState.Collecting, CollectionOrderState.Completed, CollectionOrderState.Paused,
                CollectionOrderState.NeedsAttention, CollectionOrderState.Cancelled,
            },
            [CollectionOrderState.HoldingForPlayer] = new[]
            {
                CollectionOrderState.Completed, CollectionOrderState.Paused, CollectionOrderState.NeedsAttention,
                CollectionOrderState.Cancelled,
            },
            [CollectionOrderState.Paused] = new[]
            {
                CollectionOrderState.Surveying, CollectionOrderState.Collecting, CollectionOrderState.WaitingForHauler,
                CollectionOrderState.Delivering, CollectionOrderState.HoldingForPlayer,
                CollectionOrderState.NeedsAttention, CollectionOrderState.Cancelled,
            },
            [CollectionOrderState.NeedsAttention] = new[]
            {
                CollectionOrderState.Paused, CollectionOrderState.Cancelled,
            },
        };

    public static bool CanTransition(CollectionOrderState from, CollectionOrderState to) =>
        from != to && Legal.TryGetValue(from, out CollectionOrderState[]? targets) && Array.IndexOf(targets, to) >= 0;

    public static bool IsTerminal(CollectionOrderState state) =>
        state == CollectionOrderState.Completed || state == CollectionOrderState.Cancelled;
}

/// <summary>Per-resource progress with every unit in exactly one bucket
/// (COOP-04). Estimates are separate and never count as progress.</summary>
internal sealed class ResourceProgress
{
    public ResourceProgress(CollectedResource resource, int requested)
    {
        Resource = resource;
        Requested = requested;
    }

    public CollectedResource Resource { get; }

    public int Requested { get; }

    /// <summary>Estimated yield of sources reserved for this order. Not
    /// material; never subtracted from what is still needed.</summary>
    public int ReservedEstimate { get; set; }

    /// <summary>Spawned by this order's pick and still on the ground.</summary>
    public int OnGround { get; set; }

    public int Carried { get; set; }

    public int InCart { get; set; }

    public int Delivered { get; set; }

    /// <summary>Handed to the player in hold-for-player mode.</summary>
    public int HandedOver { get; set; }

    /// <summary>Observed destroyed or removed by someone else; never silently
    /// restored.</summary>
    public int Lost { get; set; }

    /// <summary>Material this order already holds or has placed.</summary>
    public int Committed => OnGround + Carried + InCart + Delivered + HandedOver;

    /// <summary>What must still be picked up.</summary>
    public int StillToCollect => Math.Max(0, Requested - Committed);

    public bool IsDelivered => Delivered + HandedOver >= Requested;
}
