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
    [InlineData(@"\b(levels|smooths)\s*\[[^\]]*\]\s*(=[^=]|\+=|-=|\+\+|--)")]
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

    private static string SourceText(string relative) => Sources().Single(s => s.Path == relative).Text;

    /// <summary>The adapter properties a game-free test cannot see, pinned in the text that sets them. Removing or
    /// flipping any of them fails here.</summary>
    [Theory]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_raise\s*=\s*true\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_square\s*=\s*true\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_raiseDelta\s*=\s*-geometry\.Depth\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_level\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_smooth\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"settings\.m_paintCleared\s*=\s*false\s*;")]
    [InlineData("Excavation/ExcavationTerrainOps.cs", @"op\.m_spawnOnPlaced\s*=\s*null\s*;")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"CheckAccess\s*\([^;]*AccessCheckRadius\s*,\s*flash\s*:\s*true\s*,\s*wardCheck\s*:\s*true\s*\)")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"IsInside\s*\(\s*centre\s*,\s*radius\s*,\s*buildCheck\s*:\s*true\s*\)")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"float\s+radius\s*=\s*plan\.Geometry\.AccessCheckRadius\s*;")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"GetComponentInParent<Vagon>\(\)\s*!=\s*null\s*\|\|\s*hit\.GetComponentInParent<Ship>\(\)\s*!=\s*null")]
    [InlineData("Excavation/ExcavationWorldChecks.cs", @"if\s*\(\s*!vehicle\s*&&\s*hit\.GetComponentInParent<WearNTear>\(\)\s*!=\s*null\s*\)")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"if\s*\(\s*!ExcavationTerrainRules\.LatticeSupported\s*\(\s*tile\.m_width\s*,\s*tile\.m_scale\s*,\s*origin\.x\s*,\s*origin\.z\s*\)\s*\)\s*return\s+ExcavationReadFault\.UnsupportedLattice\s*;")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"if\s*\(\s*tile\.m_doLateUpdate\s*!=\s*0\s*\)\s*return\s+ExcavationReadFault\.Rebuilding\s*;")]
    [InlineData("Excavation/ExcavationTerrainReader.cs", @"WriteTo\s*\(\s*samples\s*,\s*dx\s*,\s*dz\s*,\s*ExcavationTerrainRules\.ExpectedCopies\s*\(")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"if\s*\(\s*op\s*==\s*null\s*\|\|\s*!_inFlight\.TryStart\s*\(\s*plan\s*,\s*Time\.time\s*\)\s*\)")]
    [InlineData("Excavation/ExcavationRuntime.cs", @"ExcavationPlanner\.Check\s*\(\s*plan\s*,\s*after\s*\)")]
    public void TheAdapterPropertiesAreWhereTheyMustBe(string relative, string pattern)
    {
        Assert.Equal(1, Count(SourceText(relative), pattern));
    }

    [Fact]
    public void TheTerrainToolCallComesAfterTheSlotIsTaken()
    {
        string runtime = SourceText("Excavation/ExcavationRuntime.cs");
        int slot = runtime.IndexOf("_inFlight.TryStart(plan, Time.time)", StringComparison.Ordinal);
        int spawn = Regex.Match(runtime, @"\bSpawnOnHitTerrain\s*\(").Index;
        Assert.True(slot > 0 && spawn > slot, "The single SpawnOnHitTerrain call must follow the in-flight slot.");
    }

    [Fact]
    public void TheAuditSeesWhatItLooksFor()
    {
        // The audit must fail when the protected property is removed: a planted second spawn and a planted RPC are seen.
        string planted = StripComments("x.SpawnOnHitTerrain(a); // SpawnOnHitTerrain( in a comment\n view.InvokeRPC(\"x\");");
        Assert.Equal(1, Count(planted, @"\bSpawnOnHitTerrain\s*\("));
        Assert.Equal(1, Count(planted, @"\bInvokeRPC\s*\("));
        Assert.Equal(1, Count("levels[index] = 0f;", @"\b(levels|smooths)\s*\[[^\]]*\]\s*(=[^=]|\+=|-=|\+\+|--)"));
        Assert.Equal(0, Count("float level = levels[index];", @"\b(levels|smooths)\s*\[[^\]]*\]\s*(=[^=]|\+=|-=|\+\+|--)"));
    }
}
