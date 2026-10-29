using System.Reflection;
using System.Threading;
using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>The one collection allowed to load <c>AtlasStrings</c> overrides.
///
/// The overrides are a single process-wide static, replaced wholesale by
/// <c>LoadOverrides</c>. xUnit runs each test class as its own collection, and
/// collections run in parallel, so a test that loads an override and a test in
/// another class that reads (or resets) the catalog can interleave. That is how
/// <c>Overrides_Win_AndMissingKeysFallBackToEnglish</c> once saw "Pin Workbench"
/// instead of its own override (#453). With parallelization disabled this
/// collection runs on its own, after the parallel ones, so nothing else observes
/// or disturbs an override while it is loaded.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AtlasStringsOverridesCollection
{
    public const string Name = "AtlasStrings overrides (process-wide static)";
}

[Collection(AtlasStringsOverridesCollection.Name)]
public class AtlasStringsTests
{
    private static void Reset() => AtlasStrings.LoadOverrides(new Dictionary<string, string>());

    [Fact]
    public void Defaults_ResolveAndUnknownKeysFallBackToTheKey()
    {
        Reset();
        Assert.Equal("Pin Workbench", AtlasStrings.Get("workbench.title"));
        Assert.Equal("nonsense.key", AtlasStrings.Get("nonsense.key"));
    }

    [Fact]
    public void Overrides_Win_AndMissingKeysFallBackToEnglish()
    {
        try
        {
            AtlasStrings.LoadOverrides(new Dictionary<string, string> { ["workbench.title"] = "Nadel-Werkbank" });
            Assert.Equal("Nadel-Werkbank", AtlasStrings.Get("workbench.title"));
            Assert.Equal("Apply", AtlasStrings.Get("workbench.apply"));
        }
        finally
        {
            Reset();
        }
    }

    [Fact]
    public void BrokenTranslationFormat_NeverThrows()
    {
        try
        {
            AtlasStrings.LoadOverrides(new Dictionary<string, string> { ["hud.quickPinned"] = "Broken {0} {1} {2}" });
            string result = AtlasStrings.Format("hud.quickPinned", "X");
            Assert.Contains("X", result);
        }
        finally
        {
            Reset();
        }
    }

    /// <summary>#453: the interleaving of the failed run, replayed in order on two
    /// threads. This thread loads an override, a second thread does what
    /// <c>HulgiDialogueTests</c> used to do (reset to English), and this thread then
    /// reads the key and gets English, which is the observed failure. This test
    /// pins the mechanism, that the overrides are shared across threads (not
    /// thread- or async-local); it does not guard the isolation, which is
    /// <c>OnlyThisNonParallelCollectionLoadsOverrides</c>' job.</summary>
    [Fact]
    public void Overrides_AreProcessWide_SoAResetOnAnotherThreadIsSeenHere()
    {
        try
        {
            AtlasStrings.LoadOverrides(new Dictionary<string, string> { ["workbench.title"] = "Nadel-Werkbank" });

            var other = new Thread(Reset);
            other.Start();
            other.Join();

            Assert.Equal("Pin Workbench", AtlasStrings.Get("workbench.title"));
        }
        finally
        {
            Reset();
        }
    }

    /// <summary>#453: the isolation holds only while every writer of the shared
    /// overrides is in this class and this class is in the non-parallel
    /// collection. Both halves are checked: by reflection for the attributes, and
    /// by source for the writers, so a new test elsewhere that loads an override
    /// fails here instead of becoming the next intermittent red. The source scan
    /// covers the test project and the product sources the project file links in
    /// (<c>Domain</c>, <c>Runtime/VanillaMessage.cs</c> and <c>src/Shared/Companions</c>),
    /// where only the definition itself may spell the call. It is a text audit: a
    /// writer reached through a delegate or reflection spells no
    /// <c>LoadOverrides</c> call and is invisible to it.</summary>
    [Fact]
    public void OnlyThisNonParallelCollectionLoadsOverrides()
    {
        CustomAttributeData membership = Assert.Single(
            typeof(AtlasStringsTests).CustomAttributes,
            attribute => attribute.AttributeType == typeof(CollectionAttribute));
        Assert.Equal(AtlasStringsOverridesCollection.Name, membership.ConstructorArguments[0].Value);

        CollectionDefinitionAttribute? definition =
            typeof(AtlasStringsOverridesCollection).GetCustomAttribute<CollectionDefinitionAttribute>();
        Assert.NotNull(definition);
        Assert.True(definition!.DisableParallelization, "the overrides collection must not run in parallel");

        string src = Path.Combine(RepoRoot, "src");
        var writers = new List<string>();
        foreach (string root in new[]
        {
            Path.Combine(src, "ConcernedCartographer.Tests"),
            Path.Combine(src, "ConcernedCartographer", "Domain"),
            Path.Combine(src, "ConcernedCartographer", "Runtime", "VanillaMessage.cs"),
            Path.Combine(src, "Shared", "Companions"),
        })
        {
            IEnumerable<string> files = File.Exists(root)
                ? new[] { root }
                : Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(src, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) ||
                    relative.Contains("/bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains("LoadOverrides(", StringComparison.Ordinal))
                {
                    writers.Add(relative);
                }
            }
        }

        writers.Sort(StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                "ConcernedCartographer.Tests/AtlasStringsTests.cs",
                "ConcernedCartographer/Domain/Atlas/AtlasStrings.cs",
            },
            writers);
    }

    [Fact]
    public void TemplateRoundtrip_CoversEveryKey()
    {
        var template = new List<string>(AtlasStrings.TranslatorTemplate());
        Dictionary<string, string> parsed = AtlasStrings.ParseOverrides(template, out int skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(AtlasStrings.Defaults.Count, parsed.Count);
    }

    [Fact]
    public void UnknownKeysAndMalformedRows_AreSkipped()
    {
        var lines = new List<string>
        {
            AtlasStrings.TemplateHeader,
            "workbench.title\tOK",
            "not.a.real.key\tX",
            "noTabHere",
        };

        Dictionary<string, string> parsed = AtlasStrings.ParseOverrides(lines, out int skipped);
        Assert.Single(parsed);
        Assert.Equal(2, skipped);
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        }
    }
}
