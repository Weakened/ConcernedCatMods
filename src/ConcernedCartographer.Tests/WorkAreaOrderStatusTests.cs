using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.Interop.WorkOrders;

namespace ConcernedCartographer.Tests;

/// <summary>#296: the map shows the worker, progress and attention reason of
/// an order running in a drawn area, from concernedcat.workorders/1, and says
/// nothing it cannot know.</summary>
public class WorkAreaOrderStatusTests
{
    private const string World = "777";

    private static WorkOrderLookup Lookup(params PublishedWorkOrder[] orders) =>
        WorkOrderClient.List(
            request => WorkOrderResponder.Answer(request, "0.1.0", () => new PublishedWorkOrders(World, orders)),
            World,
            "test");

    private static PublishedWorkOrder Order(string areaId, bool attention) =>
        new(areaId, "Thorstein", "Order: collecting", attention ? "Every loose stone here has been picked." : string.Empty,
            attention, new[] { "Stone: 4 of 10" });

    private static string DrawSquare(WorkAreaEditor editor, string name, float x)
    {
        editor.BeginDraw(name);
        float[][] corners = { new[] { x, 0f }, new[] { x + 30f, 0f }, new[] { x + 30f, 30f }, new[] { x, 30f }, new[] { x, 0f } };
        for (int leg = 0; leg < 4; leg++)
        {
            for (int step = 0; step < 12; step++)
            {
                float t = step / 12f;
                float px = corners[leg][0] + ((corners[leg + 1][0] - corners[leg][0]) * t);
                float pz = corners[leg][1] + ((corners[leg + 1][1] - corners[leg][1]) * t);
                editor.HandleMapFrame(true, new RoadPoint(px, 0f, pz));
            }
        }

        editor.HandleMapFrame(true, new RoadPoint(x, 0f, 0f));
        editor.HandleMapFrame(false, new RoadPoint(x, 0f, 0f));
        return editor.SelectedId!;
    }

    [Fact]
    public void TheSelectedAreaShowsWorkerStateProgressAndAttention()
    {
        WorkOrderLookup lookup = Lookup(Order("wa-a", attention: true));
        string text = WorkAreaOrders.Describe(lookup, "wa-a");

        Assert.StartsWith("Thorstein: Order: collecting", text);
        Assert.Contains("Stone: 4 of 10", text);
        Assert.Contains("Needs attention: Every loose stone here has been picked.", text);

        // The panel box truncates at the bottom: the reason must come before
        // any progress line, straight after the state.
        string[] lines = text.Split('\n');
        Assert.StartsWith("Needs attention:", lines[1]);

        Assert.DoesNotContain("Needs attention", WorkAreaOrders.Describe(Lookup(Order("wa-a", attention: false)), "wa-a"));
        Assert.Equal("No worker order runs in this area.", WorkAreaOrders.Describe(lookup, "wa-b"));
    }

    [Fact]
    public void WithoutAnAnswerTheMapSaysNothing_NotNoOrder()
    {
        Assert.Equal(string.Empty, WorkAreaOrders.Describe(null, "wa-a"));
        Assert.Equal(string.Empty, WorkAreaOrders.Describe(WorkOrderClient.List(null, World, "t"), "wa-a"));
        Assert.Equal(string.Empty, WorkAreaOrders.Describe(Lookup(Order("wa-a", false)), null));
        Assert.Equal(WorkAreaOrderMark.None, WorkAreaOrders.MarkFor(null, "wa-a"));
    }

    [Fact]
    public void OutlinesCarryTheOrderBadge()
    {
        var store = new WorkAreaStore();
        int seed = 0;
        var editor = new WorkAreaEditor(store, () => new Guid(++seed, 2, 2, new byte[8]), () => DateTime.UtcNow);
        string grove = DrawSquare(editor, "Grove", 0f);
        string copse = DrawSquare(editor, "Copse", 300f);

        // Copse is selected (drawn last); Grove, saved, has the order.
        List<WorkAreaOutline> outlines = WorkAreaOutlines.Collect(store, editor, Lookup(Order(grove, attention: true)));
        Assert.Equal(2, outlines.Count);
        Assert.Equal(WorkAreaOrderMark.NeedsAttention, Assert.Single(outlines, o => o.Emphasis == WorkAreaOutlineEmphasis.Saved).Mark);
        Assert.Equal(WorkAreaOrderMark.None, Assert.Single(outlines, o => o.Emphasis == WorkAreaOutlineEmphasis.Selected).Mark);

        Assert.All(WorkAreaOutlines.Collect(store, editor), outline => Assert.Equal(WorkAreaOrderMark.None, outline.Mark));
        Assert.Equal(WorkAreaOrderMark.Working, WorkAreaOrders.MarkFor(Lookup(Order(copse, false)), copse));
    }

    [Fact]
    public void TheSignatureChangesOnlyWithWhatTheMapShows()
    {
        string working = WorkAreaOrders.Signature(Lookup(Order("wa-a", false)));
        Assert.Equal(working, WorkAreaOrders.Signature(Lookup(Order("wa-a", false))));
        Assert.NotEqual(working, WorkAreaOrders.Signature(Lookup(Order("wa-a", true))));
        Assert.Equal(string.Empty, WorkAreaOrders.Signature(null));
    }

    [Fact]
    public void TheRuntimePanelAndOutlineLayerAreWired()
    {
        string runtime = Source("Runtime", "CartographerRuntime.cs");
        Assert.Contains("_workOrders.Poll(Time.unscaledTime, ordersWorld)", runtime);
        Assert.Contains("areaId => WorkAreaOrders.Describe(_workOrders.Latest, areaId)", runtime);
        Assert.Contains("_workOrders.Forget();", runtime);

        Assert.Contains("_order.text = _orderText(editor.SelectedId);", Source("Map", "WorkAreasPanel.cs"));
        Assert.Contains("outline.Mark == Atlas.WorkAreaOrderMark.NeedsAttention", Source("Map", "RoadVectorLayer.cs"));

        string discovery = Source("Runtime", "Interop", "WorkOrderProviderDiscovery.cs");
        Assert.Contains("Chainloader.PluginInfos.TryGetValue(WorkOrderContract.ProviderGuid", discovery);
        Assert.DoesNotContain("TheConcernedCat.ConcernedForeman", discovery);
        Assert.Contains("private const float PollSeconds = 1f;", discovery);
    }

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }

        string path = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("no repo root"), "src", "ConcernedCartographer");
        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }
}
