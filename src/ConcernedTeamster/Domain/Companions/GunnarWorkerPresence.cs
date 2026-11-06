using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;
internal static class GunnarWorkerPresence
{
    public static bool? From(WorkerBodyStatus status,bool liveBody)
    {
        if(liveBody) return true;
        switch(status)
        {
            case WorkerBodyStatus.NotFound: return false;
            case WorkerBodyStatus.Bound:
            case WorkerBodyStatus.NotLoaded:
            case WorkerBodyStatus.Duplicated:
            case WorkerBodyStatus.Faulted: return true;
            default: return null;
        }
    }
}
