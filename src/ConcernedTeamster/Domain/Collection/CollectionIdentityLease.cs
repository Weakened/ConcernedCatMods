using System;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;
using TheConcernedCat.Workers;
namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>A collection order holds the same identity and mode used by hauling.</summary>
internal sealed class CollectionIdentityLease
{
    private readonly string _job = "collection-" + Guid.NewGuid().ToString("N");
    private WorkerIdentityHold? _hold;
    public bool IsHeld => _hold != null && _hold.IsHeldBy(_job);
    public bool TryAcquire(WorkerIdentityHold? hold)
    {
        if (IsHeld) return false;
        if (hold == null) return false;
        ActorModeOutcome result = hold.Enter(ActorMode.Working, _job);
        if (result != ActorModeOutcome.Entered && result != ActorModeOutcome.AlreadyInMode) return false;
        _hold = hold;
        return true;
    }
    public void Release()
    {
        _hold?.Release(_job);
        _hold = null;
    }
}
