using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;

namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

public sealed class GunnarCompanionRecordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ct-456-" + Guid.NewGuid().ToString("N"));
    private GunnarCompanionRecord Open(long world = 11, long character = 22) =>
        GunnarCompanionRecord.Open(_root, world, character);
    private string DataPath(long world = 11, long character = 22) =>
        new CompanionSidecarStore(_root).ResolvePath(new CompanionScope(GunnarCompanionRecord.Product,
            new WorldId(world), new CharacterId(character)));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void PersistedStagesAndIdentityArePinned()
    {
        Assert.Equal(0, (int)QuestState.Unstarted);
        Assert.Equal(1, (int)QuestState.Discovered);
        Assert.Equal(2, (int)QuestState.Collected);
        Assert.Equal(3, (int)QuestState.IntroPending);
        Assert.Equal(4, (int)QuestState.Recruited);
        Assert.Equal(5, (int)QuestState.Skipped);
        Assert.Equal("gunnar", GunnarCompanionRecord.Identity.Value);
        Assert.Equal("a-fair-load", GunnarCompanionRecord.Quest.Value);
        Assert.Equal("concerned-teamster", GunnarCompanionRecord.Product.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshAndLegacyDataDoNotRecruit(bool legacy)
    {
        Directory.CreateDirectory(_root);
        if (legacy)
        {
            File.WriteAllText(Path.Combine(_root, "teamster_trips_11.tsv"), "existing trip data");
            File.WriteAllText(Path.Combine(_root, "com.theconcernedcat.valheim.concernedteamster.cfg"),
                "[Workers]\nGunnarHaulingEnabled = true");
        }
        string[] before = Directory.GetFiles(_root);
        var record = Open();
        Assert.Equal(QuestState.Unstarted, record.State);
        Assert.False(record.IsRecruited);
        Assert.True(record.ShouldPresentMemento(true));
        Assert.False(record.TryRecruitAfterAcceptedCartAssignment(false));
        Assert.Equal(before, Directory.GetFiles(_root)); // opening is read-only
    }

    [Fact]
    public void AlreadyOwningACartIsNotAnAcceptedAssignment()
    {
        var record = Open();
        Assert.True(record.BeginIntroduction());
        Assert.False(record.TryRecruitAfterAcceptedCartAssignment(false));
        Assert.Equal(QuestState.IntroPending, Open().State);
        Assert.False(Open().IsRecruited);
    }

    [Fact]
    public void ProgressPersistsAndRecruitmentIsIdempotent()
    {
        Assert.True(Open().Discover()); Assert.Equal(QuestState.Discovered, Open().State);
        Assert.True(Open().Collect()); Assert.Equal(QuestState.Collected, Open().State);
        Assert.True(Open().BeginIntroduction()); Assert.Equal(QuestState.IntroPending, Open().State);
        Assert.True(Open().TryRecruitAfterAcceptedCartAssignment(true));
        string bytes = File.ReadAllText(DataPath());
        var recruited = Open();
        Assert.True(recruited.IsRecruited);
        Assert.True(recruited.TryRecruitAfterAcceptedCartAssignment(true));
        Assert.True(recruited.Skip()); Assert.True(recruited.Discover());
        Assert.Equal(bytes, File.ReadAllText(DataPath()));
    }

    [Fact]
    public void SavedCollectionNeverPresentsTheMementoAgain()
    {
        var record = Open();
        Assert.True(record.Discover());
        Assert.True(record.ShouldPresentMemento(true));
        Assert.True(record.Collect());
        Assert.False(record.ShouldPresentMemento(true));
        Assert.False(Open().ShouldPresentMemento(true));
        Assert.True(record.BeginIntroduction());
        Assert.False(Open().ShouldPresentMemento(true));
    }

    [Fact]
    public void FailedCollectionSaveKeepsMementoAvailable()
    {
        var record = Open();
        File.WriteAllText(_root, "blocked");
        try
        {
            Assert.False(record.Collect());
            Assert.True(record.ShouldPresentMemento(true));
        }
        finally { File.Delete(_root); }
        Assert.True(record.Collect());
        Assert.False(Open().ShouldPresentMemento(true));
    }

    [Fact]
    public void ToolsOnlyDoesNotRecruitAndLaterExplicitAssignmentCanWelcome()
    {
        Assert.True(Open().Skip());
        var skipped = Open();
        Assert.Equal(QuestState.Skipped, skipped.State);
        Assert.False(skipped.IsRecruited);
        Assert.True(skipped.CanReplayStory);
        Assert.False(skipped.ShouldPresentMemento(true));
        Assert.True(skipped.TryRecruitAfterAcceptedCartAssignment(true));
        Assert.True(Open().IsRecruited);
    }

    [Fact]
    public void VisibilityAndNameNeverChangeIdentityOrRecruitment()
    {
        var record = Open();
        Assert.True(record.TryRecruitAfterAcceptedCartAssignment(true));
        Assert.False(record.ShouldPresentFigure(false));
        Assert.True(record.ShouldPresentFigure(true));
        Assert.False(record.ShouldPresentMemento(false));
        Assert.Equal("Gunnar", GunnarCompanionRecord.ResolveDisplayName(" "));
        Assert.Equal("Gunnar renamed", GunnarCompanionRecord.ResolveDisplayName("Gunnar renamed"));
        Assert.Equal("Localized", GunnarCompanionRecord.ResolveDisplayName("Edited", _ => "Localized"));
        Assert.Equal("Edited", GunnarCompanionRecord.ResolveDisplayName("Edited", key => key));
        Assert.True(Open().IsRecruited);
    }

    [Theory]
    [InlineData(11, 23)]
    [InlineData(12, 22)]
    public void CharacterAndWorldIsolation(long world, long character)
    {
        Assert.True(Open().TryRecruitAfterAcceptedCartAssignment(true));
        Assert.False(Open(world, character).IsRecruited);
        Assert.True(Open(world, character).Skip());
        Assert.True(Open().IsRecruited);
        Assert.NotEqual(DataPath(), DataPath(world, character));
    }

    [Fact]
    public void SessionNeverReturnsThePreviousWorldOrCharacterAfterSwitchOrLogout()
    {
        var session = new GunnarCompanionSession(_root);
        var first = session.Resolve(11, 22)!;
        Assert.True(first.TryRecruitAfterAcceptedCartAssignment(true));
        Assert.Same(first, session.Resolve(11, 22));
        Assert.False(session.Resolve(12, 22)!.IsRecruited);
        Assert.False(first.IsRecruited);
        Assert.False(first.TryRecruitAfterAcceptedCartAssignment(true));
        Assert.False(first.ShouldPresentMemento(true));
        Assert.False(session.Resolve(11, 23)!.IsRecruited);
        Assert.Null(session.Resolve(0, 22));
        Assert.Null(session.Resolve(11, 0));
        Assert.True(session.Resolve(11, 22)!.IsRecruited);
        session.Forget();
        Assert.NotSame(first, session.Resolve(11, 22));
    }

    [Theory]
    [InlineData(0, 22)]
    [InlineData(11, 0)]
    public void UnresolvedScopeCannotReadOrWrite(long world, long character)
    {
        Assert.Throws<ArgumentException>(() => Open(world, character));
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("row")]
    [InlineData("future")]
    [InlineData("scope")]
    public void DamagedOrUntrustedSidecarNeverRecruits(string fault)
    {
        Assert.True(Open().TryRecruitAfterAcceptedCartAssignment(true));
        string text = File.ReadAllText(DataPath());
        text = fault switch
        {
            "corrupt" => "not a companion file",
            "row" => text + "q\tbroken\tnot-a-stage\t0\t0\n",
            "future" => text.Replace("s\t1", "s\t999"),
            "scope" => text.Replace("0000000000000016", "0000000000000017"),
            _ => throw new Exception()
        };
        File.WriteAllText(DataPath(), text);
        var record = Open();
        Assert.False(record.IsRecruited);
        Assert.False(record.TryRecruitAfterAcceptedCartAssignment(true));
        Assert.False(record.ShouldPresentFigure(true));
        if (File.Exists(DataPath())) Assert.Equal(text, File.ReadAllText(DataPath()));
        else Assert.Equal(text, File.ReadAllText(DataPath() + ".corrupt"));
    }

    [Fact]
    public void FailedSaveDoesNotExposeUnsavedRecruitmentOrRetirePresentation()
    {
        var record = Open();
        File.WriteAllText(_root, "directory blocked by a file");
        try
        {
            Assert.False(record.TryRecruitAfterAcceptedCartAssignment(true));
            Assert.False(record.IsRecruited);
            Assert.True(record.ShouldPresentMemento(true));
            Assert.NotNull(record.Notice);
        }
        finally { File.Delete(_root); }
        Assert.True(record.TryRecruitAfterAcceptedCartAssignment(true));
        Assert.True(Open().IsRecruited);
    }

    [Fact]
    public void ExistingFeatureGatesHaveNoCompanionDependency()
    {
        // Completeness follows the shipped feature enum, not a handpicked list.
        Assert.Equal(Enum.GetValues<TeamsterFeature>().Order(), CartAuthorityPolicy.AllFeatures.Order());
        foreach (var feature in Enum.GetValues<TeamsterFeature>())
        foreach (var authority in Enum.GetValues<CartAuthority>())
        {
            bool expected = CartAuthorityPolicy.ClassOf(feature) == FeatureClass.Mutation &&
                authority == CartAuthority.Local;
            Assert.Equal(expected, CartAuthorityPolicy.MayMutate(feature, authority));
        }
        // Source-link tests would otherwise miss a gate newly consulting a
        // companion adapter. Pin the production boundary as well as outcomes.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string root = Path.Combine(directory!.FullName, "src", "ConcernedTeamster");
        foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "Companions" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            string source = File.ReadAllText(file).Replace("\r\n","\n");
            if (Path.GetFileName(file)=="Plugin.cs")
            {
                // Allow exactly the isolated record/keepsake/recruitment lifecycle sites. A utility condition,
                // hidden second call or a moved/omitted hook still fails.
                foreach (string hook in new[] {
                    "private Adapters.Companions.GunnarCompanionAdapter? _gunnarCompanion;",
                    "_gunnarCompanion = Adapters.Companions.GunnarCompanionAdapter.Install(Config, Logger);",
                    "_gunnarCompanion?.Tick();",
                    "_gunnarCompanion?.Forget();",
                    "private Adapters.Companions.GunnarKeepsakeRuntime? _gunnarKeepsake;",
                    "_gunnarKeepsake = Adapters.Companions.GunnarKeepsakeRuntime.Install(_gunnarCompanion,\n"+
                    "            () => _hauling?.BodyStatus ?? Domain.Hauling.Execution.WorkerBodyStatus.Unspecified,\n"+
                    "            () => _hauling?.BoundBody != null, Logger);",
                    "_gunnarKeepsake?.Tick();",
                    "_gunnarKeepsake?.Forget();",
                    "private Adapters.Companions.GunnarRecruitmentRuntime? _gunnarRecruitment;",
                    "_gunnarRecruitment = Adapters.Companions.GunnarRecruitmentRuntime.Install(_gunnarCompanion, () => _hauling?.Service, Logger);",
                    "_gunnarRecruitment?.Tick();",
                    "_gunnarRecruitment?.Forget();" })
                {
                    Assert.Equal(1, source.Split(hook,StringSplitOptions.None).Length-1);
                    source=source.Replace(hook,"");
                }
                Assert.DoesNotContain("_gunnarCompanion",source);
                Assert.DoesNotContain("_gunnarKeepsake",source);
                Assert.DoesNotContain("_gunnarRecruitment",source);
            }
            Assert.DoesNotContain("GunnarCompanion", source);
            Assert.DoesNotContain("TheConcernedCat.Companions", source);
        }
    }
}
