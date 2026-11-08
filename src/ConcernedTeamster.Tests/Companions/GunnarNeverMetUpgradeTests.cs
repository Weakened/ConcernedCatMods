using System.Text.RegularExpressions;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Tests.Companions;

/// <summary>#462 (#297's upgrade row): a 1.0.x player who installs this build
/// and never meets Gunnar keeps every utility exactly as it was. The companion
/// is presentation and story only, so a never-met character gets no figure and
/// no hire, and no utility - telemetry, the panel, the manifest, warnings,
/// descent, recovery, trips, routes, the brake - can even name the companion's
/// types, let alone ask whether he was met.</summary>
public sealed class GunnarNeverMetUpgradeTests
{
    [Fact]
    public void ANeverMetCharacterHasNoStoryNoFigureAndNoHire()
    {
        string root = Path.Combine(Path.GetTempPath(), "ct-462-never-met-" + Guid.NewGuid().ToString("N"));
        try
        {
            GunnarCompanionRecord record = GunnarCompanionRecord.Open(root, 1234, 5678);
            Assert.Equal(QuestState.Unstarted, record.State);
            Assert.False(record.IsRecruited);
            Assert.False(record.ShouldPresentFigure(true));
            // The buckle is offered, never required.
            Assert.True(record.ShouldPresentMemento(true));
            // Opening the record of a character who never met him writes nothing.
            Assert.False(Directory.Exists(root) && Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>The ports that are part of the companion's own features and
    /// live in the worker folder because they touch a cart or register his
    /// role, with exactly the companion names each may use. Anything else
    /// outside a Companions folder naming a companion type fails.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        // #458: the role registers the game-free body contract.
        ["Adapters/Workers/GunnarNpcRole.cs"] = new[] { "GunnarBodyContract", "Domain.Companions" },
        // #460: reads the explicitly chosen cart for recruitment.
        ["Adapters/Workers/GunnarRecruitmentCartPort.cs"] = new[] { "GunnarCartCheck", "GunnarCartChoice", "Domain.Companions" },
        // The plugin installs the four companion runtimes (each hook pinned in
        // GunnarCompanionRecordTests.ExistingFeatureGatesHaveNoCompanionDependency)
        // and names nothing else of the companion's.
        ["Plugin.cs"] = new[] { "Adapters.Companions", "GunnarCompanionAdapter", "GunnarKeepsakeRuntime",
            "GunnarRecruitmentRuntime", "GunnarPresentation" },
    };

    [Fact]
    public void NoUtilityNamesAnyCompanionType()
    {
        string product = Product();
        var names = new HashSet<string>(StringComparer.Ordinal) { "Domain.Companions", "Adapters.Companions", "Ui.Companions" };
        var declaration = new Regex(@"^\s*(?:internal|public)\s+(?:(?:sealed|static|readonly|abstract)\s+)*(?:class|struct|enum|interface)\s+(\w+)",
            RegexOptions.Multiline);
        foreach (string file in Sources(product).Where(InCompanions))
            foreach (Match match in declaration.Matches(File.ReadAllText(file)))
                names.Add(match.Groups[1].Value);
        Assert.Contains("GunnarCompanionRecord", names);
        Assert.Contains("GunnarPresentation", names);

        var offences = new List<string>();
        foreach (string file in Sources(product).Where(f => !InCompanions(f)))
        {
            string rel = Path.GetRelativePath(product, file).Replace('\\', '/');
            string code = Regex.Replace(File.ReadAllText(file), @"//[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            Allowed.TryGetValue(rel, out string[]? allowed);
            foreach (string name in names)
            {
                if (allowed != null && allowed.Contains(name)) continue;
                if (Regex.IsMatch(code, @"(?<![\w])" + Regex.Escape(name) + @"(?![\w])"))
                    offences.Add(rel + " names " + name);
            }
        }

        // The shared story library's own types (QuestState and the rest) are the
        // existing test's: it refuses `TheConcernedCat.Companions` outside these
        // folders. A text check: a `//` inside a string literal hides the rest of
        // its line from this one.
        Assert.True(offences.Count == 0, "A utility names the companion:\n" + string.Join("\n", offences));
    }

    private static bool InCompanions(string file) =>
        file.Replace('\\', '/').Contains("/Companions/", StringComparison.Ordinal);

    private static IEnumerable<string> Sources(string product) =>
        Directory.GetFiles(product, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal) &&
                        !f.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal));

    private static string Product()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null)
        {
            string candidate = Path.Combine(here.FullName, "src", "ConcernedTeamster");
            if (Directory.Exists(Path.Combine(candidate, "Domain", "Companions"))) return candidate;
            here = here.Parent;
        }

        throw new DirectoryNotFoundException("src/ConcernedTeamster not found above the test assembly.");
    }
}
