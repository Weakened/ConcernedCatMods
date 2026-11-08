using TheConcernedCat.Companions.Quest;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;
internal static class GunnarKeepsakeRecovery
{
    // Only resume the already-collected story. Never collect/recruit, replay a
    // notification, or turn elapsed time into progress.
    public static bool ResumeCollected(GunnarCompanionRecord? record,bool enabled) =>
        enabled && record!=null && record.State==QuestState.Collected && record.BeginIntroduction();
}
