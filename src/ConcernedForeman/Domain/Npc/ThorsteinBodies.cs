using System;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>The leases on Thorstein's two bodies, held through Concerned NPC's
/// one arbiter (#295 step 1).
///
/// <b>What it guarantees: at most one of the two leases is ever held, and the
/// one being given up is released before the other is asked for.</b> The
/// arbiter refuses the second kind while the first is held
/// (<c>RefusedOtherKindExists</c>), so getting the order wrong would not make
/// two bodies. It would make the hand-over fail, and he would be stuck as
/// whichever one he was. Releasing first is what makes the hand-over work at
/// all.
///
/// <b>The figure's lifetime is shorter than its lease, never longer.</b> The
/// runtime builds the figure only after <see cref="HoldsPresentation"/> is
/// true and destroys it before calling anything here that could release that
/// lease. This class never touches a game object. It holds permissions, and
/// the permissions are what the arbiter checks.
///
/// <b>The worker lease is a permission, not the body.</b> His saved body was
/// in the world before this lease existed, and it is found by key whether a
/// lease is held or not. Holding it whenever that body exists is what makes the
/// arbiter refuse a figure for as long as the body stands, including from any
/// other runtime in the process.
///
/// Game-free, so the hand-over is tested against the real library rather than
/// a description of it.</summary>
internal sealed class ThorsteinBodies
{
    /// <summary>Who holds the worker lease: the settlement runtime that finds,
    /// spawns and retires his saved body.</summary>
    internal const string WorkerHolder = "foreman/settlement-body";

    /// <summary>Who holds the presentation lease: the figure at home.</summary>
    internal const string PresentationHolder = "foreman/presentation";

    private readonly NpcRoleRegistry _registry;
    private readonly NpcIdentity _identity;

    private BodyLease? _worker;
    private BodyLease? _presentation;

    internal ThorsteinBodies(NpcRoleRegistry registry, NpcIdentity identity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (identity.IsEmpty)
        {
            throw new ArgumentException("Thorstein's identity is required.", nameof(identity));
        }

        _identity = identity;
    }

    /// <summary>The figure may exist. Asked by the runtime immediately before
    /// it builds one, and every pass while one stands.</summary>
    internal bool HoldsPresentation => _presentation != null && _presentation.IsActive;

    /// <summary>The worker lease is held.</summary>
    internal bool HoldsWorker => _worker != null && _worker.IsActive;

    /// <summary>What is held now. Never both: see the class remarks.</summary>
    internal ThorsteinBodyKind Held =>
        HoldsWorker ? ThorsteinBodyKind.Worker
        : HoldsPresentation ? ThorsteinBodyKind.Presentation
        : ThorsteinBodyKind.None;

    /// <summary>The last refusal the arbiter gave, for the status line. Empty
    /// after a grant.</summary>
    internal string LastRefusal { get; private set; } = string.Empty;

    /// <summary>Moves towards <paramref name="desired"/>, releasing before
    /// claiming, and returns what is held afterwards.
    ///
    /// The caller must already have destroyed the figure when
    /// <paramref name="desired"/> is not <see cref="ThorsteinBodyKind.Presentation"/>.
    /// This only gives up the permission it was standing on.</summary>
    internal ThorsteinBodyKind Reconcile(ThorsteinBodyKind desired)
    {
        switch (desired)
        {
            case ThorsteinBodyKind.Worker:
                ReleasePresentation();
                Claim(NpcBodyKind.Worker, WorkerHolder, ref _worker);
                break;

            case ThorsteinBodyKind.Presentation:
                ReleaseWorker();
                Claim(NpcBodyKind.Presentation, PresentationHolder, ref _presentation);
                break;

            default:
                ReleasePresentation();
                ReleaseWorker();
                LastRefusal = string.Empty;
                break;
        }

        return Held;
    }

    /// <summary>The hand-over an order makes: the figure's lease is given up
    /// first, then the worker lease is asked for. The caller has already taken
    /// the figure down. False, with <see cref="LastRefusal"/> saying why, when
    /// the arbiter refuses. The figure's lease is not re-taken here; the next
    /// pass decides that.</summary>
    internal bool TryTakeWorker(out string refusal)
    {
        ReleasePresentation();
        bool granted = Claim(NpcBodyKind.Worker, WorkerHolder, ref _worker);
        refusal = granted ? string.Empty : LastRefusal;
        return granted;
    }

    /// <summary>Gives up the figure's lease. Idempotent.</summary>
    internal void ReleasePresentation()
    {
        BodyLease? lease = _presentation;
        _presentation = null;
        lease?.Dispose();
    }

    /// <summary>Gives up the worker lease. Idempotent. It releases only this
    /// holder's own claim; a job's mode is untouched, because this holder never
    /// held it.</summary>
    internal void ReleaseWorker()
    {
        BodyLease? lease = _worker;
        _worker = null;
        lease?.Dispose();
    }

    /// <summary>The world has gone. Every lease died with it in the arbiter
    /// (each is stamped with its world), so this only drops the references.
    /// </summary>
    internal void Forget()
    {
        _presentation = null;
        _worker = null;
        LastRefusal = string.Empty;
    }

    private bool Claim(NpcBodyKind kind, string holder, ref BodyLease? slot)
    {
        if (slot != null && slot.IsActive)
        {
            LastRefusal = string.Empty;
            return true;
        }

        slot = null;
        BodyClaim claim = _registry.TryClaimBody(_identity, kind, holder);
        if (claim.IsGranted && claim.Lease != null)
        {
            slot = claim.Lease;
            LastRefusal = string.Empty;
            return true;
        }

        LastRefusal = claim.Status + ": " + claim.Reason;
        return false;
    }
}
