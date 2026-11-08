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
        Assert.Contains("Look, DisplayName(), TeamsterStrings.Get(\"gunnar.talk.verb\"), _onTalk);", runtime);
        Assert.Contains("_onTalk = Talk;", runtime);
        Assert.Contains("if (talk != null) _dresser.EnableTalk(displayName, verb, talk, _log);", presenter);
        Assert.Contains("()=>_hauling?.CollectionIdentity?.MayRetireBody??false,()=>_hauling?.Service,Logger);", plugin);
        // The only caller of the rotation is the talk callback: no timer, no tick.
        Assert.Single(Regex.Matches(runtime, @"_talk\.Reply\("));
        // Speech is reached from Talk() alone: no other method of the runtime
        // shows a message or calls it, so neither the tick nor the pass speaks.
        string talk = Body(runtime, "private void Talk(");
        string rest = runtime.Replace(talk, string.Empty);
        Assert.DoesNotContain("ShowMessage", rest);
        Assert.DoesNotMatch(new Regex(@"(?<![A-Za-z_])Talk\(\)"), rest);
        // A fresh rotation per character and world, started at that character's
        // own offset, and forgotten with the world.
        Assert.Contains("if (!ReferenceEquals(record, _talkScope)) { _talkScope = record; _talk = new GunnarTalk(GunnarTalk.StartFor(", talk);
        Assert.Contains("_talkScope = null;", Body(runtime, "public void OnWorldUnloading("));
    }

    [Fact]
    public void ATranslationThatDropsAPlaceholderIsRefused()
    {
        var parsed = TeamsterStrings.ParseOverrides(new[]
        {
            "gunnar.says\t{0} sagt etwas",
            "gunnar.attention\tHalt",
            "gunnar.objective\tHämta en vagn till Gunnar",
        }, out int skipped);
        Assert.Equal(2, skipped);
        Assert.False(parsed.ContainsKey("gunnar.says"));
        Assert.False(parsed.ContainsKey("gunnar.attention"));
        Assert.True(parsed.ContainsKey("gunnar.objective"));
    }

    [Fact]
    public void EachCharacterStartsTheRotationAtItsOwnStableOffset()
    {
        int a = GunnarTalk.StartFor(11, 22);
        Assert.Equal(a, GunnarTalk.StartFor(11, 22));
        Assert.InRange(a, 0, GunnarTalk.Catalog.Count - 1);
        var starts = new HashSet<int>();
        for (long character = 1; character <= 40; character++) starts.Add(GunnarTalk.StartFor(11, character));
        Assert.True(starts.Count > 1);
        string first = new GunnarTalk(GunnarTalk.StartFor(11, 22)).Reply(QuestState.Recruited, HaulPhase.Ready, default)!;
        Assert.Equal(first, new GunnarTalk(GunnarTalk.StartFor(11, 22)).Reply(QuestState.Recruited, HaulPhase.Ready, default));
    }

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source.Substring(start, index - start + 1);
        }

        throw new InvalidOperationException("unbalanced braces after " + signature);
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
