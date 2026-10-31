using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.Interop.WorkArea;

namespace ConcernedCartographer.Tests;

/// <summary>#296 slice 2: the visible work-area editor's game-free half (the
/// draw session, the editor the panel calls, the outline list), and audits
/// that the panel, the toolbar entry, the map input and the outline layer are
/// actually wired in production.</summary>
public class WorkAreaEditorTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static int _seed;

    private static WorkAreaEditor NewEditor(WorkAreaStore store) =>
        new(store, () => new Guid(Interlocked.Increment(ref _seed), 1, 1, new byte[8]), () => Now);

    private static RoadPoint At(float x, float z) => new(x, 30f, z);

    /// <summary>A hand-drawn loop: every 2 m along the given corners, back to
    /// the first corner when <paramref name="close"/>.</summary>
    private static List<RoadPoint> Trace(bool close, params (float X, float Z)[] corners)
    {
        var points = new List<RoadPoint>();
        int legs = close ? corners.Length : corners.Length - 1;
        for (int leg = 0; leg < legs; leg++)
        {
            (float ax, float az) = corners[leg];
            (float bx, float bz) = corners[(leg + 1) % corners.Length];
            float length = MathF.Sqrt(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az)));
            int steps = Math.Max(1, (int)(length / 2.5f));
            for (int step = 0; step < steps; step++)
            {
                float t = step / (float)steps;
                points.Add(At(ax + ((bx - ax) * t), az + ((bz - az) * t)));
            }
        }

        points.Add(close ? At(corners[0].X, corners[0].Z) : At(corners[^1].X, corners[^1].Z));
        return points;
    }

    private static List<RoadPoint> Square(float size = 40f, float x = 0f, float z = 0f) =>
        Trace(true, (x, z), (x + size, z), (x + size, z + size), (x, z + size));

    /// <summary>Feeds a stroke as held frames then one release, the way the
    /// runtime does; returns the last message.</summary>
    private static string? Draw(WorkAreaEditor editor, List<RoadPoint> stroke)
    {
        string? message = null;
        foreach (RoadPoint point in stroke)
        {
            message = editor.HandleMapFrame(true, point) ?? message;
        }

        return editor.HandleMapFrame(false, stroke[^1]) ?? message;
    }

    // ------------------------------------------------------------ session

    [Fact]
    public void AClickTwitchNeverBecomesAStroke()
    {
        var session = new WorkAreaDrawSession();
        session.Begin("Grove");
        Assert.Equal(WorkAreaDrawSession.Outcome.None, session.Observe(true, At(0, 0), out _));
        Assert.Equal(WorkAreaDrawSession.Outcome.None, session.Observe(true, At(0.5f, 0), out _));
        Assert.Equal(WorkAreaDrawSession.Outcome.None, session.Observe(false, At(0.5f, 0), out List<RoadPoint>? done));
        Assert.Null(done);
        Assert.True(session.Active);
        Assert.Empty(session.Draft);
    }

    [Fact]
    public void PointsAreSpacedAndReleaseHandsTheStrokeBackAndEndsTheSession()
    {
        var session = new WorkAreaDrawSession();
        session.Begin("Grove");
        session.Observe(true, At(0, 0), out _);
        Assert.Equal(WorkAreaDrawSession.Outcome.Changed, session.Observe(true, At(3, 0), out _));
        Assert.Equal(2, session.Draft.Count);
        Assert.Equal(WorkAreaDrawSession.Outcome.None, session.Observe(true, At(3.5f, 0), out _));
        Assert.Equal(WorkAreaDrawSession.Outcome.Changed, session.Observe(true, At(6, 0), out _));
        Assert.Equal(3, session.Draft.Count);

        Assert.Equal(WorkAreaDrawSession.Outcome.Completed, session.Observe(false, At(6, 0), out List<RoadPoint>? done));
        Assert.Equal(3, done!.Count);
        Assert.False(session.Active);
        Assert.Empty(session.Draft);
        Assert.Equal(WorkAreaDrawSession.Outcome.None, session.Observe(true, At(9, 0), out _));
    }

    [Fact]
    public void AStrokePastTheCapIsDropped_AndNothingDrawsUntilTheButtonIsReleased()
    {
        var session = new WorkAreaDrawSession();
        session.Begin("Grove");
        session.Observe(true, At(0, 0), out _);
        int index = 1;
        WorkAreaDrawSession.Outcome last = WorkAreaDrawSession.Outcome.None;
        while (last != WorkAreaDrawSession.Outcome.Overflowed)
        {
            Assert.True(index <= WorkAreaDrawSession.MaxStrokePoints + 1, "the cap never tripped");
            last = session.Observe(true, At(index++ * 3f, 0), out List<RoadPoint>? done);
            Assert.Null(done);
        }

        Assert.True(session.Active);
        Assert.Empty(session.Draft);

        // The same drag goes on, and on: nothing draws, nothing completes.
        for (int more = 0; more < 50; more++)
        {
            Assert.Equal(WorkAreaDrawSession.Outcome.None, session.Observe(true, At(index++ * 3f, 5f), out List<RoadPoint>? done));
            Assert.Null(done);
        }

        Assert.Empty(session.Draft);
        Assert.Equal(WorkAreaDrawSession.Outcome.None, session.Observe(false, At(0, 0), out List<RoadPoint>? released));
        Assert.Null(released);

        // After the release a new stroke starts normally.
        session.Observe(true, At(0, 0), out _);
        Assert.Equal(WorkAreaDrawSession.Outcome.Changed, session.Observe(true, At(3, 0), out _));
        Assert.Equal(2, session.Draft.Count);
    }

    [Fact]
    public void AnOverLongLineIsRefusedAndTheModeStaysOn()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        string? message = null;
        editor.HandleMapFrame(true, At(0, 0));
        for (int index = 1; index <= WorkAreaDrawSession.MaxStrokePoints + 5; index++)
        {
            message = editor.HandleMapFrame(true, At(index * 3f, 0)) ?? message;
        }

        Assert.Equal("Not saved: that line was too long. Draw a simpler loop, or Cancel.", message);
        Assert.Empty(store.Areas);
        Assert.True(editor.Session.Active);
        Assert.Equal("Grove", editor.Session.PendingName);

        // Releasing near where the line was dropped saves nothing: the tail of
        // that drag was never a stroke.
        Assert.Null(editor.HandleMapFrame(false, At(0, 0)));
        Assert.Empty(store.Areas);

        Draw(editor, Square());
        Assert.Equal("Grove", Assert.Single(store.Areas).Name);
    }

    // ------------------------------------------------------------- editor

    [Fact]
    public void AClosedLoopIsSavedSelectedAndDirty_AndNothingElseHappens()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        Assert.Contains("Grove", editor.BeginDraw("  Grove "));
        Assert.True(editor.Session.Active);

        string? message = Draw(editor, Square());

        WorkArea area = Assert.Single(store.Areas);
        Assert.Equal("Grove", area.Name);
        Assert.Equal(WorkAreaContract.Purposes.WoodGathering, area.Purpose);
        Assert.InRange(area.Boundary.AreaSquareMetres, 1500f, 1700f);
        Assert.True(area.Boundary.Points.Count <= WorkAreaPolygon.MaxPoints);
        Assert.Equal(area.Id, editor.SelectedId);
        Assert.True(store.IsDirty);
        Assert.False(editor.Session.Active);
        Assert.StartsWith("Saved \"Grove\"", message);
        Assert.Contains("Nothing starts", message);
    }

    [Fact]
    public void AnOpenLineIsRefused_NothingStored_AndTheModeStaysOnUnderTheSameName()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");

        string? message = Draw(editor, Trace(false, (0, 0), (60, 0), (60, 60)));

        Assert.Empty(store.Areas);
        Assert.False(store.IsDirty);
        Assert.True(editor.Session.Active);
        Assert.Equal("Grove", editor.Session.PendingName);
        Assert.Contains(WorkAreaStore.Describe(WorkAreaRefusal.NotClosed), message);

        // ...and the next loop under that mode is saved under that name.
        Draw(editor, Square());
        Assert.Equal("Grove", Assert.Single(store.Areas).Name);
    }

    [Fact]
    public void AFigureEightIsRefusedAsAShape_NeverStoredAsAnyRegion()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Eight");

        string? message = Draw(editor, Trace(true, (0, 0), (40, 40), (40, 0), (0, 40)));

        Assert.Empty(store.Areas);
        Assert.True(editor.Session.Active);
        Assert.StartsWith("Not saved:", message);
        Assert.Contains("Draw the loop again", message);
    }

    [Fact]
    public void AnOversizedLoopIsRefused()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Huge");
        Draw(editor, Square(WorkAreaPolygon.MaxExtentMetres + 40f));
        Assert.Empty(store.Areas);
    }

    [Fact]
    public void AnEmptyNameTakesTheFirstFreeDefault()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("");
        Assert.Equal("Area 1", editor.Session.PendingName);
        Draw(editor, Square());
        editor.BeginDraw(null);
        Assert.Equal("Area 2", editor.Session.PendingName);
    }

    [Fact]
    public void ANameTheStoreWouldRefuseIsRefusedBeforeDrawing()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        Draw(editor, Square());

        Assert.StartsWith("Not started:", editor.BeginDraw("grove"));
        Assert.False(editor.Session.Active);
        Assert.StartsWith("Not started:", editor.BeginDraw("bad\tname"));
        Assert.StartsWith("Not started:", editor.BeginDraw(new string('x', WorkAreaStore.MaxNameLength + 1)));
        Assert.False(editor.Session.Active);
    }

    [Fact]
    public void AFullWorldRefusesBeforeDrawing()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        for (int index = 0; index < WorkAreaStore.MaxAreas; index++)
        {
            editor.BeginDraw("A" + index);
            Draw(editor, Square(30f, index * 300f, 0f));
        }

        Assert.Equal(WorkAreaStore.MaxAreas, store.Areas.Count);
        Assert.StartsWith("Not started:", editor.BeginDraw("One more"));
        Assert.False(editor.Session.Active);
    }

    [Fact]
    public void CancelEndsTheModeAndKeepsNothing()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        foreach (RoadPoint point in Square().Take(20))
        {
            editor.HandleMapFrame(true, point);
        }

        Assert.NotEmpty(editor.Session.Draft);
        Assert.Equal("Drawing cancelled. Nothing was saved.", editor.CancelDraw());
        Assert.False(editor.Session.Active);
        Assert.Empty(editor.Session.Draft);
        Assert.Empty(store.Areas);
        Assert.Equal(string.Empty, editor.CancelDraw());
    }

    [Fact]
    public void RenameUsesTheStoresRules()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        Assert.Equal("Select a work area in the list first.", editor.RenameSelected("X"));

        editor.BeginDraw("Grove");
        Draw(editor, Square());
        editor.BeginDraw("Copse");
        Draw(editor, Square(30f, 500f, 0f));

        Assert.StartsWith("Not renamed:", editor.RenameSelected("grove"));
        Assert.Equal("Renamed to \"Birches\".", editor.RenameSelected(" Birches "));
        Assert.NotNull(store.Find("Birches"));
        Assert.Equal(store.Find("Birches")!.Id, editor.SelectedId);
    }

    [Fact]
    public void DeleteTakesTwoClicksOnTheSameSelection()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        Draw(editor, Square());
        string groveId = editor.SelectedId!;
        editor.BeginDraw("Copse");
        Draw(editor, Square(30f, 500f, 0f));
        string copseId = editor.SelectedId!;

        Assert.StartsWith("Click Delete again", editor.DeleteSelected());
        editor.Select(groveId);
        Assert.StartsWith("Click Delete again to remove \"Grove\"", editor.DeleteSelected());
        editor.Disarm();
        Assert.StartsWith("Click Delete again", editor.DeleteSelected());
        Assert.Equal("Deleted \"Grove\".", editor.DeleteSelected());

        Assert.Equal(copseId, Assert.Single(store.Areas).Id);
        Assert.Null(editor.SelectedId);
        Assert.Equal("Select a work area in the list first.", editor.DeleteSelected());
    }

    [Fact]
    public void ASelectionTheConsoleDeletedIsForgotten()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        Draw(editor, Square());
        Assert.NotNull(editor.SelectedId);

        WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "delete", "Grove", Guid.NewGuid(), Now);

        Assert.Null(editor.SelectedId);
        Assert.Equal("No work areas in this world yet.", editor.SelectionDetails());
    }

    [Fact]
    public void TheConsoleSeesWhatThePanelDrew()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        Draw(editor, Square());

        string listing = WorkAreaCommands.Execute(store, Array.Empty<AtlasRoute>(), "list", "", Guid.NewGuid(), Now);
        Assert.Contains("\"Grove\"", listing);
        Assert.Contains(store.Areas[0].Id, listing);
    }

    [Fact]
    public void RowsAreCappedWithAHiddenCountAndDetailsDescribeTheSelection()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        Assert.Empty(editor.Rows(6, out int none));
        Assert.Equal(0, none);

        for (int index = 0; index < 8; index++)
        {
            editor.BeginDraw("A" + index);
            Draw(editor, Square(30f, index * 300f, 0f));
        }

        List<(string Id, string Label)> rows = editor.Rows(6, out int hidden);
        Assert.Equal(6, rows.Count);
        Assert.Equal(2, hidden);
        Assert.StartsWith("A0 — about ", rows[0].Label);

        editor.Select(rows[2].Id);
        Assert.StartsWith("Selected: A2 — wood gathering, about ", editor.SelectionDetails());
        Assert.EndsWith(" corners.", editor.SelectionDetails());
    }

    [Fact]
    public void SelectIgnoresANameOrAnUnknownId()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        Draw(editor, Square());
        string id = editor.SelectedId!;
        editor.Select("Grove");
        editor.Select("wa-00000000000000000000000000000000");
        Assert.Equal(id, editor.SelectedId);
    }

    // ----------------------------------------------------------- outlines

    [Fact]
    public void OutlinesDrawTheSelectionOverTheRestAndTheDraftLast()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        editor.BeginDraw("Grove");
        Draw(editor, Square());
        string groveId = editor.SelectedId!;
        editor.BeginDraw("Copse");
        Draw(editor, Square(30f, 500f, 0f));
        editor.Select(groveId);

        editor.BeginDraw("Draft");
        foreach (RoadPoint point in Square(30f, 900f, 0f).Take(10))
        {
            editor.HandleMapFrame(true, point);
        }

        List<WorkAreaOutline> outlines = WorkAreaOutlines.Collect(store, editor);
        Assert.Equal(3, outlines.Count);
        Assert.Equal(WorkAreaOutlineEmphasis.Saved, outlines[0].Emphasis);
        Assert.True(outlines[0].Closed);
        Assert.Equal(WorkAreaOutlineEmphasis.Selected, outlines[1].Emphasis);
        Assert.Equal(store.Find(groveId)!.Boundary.Points.Count, outlines[1].Points.Count);
        Assert.Equal(WorkAreaOutlineEmphasis.Draft, outlines[2].Emphasis);
        Assert.False(outlines[2].Closed);
        Assert.Equal(editor.Session.Draft.Count, outlines[2].Points.Count);

        editor.CancelDraw();
        Assert.Equal(2, WorkAreaOutlines.Collect(store, editor).Count);
        Assert.Equal(2, WorkAreaOutlines.Collect(store, null).Count);
    }

    [Fact]
    public void EveryChangeTheOutlinesShowBumpsTheStamp()
    {
        var store = new WorkAreaStore();
        WorkAreaEditor editor = NewEditor(store);
        long stamp = editor.ChangeStamp;
        editor.BeginDraw("Grove");
        Assert.NotEqual(stamp, stamp = editor.ChangeStamp);
        editor.HandleMapFrame(true, At(0, 0));
        editor.HandleMapFrame(true, At(5, 0));
        Assert.NotEqual(stamp, stamp = editor.ChangeStamp);
        editor.CancelDraw();
        Assert.NotEqual(stamp, editor.ChangeStamp);
    }

    // --------------------------------------------------- strings, wiring

    [Fact]
    public void EveryAreasStringTheEditorAndPanelUseExists()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in new[]
        {
            Path.Combine(RepoRoot, "src", "ConcernedCartographer", "Domain", "Atlas", "WorkAreaEditing.cs"),
            Path.Combine(RepoRoot, "src", "ConcernedCartographer", "Map", "WorkAreasPanel.cs"),
            Path.Combine(RepoRoot, "src", "ConcernedCartographer", "Map", "MapUiCoordinator.cs"),
        })
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"((?:areas|toolbar)\\.[A-Za-z.]+)\""))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        Assert.Contains("toolbar.areas", used);
        Assert.Contains("areas.modeDraw", used);
        foreach (string key in used)
        {
            Assert.True(AtlasStrings.Defaults.ContainsKey(key), $"{key} has no English text in AtlasStrings.");
        }
    }

    [Fact]
    public void ThePanelToolbarMapInputAndOutlineLayerAreWiredInProduction()
    {
        string runtime = Source("Runtime", "CartographerRuntime.cs");
        Assert.Contains("_areasPanel = new WorkAreasPanel(", runtime);
        Assert.Contains("_mapUi.AreasClicked = () =>", runtime);
        Assert.Contains("OpenSidePanel(_areasToken, _areasPanel);", runtime);
        Assert.Contains("_areasPanel.HandleFrame();", runtime);
        Assert.Contains("_areaEditor.HandleMapFrame(", runtime);
        Assert.Contains("MapInputGate.ConsumeClicks = uiRouteMode || uiAreaMode;", runtime);
        Assert.Contains("_renderer.SetWorkAreaSource(() => WorkAreaOutlines.Collect(_workAreas, _areaEditor, _workOrders.Latest));", runtime);

        // The panel is handed no editor while the world's area file is
        // read-only, so the UI can never write over areas it could not read.
        int writable = runtime.IndexOf("private WorkAreaEditor? WritableAreaEditor()", StringComparison.Ordinal);
        Assert.True(writable > 0);
        Assert.Contains("_workAreaPersistence.IsReadOnly(uid)", runtime.Substring(writable, 400));

        // While an area is being drawn, a console-entered route mode does not
        // draw from the same drag.
        Assert.Contains("(uiRouteMode || (!uiAreaMode && Input.GetKey(_settings.RouteDrawModifier.Value)))", runtime);

        // Every way out of the panel ends the draw mode: Escape (which keeps
        // the panel), hiding, and the panel found hidden mid-draw.
        string panel = Source("Map", "WorkAreasPanel.cs");
        int escape = panel.IndexOf("Input.GetKeyDown(KeyCode.Escape)", StringComparison.Ordinal);
        Assert.True(escape > 0);
        Assert.Contains("CancelDraw()", panel.Substring(escape, 250));
        int hidden = panel.IndexOf("protected override void OnHidden()", StringComparison.Ordinal);
        Assert.True(hidden > 0);
        Assert.Contains("CancelDraw()", panel.Substring(hidden, 200));
        int stale = panel.IndexOf("if (!IsVisible && ModeActive)", StringComparison.Ordinal);
        Assert.True(stale > 0);
        Assert.Contains("CancelDraw()", panel.Substring(stale, 300));

        string toolbar = Source("Map", "MapUiCoordinator.cs");
        Assert.Contains("(\"toolbar.areas\", () => AreasClicked)", toolbar);

        Assert.Contains("_vectorLayer.SetWorkAreaSource(source);", Source("Map", "RoadOverlayRenderer.cs"));
        Assert.Contains("BakeWorkAreas(halfWidth);", Source("Map", "RoadVectorLayer.cs"));
    }

    private static string Source(string folder, string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedCartographer", folder, file));

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
