using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using Rig = ConcernedTeamster.Tests.HaulingExecutionRig;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

/// <summary>#462 (#297's upgrade row): a 1.0.x player who installs this build
/// and never meets Gunnar keeps every utility exactly as it was. The companion
/// is presentation and story only: no feature gate, no worker switch and no
/// haul asks whether he was met, and a never-met character gets no figure and
/// no recruitment, only the buckle a player may ignore forever.</summary>
public sealed class GunnarNeverMetUpgradeTests
{
    private static GunnarCompanionRecord NeverMet(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "ct-462-never-met-" + Guid.NewGuid().ToString("N"));
        return GunnarCompanionRecord.Open(root, 1234, 5678);
    }

    [Fact]
    public void ANeverMetCharacterHasNoStoryNoFigureAndNoHire()
    {
        GunnarCompanionRecord record = NeverMet(out string root);
        try
        {
            Assert.Equal(QuestState.Unstarted, record.State);
            Assert.False(record.IsRecruited);
            Assert.False(record.ShouldPresentFigure(true));
            // The buckle is offered, never required.
            Assert.True(record.ShouldPresentMemento(true));
            // Opening the record of a character who never met him writes nothing.
            Assert.False(Directory.Exists(root) && Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any());

            var everythingElseTrue = new GunnarBodyFacts(registered: true, worldLoaded: true, host: true, enabled: true,
                stage: null, savedWorkers: 0, resting: true, anchorKnown: true, near: true);
            Assert.Equal(GunnarBodyKind.None, GunnarBodyPlan.Decide(everythingElseTrue));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void EveryFeatureGateIsTheSameWithTheCompanionNeverMet()
    {
        // The gate's inputs are the feature and the cart's authority. There is
        // no companion argument, so there is nothing a never-met state changes.
        foreach (TeamsterFeature feature in Enum.GetValues<TeamsterFeature>())
        foreach (CartAuthority authority in Enum.GetValues<CartAuthority>())
        {
            bool expected = CartAuthorityPolicy.ClassOf(feature) == FeatureClass.Mutation && authority == CartAuthority.Local;
            Assert.Equal(expected, CartAuthorityPolicy.MayMutate(feature, authority));
        }
    }

    [Fact]
    public void AHaulRunsWithTheCompanionNeverMet()
    {
        // The executor and service know nothing about the story: the rig has no
        // companion record at all, as a 1.0.x world has none.
        var rig = new Rig();
        rig.AssignAndStartLeg();
        Assert.Equal("haul-1", rig.Service.Snapshot.HaulId);
        Assert.NotEqual(HaulPhase.NeedsAttention, rig.Service.Snapshot.Phase);
        rig.AssertNoBugs();
    }

    [Fact]
    public void NoUtilityOrWorkerSourceConsultsTheCompanion()
    {
        // GunnarCompanionRecordTests.ExistingFeatureGatesHaveNoCompanionDependency
        // pins Plugin.cs's lifecycle hooks; this pins the two runtimes a 1.0.x
        // player uses most against the companion's record and story types.
        foreach (string file in new[]
        {
            Path.Combine("Adapters", "Workers", "GunnarHaulingRuntime.cs"),
            Path.Combine("Adapters", "Workers", "GunnarCollectionRuntime.cs"),
            Path.Combine("Domain", "Hauling", "Execution", "HaulExecutor.cs"),
        })
        {
            string source = Read(file);
            Assert.DoesNotContain("GunnarCompanion", source);
            Assert.DoesNotContain("QuestState", source);
            Assert.DoesNotContain("ShouldPresentFigure", source);
        }
    }

    private static string Read(string relative)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null)
        {
            string candidate = Path.Combine(here.FullName, "src", "ConcernedTeamster", relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            here = here.Parent;
        }

        throw new FileNotFoundException("Shipped source not found above the test assembly.", relative);
    }
}
