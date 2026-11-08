using System;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>Gunnar's body contract with the shared NPC runtime (#458).
///
/// <b>Zero migrations.</b> The same prefab name and the same key prefix his
/// saved worker body has always had, read from <see cref="GunnarHaulingDefaults"/>
/// where the shipped body factory reads them too. The only change from the
/// worker-only contract is that the arbiter may now also grant his presentation
/// figure, one kind at a time. Game-free, so the contract the plugin registers
/// is the one the tests check.</summary>
internal static class GunnarBodyContract
{
    public static NpcBodyContract Create() =>
        NpcBodyContract.ForWorkerWithPresentation(
            GunnarHaulingDefaults.WorkerPrefabName,
            GunnarHaulingDefaults.WorkerKeyPrefix);
}

/// <summary>The leases on Gunnar's two bodies, held through Concerned NPC's one
/// arbiter (#458, modelled on Foreman's <c>ThorsteinBodies</c>).
///
/// <b>Release before claim.</b> The arbiter refuses the second kind while the
/// first is held, so the hand-over gives up the figure's lease first and only
/// then asks for a worker lease. Getting the order wrong would not make two
/// bodies; it would make the hand-over fail.
///
/// <b>The worker lease here is an arrival guard, not a job.</b> In Teamster a
/// job (a haul, a collection tour) claims the worker kind under its own id, so a
/// standing body lease would refuse every job. This class therefore holds the
/// worker lease only from the hand-over until the census has accounted for the
/// body that was just placed, which closes the frames in which the census still
/// says "no body" and a figure could otherwise be stood beside him. After that
/// the census itself keeps the figure away: a saved body anywhere outranks
/// everything (<see cref="GunnarBodyPlan"/>).
///
/// The figure's lifetime is shorter than its lease, never longer: the caller's
/// removal runs before the lease is released, and a removal that throws keeps
/// the lease and claims nothing. This class never touches a game object.</summary>
internal sealed class GunnarBodyLeases
{
    /// <summary>Who holds the presentation lease: the figure at home.</summary>
    public const string Holder = "teamster/gunnar-presentation";

    /// <summary>Who holds the worker lease between the hand-over and the census
    /// seeing the body that was placed.</summary>
    public const string ArrivalHolder = "teamster/gunnar-arrival";

    private readonly NpcRoleRegistry _registry;
    private readonly NpcIdentity _identity;
    private BodyLease? _presentation;
    private BodyLease? _arrival;

    public GunnarBodyLeases(NpcRoleRegistry registry, NpcIdentity identity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (identity.IsEmpty) throw new ArgumentException("Gunnar's identity is required.", nameof(identity));
        _identity = identity;
    }

    public NpcIdentity Identity => _identity;
    public bool HoldsPresentation => _presentation != null && _presentation.IsActive;
    public bool HoldsWorker => _arrival != null && _arrival.IsActive;
    public string LastRefusal { get; private set; } = string.Empty;

    /// <summary>Whether the arbiter accepted a role for him that admits a
    /// presentation figure. Without it no figure may be built.</summary>
    public bool Registered =>
        _registry.TryGetRole(_identity, out INpcRole role) && role.Body.Admits(NpcBodyKind.Presentation);

    /// <summary>Asks for the figure's lease. Never releases anything to get it:
    /// while the arrival guard or a job's worker lease is held, the arbiter
    /// refuses, and that refusal stands.</summary>
    public bool TakePresentation()
    {
        if (HoldsPresentation) { LastRefusal = string.Empty; return true; }
        _presentation = null;
        BodyClaim claim = _registry.TryClaimBody(_identity, NpcBodyKind.Presentation, Holder);
        if (claim.IsGranted && claim.Lease != null)
        { _presentation = claim.Lease; LastRefusal = string.Empty; return true; }
        LastRefusal = claim.Status + ": " + claim.Reason;
        return false;
    }

    /// <summary>The figure comes down, then its lease goes. If removal throws,
    /// the lease is kept: a figure must never outlive its permission.</summary>
    public void ReleasePresentation(Action removeFigure)
    {
        if (removeFigure == null) throw new ArgumentNullException(nameof(removeFigure));
        removeFigure();
        BodyLease? lease = _presentation;
        _presentation = null;
        lease?.Dispose();
    }

    /// <summary>The hand-over an order makes before his worker body is placed:
    /// figure down, presentation lease released, worker lease claimed. False,
    /// with the arbiter's reason, when the worker lease is refused - and then
    /// nothing may be placed.
    ///
    /// One refusal is not a reason to stop: a job already holds him. The
    /// arbiter refuses a figure to everybody for as long as that lasts, so
    /// there is no figure to hand over from and none can appear.</summary>
    public bool TryTakeWorker(Action removeFigure, out string refusal)
    {
        ReleasePresentation(removeFigure);
        if (HoldsWorker)
        {
            // A body was just placed and the census has not counted it yet: a
            // second one now would be the duplicate the census exists to catch.
            refusal = "a worker body was just placed and has not been counted yet; try again in a moment";
            LastRefusal = refusal;
            return false;
        }

        _arrival = null;
        BodyClaim claim = _registry.TryClaimBody(_identity, NpcBodyKind.Worker, ArrivalHolder);
        if (claim.IsGranted && claim.Lease != null)
        { _arrival = claim.Lease; refusal = string.Empty; LastRefusal = string.Empty; return true; }
        if (claim.Status == BodyClaimStatus.RefusedHolderConflict && JobHolds())
        { refusal = string.Empty; LastRefusal = string.Empty; return true; }
        refusal = claim.Status + ": " + claim.Reason;
        LastRefusal = refusal;
        return false;
    }

    /// <summary>Gives up the arrival guard. Idempotent. Releases only this
    /// holder's own claim; a job's lease is never touched.</summary>
    public void ReleaseWorker()
    {
        BodyLease? lease = _arrival;
        _arrival = null;
        lease?.Dispose();
    }

    /// <summary>The world has gone. Every lease died with it in the arbiter
    /// (each is stamped with its world), so this only drops the references.</summary>
    public void Forget()
    {
        _presentation = null;
        _arrival = null;
        LastRefusal = string.Empty;
    }

    // A job's worker lease, or a job holding his mode: either way the arbiter
    // refuses every presentation claim until it ends.
    private bool JobHolds() =>
        _registry.CurrentBodyKind(_identity) == NpcBodyKind.Worker ||
        _registry.ModeOf(_identity)?.MayRetireBody == false;
}
