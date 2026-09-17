using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling;

/// <summary>Which cart, for one world load only (DECISIONS.md D6): the cart's
/// in-session network id as text, plus the epoch minted when this world was
/// opened. The game renumbers its objects on every load, so a key from another
/// epoch names nothing - or worse, another cart - and is never trusted.
/// </summary>
internal readonly struct CartKey : IEquatable<CartKey>
{
    public CartKey(string sessionId, Guid worldLoadEpoch)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            throw new ArgumentException("A cart key needs the cart's in-session id.", nameof(sessionId));
        }

        if (worldLoadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A cart key needs a world-load epoch.", nameof(worldLoadEpoch));
        }

        SessionId = sessionId;
        WorldLoadEpoch = worldLoadEpoch;
    }

    public string SessionId { get; }

    public Guid WorldLoadEpoch { get; }

    public bool IsEmpty => string.IsNullOrEmpty(SessionId);

    public bool IsFromEpoch(Guid epoch) => !IsEmpty && WorldLoadEpoch == epoch;

    public bool Equals(CartKey other) =>
        string.Equals(SessionId, other.SessionId, StringComparison.Ordinal) && WorldLoadEpoch == other.WorldLoadEpoch;

    public override bool Equals(object? obj) => obj is CartKey other && Equals(other);

    public override int GetHashCode() =>
        unchecked((StringComparer.Ordinal.GetHashCode(SessionId ?? string.Empty) * 397) ^ WorldLoadEpoch.GetHashCode());

    public override string ToString() =>
        IsEmpty ? "<no cart>" : SessionId + "@" + WorldLoadEpoch.ToString("N", CultureInfo.InvariantCulture).Substring(0, 8);
}

internal enum CartLeaseState
{
    Unspecified = 0,
    Active = 1,
    Released = 2,
    Invalidated = 3,
}

internal enum LeaseInvalidation
{
    Unspecified = 0,
    WorldReloaded = 1,
    CartDestroyed = 2,
    CartUnloaded = 3,
    OwnershipLost = 4,
    PlayerRevoked = 5,
    AuthorityLost = 6,
    WorkerBodyLost = 7,
}

internal enum LeaseOutcome
{
    Unspecified = 0,
    Assigned = 1,

    /// <summary>The same lease id for the same worker and cart already exists.
    /// </summary>
    AlreadySatisfied = 2,

    /// <summary>The same lease id was used for a different worker or cart.
    /// </summary>
    RejectedDifferentPayload = 3,

    /// <summary>The worker already holds another active lease (one hauling
    /// assignment per Gunnar).</summary>
    RefusedWorkerBusy = 4,

    /// <summary>Another active lease already holds the cart (never two owners
    /// of one cart).</summary>
    RefusedCartLeased = 5,

    /// <summary>The cart key is from another world load.</summary>
    RefusedStaleEpoch = 6,

    Released = 7,
    Invalidated = 8,
    NotFound = 9,

    /// <summary>The lease is no longer active; nothing changed.</summary>
    NotActive = 10,
}

/// <summary>One worker-to-cart pairing (CART-01). Owned by a
/// <see cref="CartLeaseBook"/>; everything that touches the cart revalidates
/// the lease first.</summary>
internal sealed class CartLease
{
    internal CartLease(string leaseId, WorkerKey worker, CartKey cart, int revision)
    {
        LeaseId = leaseId;
        Worker = worker;
        Cart = cart;
        Revision = revision;
        State = CartLeaseState.Active;
    }

    public string LeaseId { get; }

    public WorkerKey Worker { get; }

    public CartKey Cart { get; }

    /// <summary>Increments on every change of this lease.</summary>
    public int Revision { get; private set; }

    public CartLeaseState State { get; private set; }

    public LeaseInvalidation Invalidation { get; private set; }

    public bool IsActive => State == CartLeaseState.Active;

    internal void End(CartLeaseState state, LeaseInvalidation reason)
    {
        State = state;
        Invalidation = reason;
        Revision++;
    }
}

/// <summary>Every cart lease of one world load: one active lease per worker,
/// one active lease per cart, payload-checked idempotence, and wholesale
/// invalidation when the world reloads. Game-free; the adapter feeds it facts.
/// </summary>
internal sealed class CartLeaseBook
{
    private readonly Dictionary<string, CartLease> _leases = new Dictionary<string, CartLease>(StringComparer.Ordinal);

