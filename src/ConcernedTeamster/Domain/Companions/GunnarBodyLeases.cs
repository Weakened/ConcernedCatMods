using System;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>Owns only a presentation lease. Worker claims remain owned by the
/// existing job runtime, through the supplied callback. No second worker owner.</summary>
internal sealed class GunnarBodyLeases
{
    private readonly NpcRoleRegistry _registry;
    private readonly NpcIdentity _identity;
    private BodyLease? _presentation;
    public const string Holder = "teamster/gunnar-presentation";
    public GunnarBodyLeases(NpcRoleRegistry registry, NpcIdentity identity)
    { _registry=registry; _identity=identity; }
    public bool HoldsPresentation => _presentation != null && _presentation.IsActive;
    public bool TakePresentation()
    {
        if (HoldsPresentation) return true;
        BodyClaim claim = _registry.TryClaimBody(_identity, NpcBodyKind.Presentation, Holder);
        _presentation = claim.IsGranted ? claim.Lease : null;
        return HoldsPresentation;
    }
    public void ReleasePresentation(Action removeFigure)
    {
        removeFigure(); // if removal throws, keep the lease and refuse handover
        _presentation?.Dispose(); _presentation=null;
    }
    public bool HandOverToExistingWorker(Action removeFigure, Func<bool> claimWorker)
    {
        ReleasePresentation(removeFigure);
        return claimWorker();
    }
}

/// <summary>Wrap the existing role BEFORE its sole registration. Same identity,
/// paths, worker prefab and key prefix: adds presentation permission only.</summary>
internal sealed class GunnarPresentationRole : INpcRole
{
    private readonly INpcRole _worker;
    public GunnarPresentationRole(INpcRole worker)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        if (worker.Identity.Value != TheConcernedCat.Workers.WorkerKey.Gunnar.Value || worker.Body.PrefabName !=
            Hauling.Execution.GunnarHaulingDefaults.WorkerPrefabName || worker.Body.ZdoKeyPrefix !=
            Hauling.Execution.GunnarHaulingDefaults.WorkerKeyPrefix)
            throw new ArgumentException("Only Gunnar's unchanged worker contract can be upgraded.", nameof(worker));
        Body = NpcBodyContract.ForWorkerWithPresentation(worker.Body.PrefabName, worker.Body.ZdoKeyPrefix);
    }
    public NpcIdentity Identity => _worker.Identity;
    public NpcBodyContract Body { get; }
    public INpcDataPaths Paths => _worker.Paths;
}
