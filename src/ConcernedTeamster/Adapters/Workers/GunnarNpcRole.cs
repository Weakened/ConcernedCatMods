using System;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar, as the shared NPC runtime sees him (#381, adopting #371).
///
/// <b>What registering does, and what it very deliberately does not.</b> It
/// tells the shared runtime that one identity exists, which prefab its saved
/// body is found under, and which key prefix that body stores itself beneath. It
/// does <b>not</b> hand the runtime the prefab, and it does not ask the runtime
/// to build, register or find a body. Teamster's own prefab factory still does
/// all of that, at the same moment in start-up it always has, under the same
/// name - because the host destroys any saved object whose prefab is not
/// registered when a world's objects are created, and a prefab registered later,
/// or under a different name, would delete every existing Gunnar with whatever
/// he is carrying inside him.
///
/// <b>Zero migrations, mechanically.</b> Both durable facts below come from
/// <see cref="GunnarHaulingDefaults"/>, which is where they already were and
/// where the shipped body factory still reads them. There is no second spelling
/// of either, so there is nothing for the two to drift apart about, and nothing
/// this file does reaches a save.</summary>
internal sealed class GunnarNpcRole : INpcRole
{
    private GunnarNpcRole(NpcIdentity identity)
    {
        Identity = identity;
        Body = NpcBodyContract.ForWorker(
            GunnarHaulingDefaults.WorkerPrefabName,
            GunnarHaulingDefaults.WorkerKeyPrefix);
        Paths = new TeamsterDataPaths();
    }

    /// <summary>Builds the role, or says why it could not - which can only
    /// happen if Gunnar's own worker key stops being a valid identity, and is
    /// checked rather than assumed because the alternative is an exception out
    /// of a plugin's <c>Awake</c>.</summary>
    public static bool TryCreate(out GunnarNpcRole role, out string reason)
    {
        role = null!;
        if (!NpcIdentity.TryParse(WorkerKey.Gunnar.Value, out NpcIdentity identity))
        {
            reason = "'" + WorkerKey.Gunnar.Value + "' is not a valid NPC identity";
            return false;
        }

        role = new GunnarNpcRole(identity);
        if (!role.Body.TryValidate(out reason))
        {
            role = null!;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public NpcIdentity Identity { get; }

    public NpcBodyContract Body { get; }

    public INpcDataPaths Paths { get; }

    /// <summary>Where Teamster keeps its own files. The shared runtime never
    /// joins anything to this, never creates it and never reads it; it asks for
    /// it so that a role which has not been given one is refused at
    /// registration rather than discovered later.</summary>
    private sealed class TeamsterDataPaths : INpcDataPaths
    {
        public string Root => Adapters.TripRecordingService.SidecarDirectory;

        /// <summary>There are no purposes. This product keeps its own files by
        /// its own names, as it always has, and the shared runtime reads and
        /// writes none of them.</summary>
        public bool TryResolveFile(string purpose, out string absolutePath)
        {
            absolutePath = string.Empty;
            return false;
        }
    }
}

/// <summary>Gunnar's identity hold, taken from the shared runtime's arbiter
/// instead of from a mode owner this product built for itself.
///
/// <b>Why through the arbiter.</b> One identity may be doing one thing at a
/// time. Before this, three products each kept their own answer to that in their
/// own assembly, and the rule held only because no two of them were ever asked
/// about the same NPC. Now that Teamster consumes the shared runtime, the
/// repository fails the build if it builds its own mode owner, and this is what
/// it builds instead: every take and every release goes to the one arbiter in
/// the process, so Gunnar cannot be hauling and collecting at the same time, and
/// a world unload ends the hold whether or not this product noticed.
///
/// <b>It fails closed.</b> No registration, no world, no arbiter: no hold. A
/// refusal stops a job; it never starts one.</summary>
internal sealed class GunnarIdentityAuthority : IWorkerIdentityAuthority
{
    private readonly NpcRoleRegistry _registry;
    private readonly NpcIdentity _identity;
    private string? _holder;

    public GunnarIdentityAuthority(NpcRoleRegistry registry, NpcIdentity identity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _identity = identity;
    }

    /// <summary>The job holding Gunnar, or null. Asked of the arbiter every
    /// time rather than remembered, because the arbiter ends every hold when a
    /// world goes away and a remembered holder would outlive the world it named.
    /// </summary>
    public string? Holder =>
        _holder != null && _registry.CurrentBodyKind(_identity) == NpcBodyKind.Worker ? _holder : null;

    public WorkerHoldOutcome Take(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return WorkerHoldOutcome.Unspecified;
        }

        BodyClaim claim = _registry.TryClaimBody(_identity, NpcBodyKind.Worker, jobId);
        switch (claim.Status)
        {
            case BodyClaimStatus.Claimed:
                _holder = jobId;
                return WorkerHoldOutcome.Taken;
            case BodyClaimStatus.AlreadyHeld:
                _holder = jobId;
                return WorkerHoldOutcome.AlreadyHeld;
            case BodyClaimStatus.RefusedHolderConflict:
            case BodyClaimStatus.RefusedOtherKindExists:
                return WorkerHoldOutcome.RefusedBusy;
            default:
                // No world, no registration, no holder: the arbiter is not in a
                // position to answer, which is a refusal and not a grant.
                return WorkerHoldOutcome.Unavailable;
        }
    }

    public WorkerHoldOutcome Give(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return WorkerHoldOutcome.Unspecified;
        }

        BodyClaim claim = _registry.ReleaseBody(_identity, NpcBodyKind.Worker, jobId);
        if (string.Equals(_holder, jobId, StringComparison.Ordinal))
        {
            _holder = null;
        }

        return claim.Status == BodyClaimStatus.Released ? WorkerHoldOutcome.Given : WorkerHoldOutcome.NotHeld;
    }
}