    public CartLeaseBook(Guid worldLoadEpoch)
    {
        if (worldLoadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A lease book needs a world-load epoch.", nameof(worldLoadEpoch));
        }

        WorldLoadEpoch = worldLoadEpoch;
    }

    public Guid WorldLoadEpoch { get; private set; }

    /// <summary>Increments on every change to any lease in the book.</summary>
    public int Revision { get; private set; }

    public LeaseOutcome Assign(string leaseId, WorkerKey worker, CartKey cart)
    {
        if (string.IsNullOrEmpty(leaseId))
        {
            throw new ArgumentException("A lease id is required.", nameof(leaseId));
        }

        if (worker.IsEmpty || cart.IsEmpty)
        {
            throw new ArgumentException("A lease needs a worker and a cart.");
        }

        if (_leases.TryGetValue(leaseId, out CartLease? existing))
        {
            return existing.Worker.Equals(worker) && existing.Cart.Equals(cart)
                ? (existing.IsActive ? LeaseOutcome.AlreadySatisfied : LeaseOutcome.NotActive)
                : LeaseOutcome.RejectedDifferentPayload;
        }

        if (!cart.IsFromEpoch(WorldLoadEpoch))
        {
            return LeaseOutcome.RefusedStaleEpoch;
        }

        foreach (CartLease lease in _leases.Values)
        {
            if (!lease.IsActive)
            {
                continue;
            }

            if (lease.Worker.Equals(worker))
            {
                return LeaseOutcome.RefusedWorkerBusy;
            }

            if (lease.Cart.Equals(cart))
            {
                return LeaseOutcome.RefusedCartLeased;
            }
        }

        Revision++;
        _leases[leaseId] = new CartLease(leaseId, worker, cart, Revision);
        return LeaseOutcome.Assigned;
    }

    public LeaseOutcome Release(string leaseId)
    {
        if (!_leases.TryGetValue(leaseId, out CartLease? lease))
        {
            return LeaseOutcome.NotFound;
        }

        if (!lease.IsActive)
        {
            return LeaseOutcome.NotActive;
        }

        lease.End(CartLeaseState.Released, LeaseInvalidation.Unspecified);
        Revision++;
        return LeaseOutcome.Released;
    }

    public LeaseOutcome Invalidate(string leaseId, LeaseInvalidation reason)
    {
        if (reason == LeaseInvalidation.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "An invalidation needs a reason.");
        }

        if (!_leases.TryGetValue(leaseId, out CartLease? lease))
        {
            return LeaseOutcome.NotFound;
        }

        if (!lease.IsActive)
        {
            return LeaseOutcome.NotActive;
        }

        lease.End(CartLeaseState.Invalidated, reason);
        Revision++;
        return LeaseOutcome.Invalidated;
    }

    /// <summary>A new world load: every active lease ends as WorldReloaded and
    /// the book moves to the new epoch. A lease is never carried across.
    /// </summary>
    public int BeginWorldLoad(Guid newEpoch)
    {
        if (newEpoch == Guid.Empty || newEpoch == WorldLoadEpoch)
        {
            throw new ArgumentException("A new world load needs a new epoch.", nameof(newEpoch));
        }

        int ended = 0;
        foreach (CartLease lease in _leases.Values)
        {
            if (lease.IsActive)
            {
                lease.End(CartLeaseState.Invalidated, LeaseInvalidation.WorldReloaded);
                ended++;
            }
        }

        WorldLoadEpoch = newEpoch;
        Revision++;
        return ended;
    }

    public bool TryGet(string leaseId, out CartLease? lease) => _leases.TryGetValue(leaseId, out lease);

    public bool TryGetActiveForWorker(WorkerKey worker, out CartLease? lease)
    {
        foreach (CartLease candidate in _leases.Values)
        {
            if (candidate.IsActive && candidate.Worker.Equals(worker))
            {
                lease = candidate;
                return true;
            }
        }

        lease = null;
        return false;
    }

    public bool TryGetActiveForCart(CartKey cart, out CartLease? lease)
    {
        foreach (CartLease candidate in _leases.Values)
        {
            if (candidate.IsActive && candidate.Cart.Equals(cart))
            {
                lease = candidate;
                return true;
            }
        }

        lease = null;
        return false;
    }
}
