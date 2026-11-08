using System.Text.RegularExpressions;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

/// <summary>#459: Gunnar's lines are registered in Teamster's one localization
/// catalog, so the translator template offers them and a translator file can
/// override them, and the figure's talk point is bound to them.</summary>
[Collection("TeamsterStrings shared statics")]
public sealed class GunnarDialogueCatalogTests
{
    private static readonly string[] StoryKeys =
        { "gunnar.intro", "gunnar.objective", "gunnar.complete", "gunnar.attention", "gunnar.talk.verb", "gunnar.says" };

    [Fact]
    public void EveryLineHeCanSayIsACatalogRow()
    {
        foreach (var line in GunnarTalk.Catalog.Lines) Assert.True(TeamsterStrings.HasKey(line.Key), line.Key);
        foreach (string key in StoryKeys) Assert.True(TeamsterStrings.HasKey(key), key);
        Assert.True(GunnarTalk.Catalog.Count >= 24);
        Assert.True(GunnarDialogueStrings.English.Count >= GunnarTalk.Catalog.Count + StoryKeys.Length);
        Assert.NotEmpty(GunnarRecruitmentStrings.English);
        foreach (var row in GunnarDialogueStrings.English) Assert.Equal(TeamsterStrings.Defaults[row.Key], row.Value);
        foreach (var row in GunnarRecruitmentStrings.English) Assert.Equal(TeamsterStrings.Defaults[row.Key], row.Value);
    }

    [Fact]
    public void TheTranslatorTemplateOffersHisLines()
    {
        var template = TeamsterStrings.TranslatorTemplate().ToList();
        foreach (string key in StoryKeys.Concat(GunnarTalk.Catalog.Lines.Select(l => l.Key)))
            Assert.Contains(template, row => row.StartsWith(key + "\t", StringComparison.Ordinal));
        Assert.Contains(template, row => row.StartsWith("gunnar.recruit.question\t", StringComparison.Ordinal));
    }

    [Fact]
    public void ATranslatorFileChangesWhatHeSays()
    {
        try
        {
            TeamsterStrings.LoadOverrides(new Dictionary<string, string>
            {
                ["gunnar.objective"] = "Hämta en vagn till Gunnar",
                ["gunnar.says"] = "{0} säger: {1}",
            });
            Assert.Equal("Hämta en vagn till Gunnar",
                new GunnarTalk().Reply(QuestState.IntroPending, HaulPhase.Ready, default, key => TeamsterStrings.Get(key)));
            Assert.Equal("Hämta en vagn till Gunnar", GunnarDialogueStrings.Resolve("gunnar.objective"));
            Assert.Equal("Gunnar säger: hej", TeamsterStrings.Format("gunnar.says", "Gunnar", "hej"));
        }
        finally
        {
            TeamsterStrings.LoadOverrides(new Dictionary<string, string>());
        }

        Assert.Equal("Bring Gunnar a usable cart", GunnarDialogueStrings.Resolve("gunnar.objective"));
        Assert.Equal("Gunnar: \"hej\"", TeamsterStrings.Format("gunnar.says", "Gunnar", "hej"));
    }

    [Fact]
    public void TheEnglishTextLivesOnlyInTheCatalog()
    {
        // A second copy would drift from the one a translator overrides.
        foreach (string file in new[] { "GunnarDialogueStrings.cs", "GunnarRecruitmentStrings.cs" })
        {
            string source = Read("src", "ConcernedTeamster", "Domain", "Companions", file);
            Assert.DoesNotMatch(new Regex(@"\[""gunnar\.[a-z_.]+""\]\s*="), source);
        }
    }

    [Fact]
    public void TheFigureSpeaksOnlyWhenTalkedToAndThroughTheCatalog()
    {
        string runtime = Read("src", "ConcernedTeamster", "Adapters", "Companions", "GunnarPresentation.cs");
        string presenter = Read("src", "ConcernedTeamster", "Adapters", "Companions", "GunnarFigurePresenter.cs");
        string plugin = Regex.Replace(Read("src", "ConcernedTeamster", "Plugin.cs"), @"\s+", string.Empty);
        Assert.Contains("Look, DisplayName(), TeamsterStrings.Get(\"gunnar.talk.verb\"), Talk);", runtime);
        Assert.Contains("if (talk != null) _dresser.EnableTalk(displayName, verb, talk, _log);", presenter);
        Assert.Contains("()=>_hauling?.CollectionIdentity?.MayRetireBody??false,()=>_hauling?.Service,Logger);", plugin);
        // The only caller of the rotation is the talk callback: no timer, no tick.
        Assert.Single(Regex.Matches(runtime, @"_talk\.Reply\("));
        string tick = runtime.Substring(runtime.IndexOf("public void Tick(", StringComparison.Ordinal));
        tick = tick.Substring(0, tick.IndexOf("public bool TakeWorkerBody(", StringComparison.Ordinal));
        Assert.DoesNotContain("Talk()", tick);
        Assert.DoesNotContain("ShowMessage", tick);
    }

    private static string Read(params string[] relative)
    {
        string path = Path.Combine(relative);
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null)
        {
            string candidate = Path.Combine(here.FullName, path);
            if (File.Exists(candidate)) return File.ReadAllText(candidate).Replace("\r\n", "\n");
            here = here.Parent;
        }

        throw new FileNotFoundException("Shipped source not found above the test assembly.", path);
    }
}
