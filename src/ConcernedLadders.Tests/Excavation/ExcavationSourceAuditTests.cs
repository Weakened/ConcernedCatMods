using System.Text.RegularExpressions;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 slice 2: the excavation adapter changes terrain in exactly one way. A text audit of the product
/// source (not the compiled DLL): it pins the one terrain-tool call to one file and refuses the ways of changing the
/// world this slice is not authorized to use. It cannot see a call reached through a delegate or reflection invoke;
/// the reflection the adapter does use is pinned to reads.</summary>
public sealed class ExcavationSourceAuditTests
{
    private static string ProductRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }
        string root = directory?.FullName ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        return Path.Combine(root, "src", "ConcernedLadders");
    }

    private static IEnumerable<(string Path, string Text)> Sources(string? subfolder = null)
    {
        string root = ProductRoot();
        string start = subfolder == null ? root : Path.Combine(root, subfolder);
        foreach (string file in Directory.EnumerateFiles(start, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)) continue;
            yield return (relative, StripComments(File.ReadAllText(file)));
        }
    }

    /// <summary>Removes // and /* */ comments so documentation that names a call is not counted as one. String
    /// literals are left alone (none of the audited tokens appears in one).</summary>
    private static string StripComments(string text) =>
        Regex.Replace(text, @"/\*.*?\*/|//[^\n]*", "", RegexOptions.Singleline);

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    [Fact]
    public void TheTerrainToolCallIsMadeOnceInOneFile()
    {
        var hits = Sources().Select(s => (s.Path, N: Count(s.Text, @"\bSpawnOnHitTerrain\s*\("))).Where(h => h.N > 0).ToList();
        Assert.Single(hits);
        Assert.Equal("Excavation/ExcavationRuntime.cs", hits[0].Path);
        Assert.Equal(1, hits[0].N);
    }

    [Theory]
    [InlineData(@"\bInvokeRPC\s*\(")]
    [InlineData(@"\bInvokeRoutedRPC\s*\(")]
    [InlineData(@"\bSetHeight\s*\(")]
    [InlineData(@"\bApplyOperation\s*\(")]
    [InlineData(@"\bGetAndCreateTerrainCompiler\s*\(")]
    [InlineData(@"\bClaimOwnership\s*\(")]
    [InlineData(@"\bSetOwner\s*\(")]
    [InlineData(@"\bGetZDO\s*\(")]
    [InlineData(@"\bZDOMan\b")]
    [InlineData(@"\bInstantiate\s*\(")]
    [InlineData(@"\bSetValue\s*\(")]
    [InlineData(@"\bPrefabManager\b")]
    [InlineData(@"\bDestroyImmediate\s*\(")]
    [InlineData(@"\.Damage\s*\(")]
    [InlineData(@"\bm_levelDelta\s*\[")]
    public void TheAdapterNeverChangesTheWorldAnyOtherWay(string pattern)
    {
        foreach ((string path, string text) in Sources("Excavation"))
        {
            Assert.True(Count(text, pattern) == 0, path + " uses " + pattern + ".");
        }
        foreach ((string path, string text) in Sources("Domain/Excavation"))
        {
            Assert.True(Count(text, pattern) == 0, path + " uses " + pattern + ".");
        }
    }

    [Fact]
    public void OnlyTheAdaptersOwnContainerIsEverDestroyed()
    {
        var hits = Sources("Excavation").Select(s => (s.Path, N: Count(s.Text, @"\bDestroy\s*\("))).Where(h => h.N > 0).ToList();
        Assert.Single(hits);
        Assert.Equal("Excavation/ExcavationTerrainOps.cs", hits[0].Path);
        Assert.Equal(1, hits[0].N);
        Assert.Matches(@"Object\.Destroy\s*\(\s*_container\s*\)", Sources("Excavation").Single(s => s.Path == hits[0].Path).Text);
    }

    [Fact]
    public void TheAuditSeesWhatItLooksFor()
    {
        // The audit must fail when the protected property is removed: a planted second spawn and a planted RPC are seen.
        string planted = StripComments("x.SpawnOnHitTerrain(a); // SpawnOnHitTerrain( in a comment\n view.InvokeRPC(\"x\");");
        Assert.Equal(1, Count(planted, @"\bSpawnOnHitTerrain\s*\("));
        Assert.Equal(1, Count(planted, @"\bInvokeRPC\s*\("));
    }
}
