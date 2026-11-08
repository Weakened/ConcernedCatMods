using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ConcernedForeman.Tests;

/// <summary>#379, read from source because the runtimes bind the game: the
/// shared plans are really wired into production (a helper with no caller is
/// not an adoption), opened after the settlement record they sit beside and
/// closed with the world, and there is a way out of a stopped plan.</summary>
public sealed class ThorsteinPlanWiringTests
{
    private static string Source(params string[] parts)
    {
        string path = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(path, "ConcernedCatMods.sln")))
        {
            path = Directory.GetParent(path)!.FullName;
        }

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return Regex.Replace(Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);
    }

    private static int At(string text, string what)
    {
        int index = text.IndexOf(what, StringComparison.Ordinal);
        Assert.True(index >= 0, "missing: " + what);
        return index;
    }

    [Fact]
    public void ThePluginOpensBothPlansAfterTheSettlementRecordAndClosesThemWithTheWorld()
    {
        string plugin = Source("src", "ConcernedForeman", "Plugin.cs");
        Assert.Contains("_plans = new ThorsteinPlans(ForemanPaths.Settlements);", plugin);
        Assert.Contains("plans: _plans);", plugin);
        Assert.Equal(2, Regex.Matches(plugin, @"plans:\s*_plans\)").Count);
        Assert.True(At(plugin, "_settlement?.OnWorldLoaded();") < At(plugin, "_plans?.OnWorldLoaded(_settlement?.Custody.Scope, world);"));
        Assert.True(At(plugin, "_construction?.OnWorldUnloaded();") < At(plugin, "_plans?.OnWorldUnloaded();"));
    }

    [Fact]
    public void TheCollectionLoopIsGivenTheCollectionPlanAsItsWitness()
    {
        string runtime = Source("src", "ConcernedForeman", "Runtime", "Collection", "CollectionRuntime.cs");
        Assert.Contains("new CollectionPlanWitness(runtime._plans.Collect, runtime.PlanEvidence, runtime.MeasureCarried)", runtime);
        Assert.Contains("_plans.MeasureCollect = MeasureCarried;", runtime);
        Assert.Contains("case \"resolve\":", runtime);
        Assert.Contains("_plans.Resolve(", runtime);
    }

    [Fact]
    public void TheConstructionLoopMovesMaterialOnlyThroughTheWitness()
    {
        string runtime = Source("src", "ConcernedForeman", "Runtime", "Construction", "ShelterConstructionRuntime.cs");
        Assert.Contains("_witness = new WitnessedBuildMaterials(_materials, plans.Build, PlanEvidence);", runtime);
        Assert.Contains("loopMaterials = _witness;", runtime);
        Assert.Matches(new Regex(@"new WorkerBuildWalk\(motion, \(\) => _job\),\s*loopMaterials,"), runtime);
        Assert.Contains("_witness?.Ended(true, \"the shelter is finished\");", runtime);
        Assert.Contains("_witness?.Ended(false, _loop.Reason);", runtime);
    }

    [Fact]
    public void TheCollectionLoopAnnouncesBeforeEveryMovementOfMaterial()
    {
        string loop = Source("src", "Shared", "Settlement", "Collection", "Planning", "SoloCollectionLoop.cs");
        Assert.True(At(loop, "Announce(\"about to pick \"") < At(loop, "_pickup.TryPick("));
        Assert.True(At(loop, "Announce(\"about to strike \"") < At(loop, "_felling.TryStrike("));
        Assert.True(At(loop, "Announce(\"about to deposit \"") < At(loop, "_custody.Executor.Execute("));
        Assert.Single(Regex.Matches(loop, @"_pickup\.TryPick\("));
        Assert.Single(Regex.Matches(loop, @"_felling\.TryStrike\("));
        Assert.Single(Regex.Matches(loop, @"_custody\.Executor\.Execute\("));
    }
}
