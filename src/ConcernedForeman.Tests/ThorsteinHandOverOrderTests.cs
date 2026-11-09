using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ConcernedForeman.Tests;

/// <summary>The order of the hand-over in the game-bound runtime, which no test
/// project can run: it names the game's character, its scene and Concerned
/// NPC's extraction. The leases are tested for real in
/// <see cref="ThorsteinBodyTests"/>. What those tests cannot see is whether the
/// runtime takes the figure down BEFORE it gives up the lease the figure stands
/// on, and builds it only AFTER the lease is granted. A reorder there would make
/// a figure that outlives its permission. So the shipped source is read, and
/// the order is checked statement by statement.
///
/// This is a text check with the limits of one: it proves the calls are
/// written in this order inside these methods, not that control flow could not
/// route around them. A reviewer still reads the file.</summary>
public sealed class ThorsteinHandOverOrderTests
{
    private static readonly string Runtime = Read(Path.Combine(
        "src", "ConcernedForeman", "Runtime", "Presentation", "ThorsteinPresentation.cs"));

    private static readonly string Settlement = Read(Path.Combine(
        "src", "ConcernedForeman", "Runtime", "Settlement", "SettlementRuntime.cs"));

    [Fact]
    public void TheHandOverTakesTheFigureDownBeforeAskingForTheWorker()
    {
        string body = Method(Runtime, "internal bool TakeWorkerBody(");
        AssertBefore(body, "TakeDown();", "_bodies.TryTakeWorker(");
    }

    [Fact]
    public void APassThatDoesNotWantTheFigureTakesItDownBeforeReconciling()
    {
        string body = Method(Runtime, "private void Pass(");
        AssertBefore(body, "TakeDown();", "_bodies.Reconcile(_last.Kind);");
    }

    [Fact]
    public void TheFigureIsBuiltOnlyAfterItsLeaseIsGrantedAndHeld()
    {
        string body = Method(Runtime, "private void Pass(");
        AssertBefore(body, "_bodies.Reconcile(ThorsteinBodyKind.Presentation)", "Build(home");
        Assert.Matches(new Regex(@"_bodies\.HoldsPresentation[^;{]*\)\s*\{\s*Build\(home", RegexOptions.Singleline), body);
    }

    [Fact]
    public void AWorldUnloadTakesTheFigureDownBeforeReleasingAndForgetting()
    {
        string body = Method(Runtime, "internal void OnWorldUnloaded(");
        AssertBefore(body, "TakeDown();", "_bodies.Reconcile(ThorsteinBodyKind.None);");
        AssertBefore(body, "_bodies.Reconcile(ThorsteinBodyKind.None);", "_bodies.Forget();");
    }

    [Fact]
    public void SpawnAsksForTheHandOverBeforeTheBodyIsCreated()
    {
        string body = Method(Settlement, "private string Spawn(");
        AssertBefore(
            body,
            "handOver(out Vector3? standing, out BodyLease? lease, out string refusal)",
            "ForemanWorkerPrefab.Spawn(lease, position");
    }

    [Fact]
    public void SpawnHasNoPathToABodyWithoutAHandOver()
    {
        // #372: no hand-over means no lease, and the library refuses a body
        // without one - but the refusal is also written here, so a runtime that
        // stopped wiring the hand-over says why rather than failing quietly.
        string body = Method(Settlement, "private string Spawn(");
        Assert.Matches(new Regex(@"if\s*\(\s*handOver\s*==\s*null\s*\)\s*\{\s*return\s", RegexOptions.Singleline), body);
        AssertBefore(body, "handOver == null", "ForemanWorkerPrefab.Spawn(lease, position");
    }

    [Fact]
    public void ThePresentationHandsBackTheLeaseItWasGranted()
    {
        string body = Method(Runtime, "internal bool TakeWorkerBody(");
        AssertBefore(body, "_bodies.TryTakeWorker(", "lease = granted ? _bodies.WorkerLease : null;");
        // While an order holds him there is no lease this runtime may show, so
        // the hand-over refuses rather than letting a body be built without one.
        Assert.Matches(new Regex(
            @"if\s*\(\s*!granted\s*&&\s*_npc\.IsRegistered\s*&&\s*!_npc\.Modes\.MayRetireBody\s*\)\s*\{[^}]*return\s+false\s*;",
            RegexOptions.Singleline), body);
    }

    [Fact]
    public void ADespawnGivesTheWorkerLeaseBackOnlyAfterTheBodyIsGone()
    {
        string body = Method(Settlement, "private string Despawn(");
        AssertBefore(body, "view.Destroy();", "ReleaseWorkerBody?.Invoke();");
    }

    [Fact]
    public void ThePluginBuildsTheRuntimeWiresTheHandOverAndTicksIt()
    {
        // A runtime with no caller is not a feature. The plugin is not linked into
        // any test project, so the wiring is read: construction, the four seams
        // the settlement runtime calls, the tick with its condition, and the
        // world-unload take-down before Concerned NPC ends the world.
        string plugin = Read(Path.Combine("src", "ConcernedForeman", "Plugin.cs"));
        string tight = Regex.Replace(plugin, @"\s+", string.Empty);

        Assert.Contains("newRuntime.Presentation.ThorsteinPresentation(", tight);
        Assert.Contains("_settlement.TakeWorkerBody=presentation.TakeWorkerBody;", tight);
        Assert.Contains("_settlement.ReleaseWorkerBody=presentation.ReleaseWorkerBody;", tight);
        Assert.Contains("_settlement.BodiesChanged=presentation.NoteBodiesChanged;", tight);
        Assert.Contains("if(worldIsUp){_presentation?.Tick(Time.deltaTime);}", tight);
        AssertBefore(tight, "_presentation?.OnWorldUnloaded();", "_npc?.NoteWorldUnloaded();");
    }

    private static void AssertBefore(string text, string first, string second)
    {
        int a = text.IndexOf(first, StringComparison.Ordinal);
        int b = text.IndexOf(second, StringComparison.Ordinal);
        Assert.True(a >= 0, "missing: " + first);
        Assert.True(b >= 0, "missing: " + second);
        Assert.True(a < b, "'" + first + "' must come before '" + second + "'");
    }

    /// <summary>The text of one method: from its signature to the brace that
    /// closes it, counted.</summary>
    private static string Method(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(start, index - start + 1);
            }
        }

        throw new InvalidOperationException("unbalanced braces after " + signature);
    }

    private static string Read(string relative)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null)
        {
            string candidate = Path.Combine(here.FullName, relative);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            here = here.Parent;
        }

        throw new FileNotFoundException("Shipped source not found above the test assembly.", relative);
    }
}
