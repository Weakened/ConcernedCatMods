using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;
public sealed class GunnarTalkTests
{
    [Fact] public void AuthoredCatalogHasAtLeast24UniqueLocalizableLines()
    {
        Assert.True(GunnarTalk.Catalog.Count>=24);
        foreach (var line in GunnarTalk.Catalog.Lines)
        {
            Assert.True(GunnarDialogueStrings.English.ContainsKey(line.Key));
            Assert.Null(line.RequiredBiome); // no unavailable progression claim
        }
    }
    [Fact] public void RotationSuppressesRecentRepeatsAndReachesEveryLine()
    {
        var talk=new GunnarTalk(); var recent=new Queue<string>(); var seen=new HashSet<string>();
        for (int i=0;i<GunnarTalk.Catalog.Count*3;i++)
        {
            string line=talk.Reply(QuestState.Recruited,HaulPhase.Ready,HaulAttentionReason.Unspecified)!;
            Assert.DoesNotContain(line,recent); seen.Add(line); recent.Enqueue(line);
            if (recent.Count>8) recent.Dequeue();
        }
        Assert.Equal(GunnarTalk.Catalog.Count,seen.Count);
    }
    [Fact] public void EveryRealAttentionReasonUsesTheSameCatalogKeyAsThePanel()
    {
        foreach (var reason in Enum.GetValues<HaulAttentionReason>())
        {
            if (reason==HaulAttentionReason.Unspecified) { Assert.Null(GunnarTalk.AttentionKey(reason)); continue; }
            string key=GunnarTalk.AttentionKey(reason)!;
            Assert.Equal(HaulPanelKeys.ForReason(reason),key); Assert.True(TeamsterStrings.HasKey(key));
            Assert.Contains(TeamsterStrings.Get(key),new GunnarTalk().Reply(
                QuestState.Recruited,HaulPhase.NeedsAttention,reason));
        }
        Assert.Null(GunnarTalk.AttentionKey((HaulAttentionReason)999));
    }
    [Fact] public void IntroductionAndToolsOnlyDoNotPretendHeWasRecruited()
    {
        var talk=new GunnarTalk();
        Assert.Equal("Bring Gunnar a usable cart",talk.Reply(QuestState.IntroPending,HaulPhase.Ready,default));
        Assert.Null(talk.Reply(QuestState.Skipped,HaulPhase.Ready,default));
        Assert.Null(talk.Reply(QuestState.Unstarted,HaulPhase.Ready,default));
    }
    [Fact] public void TranslatedLineIsUsedAndNoMissingOptionalCompanionOrProgressionHintLeaks()
    {
        Assert.Equal("translated",new GunnarTalk().Reply(QuestState.Recruited,HaulPhase.Ready,default,_=>"translated"));
        foreach (string line in GunnarDialogueStrings.English.Values)
        {
            Assert.DoesNotContain("Hulgi",line); Assert.DoesNotContain("Thorstein",line);
            Assert.DoesNotContain("boss",line,StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("metal out of",line,StringComparison.OrdinalIgnoreCase);
        }
    }
}
