using System;
using System.IO;

namespace ConcernedForeman.Tests;

/// <summary>#296 slice 3: the Cartographer work-area path has production
/// callers, and keeps the rules its game-free half is tested for. The runtime
/// files are game-bound and not compiled here, so this reads their source.
/// </summary>
public sealed class WorkAreaScopeWiringTests
{
    private static string Source(params string[] parts)
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "ConcernedCatMods.sln")))
        {
            directory = Directory.GetParent(directory)!.FullName;
        }

        string path = directory;
        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    [Fact]
    public void TheRuntimeAsksCartographerAndHonoursTheChoiceFirst()
    {
        string runtime = Source("src", "ConcernedForeman", "Runtime", "Collection", "CollectionRuntime.cs");

        Assert.Contains("_scopes = new WorkScopeBuilder(_reader, _cartographerAreas.Describe);", runtime);
        Assert.Contains("case \"area\":", runtime);

        // The chosen area is tried before the harvest area and the circle, and
        // its refusal returns rather than falling through to them.
        int choice = runtime.IndexOf("if (_areaChoice != null)", StringComparison.Ordinal);
        int harvest = runtime.IndexOf("if (!_scopes.TryGetHarvestArea(out Designation? harvest, out failure))", StringComparison.Ordinal);
        Assert.True(choice > 0 && harvest > choice, "the chosen area must be resolved before the harvest area");
        string chosenBlock = runtime.Substring(choice, harvest - choice);
        Assert.Contains("CartographerAreaScopes.TryBuild(", chosenBlock);
        Assert.Contains("return false;", chosenBlock);

        // A recovered Cartographer order is rebound only from a fresh choice.
        Assert.Contains("case WorkScopeSource.CartographerWorkArea:", runtime);

        // The choice belongs to one world load.
        int drop = runtime.IndexOf("private void DropWorld()", StringComparison.Ordinal);
        Assert.True(drop > 0);
        Assert.Contains("_areaChoice = null;", runtime.Substring(drop, 700));
    }

    [Fact]
    public void ObservationAndTheSurveyUseTheBoundary()
    {
        Assert.Contains(
            "return CartographerAreaScopes.Observe(scope, _describeWorkArea, currentEpoch);",
            Source("src", "ConcernedForeman", "Runtime", "Collection", "WorkScopeBuilder.cs"));
        Assert.Contains(
            "if (scope.OutsideBoundary(point))",
            Source("src", "ConcernedForeman", "Runtime", "Collection", "WorldSourceSurvey.cs"));
    }

    [Fact]
    public void ThePanelChoosesThroughTheSameCommand()
    {
        string panel = Source("src", "ConcernedForeman", "Ui", "CollectionOrderPanel.cs");
        Assert.Contains("_collect(new[] { \"area\", \"next\" })", panel);
        Assert.Contains("_collect(new[] { \"area\", \"default\" })", panel);
    }

    [Fact]
    public void DiscoveryReadsOnlyAndNamesNoCartographerType()
    {
        string discovery = Source("src", "ConcernedForeman", "Runtime", "Collection", "CartographerWorkAreas.cs");
        Assert.Contains("Chainloader.PluginInfos.TryGetValue(WorkAreaContract.ProviderGuid", discovery);
        Assert.Contains("CapabilityMap.TryGetEndpoint(", discovery);
        Assert.DoesNotContain("TheConcernedCat.ConcernedCartographer", discovery);

        // The contract it speaks has no op that changes anything.
        string contract = Source("src", "Shared", "Interop", "WorkArea", "WorkAreaContract.cs");
        Assert.Contains("public const string Hello = \"hello\";", contract);
        Assert.Contains("public const string ListAreas = \"listAreas\";", contract);
        Assert.Contains("public const string DescribeArea = \"describeArea\";", contract);
        int ops = contract.IndexOf("internal static class Ops", StringComparison.Ordinal);
        int purposes = contract.IndexOf("internal static class Purposes", StringComparison.Ordinal);
        Assert.Equal(3, CountOf(contract.Substring(ops, purposes - ops), "public const string"));
    }

    private static int CountOf(string text, string token)
    {
        int count = 0;
        for (int at = text.IndexOf(token, StringComparison.Ordinal); at >= 0; at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
