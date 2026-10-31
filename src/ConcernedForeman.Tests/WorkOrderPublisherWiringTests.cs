using System;
using System.IO;

namespace ConcernedForeman.Tests;

/// <summary>#296: Foreman publishes concernedcat.workorders/1 from the same
/// facts and view as its own collection panel, and the contract only answers.
/// </summary>
public sealed class WorkOrderPublisherWiringTests
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

        return File.ReadAllText(path);
    }

    [Fact]
    public void ThePluginPublishesTheWorkOrderEndpointBuiltFromThePanelsFacts()
    {
        string plugin = Source("src", "ConcernedForeman", "Plugin.cs");
        Assert.Contains("public IReadOnlyDictionary<string, object> ConcernedCatCapabilities => _capabilities;", plugin);
        Assert.Contains("[WorkOrderCapabilityPublisher.Key] = _workOrderCapability.Endpoint,", plugin);

        int publisher = plugin.IndexOf("_workOrderCapability = new WorkOrderCapabilityPublisher(", StringComparison.Ordinal);
        Assert.True(publisher > 0);
        string body = plugin.Substring(publisher, 600);
        Assert.Contains("BuildOrderPanelFacts(settings, collection, custody, delivery, companions)", body);
        Assert.Contains("OrderPanelPresenter.Present(facts, null, Time.time)", body);
        Assert.Contains("WorkOrderSnapshot.From(", body);

        // The panel reads the same facts.
        Assert.Contains("() => BuildOrderPanelFacts(settings, collection, custody, delivery, companions),", plugin);
    }

    [Fact]
    public void ThePublisherOnlyAnswers()
    {
        string publisher = Source("src", "ConcernedForeman", "Runtime", "Interop", "WorkOrderCapabilityPublisher.cs");
        Assert.Contains("return WorkOrderResponder.Answer(request, _providerVersion, _snapshot);", publisher);

        string contract = Source("src", "Shared", "Interop", "WorkOrders", "WorkOrderContract.cs");
        int ops = contract.IndexOf("internal static class Ops", StringComparison.Ordinal);
        int keys = contract.IndexOf("internal static class Keys", StringComparison.Ordinal);
        string opsBlock = contract.Substring(ops, keys - ops);
        Assert.Contains("Hello = \"hello\"", opsBlock);
        Assert.Contains("ListOrders = \"listOrders\"", opsBlock);
        Assert.Equal(2, opsBlock.Split("public const string").Length - 1);
    }
}
