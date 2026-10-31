using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedCartographer.Roads;
using TheConcernedCat.Interop.WorkArea;
using TheConcernedCat.Interop.WorkOrders;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>#296 slice 2: one freehand boundary being drawn on the large map.
/// Pure state; the runtime feeds it the cursor and the held button each map
/// frame, exactly as the route Free Draw is fed. A stroke only begins once the
/// hold has travelled (the route <see cref="FreeDrawStrokeGate"/>, so a click
/// twitch never counts), points are kept at least
/// <see cref="RouteOperations.FreehandMinimumSpacingMeters"/> apart, and the
/// stroke is bounded at <see cref="MaxStrokePoints"/>: a longer line is
/// dropped as <see cref="Outcome.Overflowed"/> rather than growing without
/// limit, and the session then ignores the rest of that drag until the button
/// is released, so no new line starts mid-drag. Releasing the button completes
/// the stroke and ends the session; the caller decides whether it becomes an
/// area.</summary>
internal sealed class WorkAreaDrawSession
{
    /// <summary>A loop inside the 256 m extent limit, drawn at 2 m spacing, is
    /// a few hundred points; 2 048 leaves room for a wobbly hand and still
    /// bounds the memory and the per-frame outline bake.</summary>
    public const int MaxStrokePoints = 2048;

    public enum Outcome
    {
        /// <summary>Nothing visible changed this frame.</summary>
        None,

        /// <summary>The draft line changed; redraw it.</summary>
        Changed,

        /// <summary>The stroke ended; the session is over and the stroke is
        /// handed back for the caller to turn into an area or refuse.</summary>
        Completed,

        /// <summary>The stroke passed <see cref="MaxStrokePoints"/> and was
        /// dropped. The session stays on, but nothing is drawn until the
        /// button has been released.</summary>
        Overflowed,
    }

    private readonly FreeDrawStrokeGate _gate = new();
    private readonly List<RoadPoint> _stroke = new();
    private bool _awaitRelease;

    public bool Active { get; private set; }

    /// <summary>The name the finished area will be saved under.</summary>
    public string PendingName { get; private set; } = string.Empty;

    /// <summary>The live draft, for the outline layer. Empty between strokes.
    /// </summary>
    public IReadOnlyList<RoadPoint> Draft => _stroke;

    public void Begin(string name)
    {
        Active = true;
        PendingName = name ?? string.Empty;
        _stroke.Clear();
        _gate.Reset();
        _awaitRelease = false;
    }

    public void Cancel()
    {
        Active = false;
        _stroke.Clear();
        _gate.Reset();
        _awaitRelease = false;
    }

    public Outcome Observe(bool held, RoadPoint cursor, out List<RoadPoint>? completed)
    {
        completed = null;
        if (!Active)
        {
            return Outcome.None;
        }

        if (_awaitRelease)
        {
            // The rest of an over-long drag draws nothing; the next stroke
            // starts only after the button is let go.
            _awaitRelease = held;
            return Outcome.None;
        }

        FreeDrawStrokeGate.Decision decision = _gate.Observe(held, cursor);
        switch (decision.Kind)
        {
            case FreeDrawStrokeGate.DecisionKind.StartStroke:
                _stroke.Clear();
                _stroke.Add(decision.StrokeStart);
                _stroke.Add(cursor);
                return Outcome.Changed;
            case FreeDrawStrokeGate.DecisionKind.Append:
                if (_stroke.Count > 0 &&
                    _stroke[_stroke.Count - 1].HorizontalDistanceTo(cursor) < RouteOperations.FreehandMinimumSpacingMeters)
                {
                    return Outcome.None;
                }

                if (_stroke.Count >= MaxStrokePoints)
                {
                    _stroke.Clear();
                    _gate.Reset();
                    _awaitRelease = true;
                    return Outcome.Overflowed;
                }

                _stroke.Add(cursor);
                return Outcome.Changed;
            case FreeDrawStrokeGate.DecisionKind.EndStroke:
                completed = new List<RoadPoint>(_stroke);
                Cancel();
                return Outcome.Completed;
            default:
                return Outcome.None;
        }
    }
}

/// <summary>#296 slice 2: what the Work Areas panel does, without the panel.
/// Every change goes through <see cref="WorkAreaStore"/>, the same store and
/// the same rules as <c>cc_areas</c>, so the UI and the console cannot drift.
/// Selection is by stable id and survives renames; a deleted or vanished area
/// clears it. Drawing or saving an area never starts any work and names no
/// worker. Player-facing text comes from <see cref="AtlasStrings"/>; the
/// refusal reasons are the store's and the polygon's own (English, like the
/// console).</summary>
internal sealed class WorkAreaEditor
{
    private readonly WorkAreaStore _store;
    private readonly Func<Guid> _newId;
    private readonly Func<DateTime> _clock;
    private string? _selectedId;
    private string? _deleteArmedId;

    public WorkAreaEditor(WorkAreaStore store, Func<Guid> newId, Func<DateTime> clock)
    {
        _store = store;
        _newId = newId;
        _clock = clock;
    }

    public WorkAreaDrawSession Session { get; } = new();

    /// <summary>Bumps on every change the panel or the outline layer should
    /// show: a store edit, a selection change, a draft point.</summary>
    public long ChangeStamp { get; private set; }

    public string? SelectedId
    {
        get
        {
            if (_selectedId != null && _store.Find(_selectedId) == null)
            {
                _selectedId = null;
            }

            return _selectedId;
        }
    }

    /// <summary>Starts drawing. An empty name takes the first free
    /// "Area N"; a name the store would refuse is refused now, before the
    /// player draws anything.</summary>
    public string BeginDraw(string? typedName)
    {
        _deleteArmedId = null;
        string trimmed = (typedName ?? string.Empty).Trim();
        string name = trimmed.Length == 0 ? NextDefaultName() : trimmed;
        if (!WorkAreaStore.TryNormaliseName(name, out string clean))
        {
            return AtlasStrings.Format("areas.notStarted", WorkAreaStore.Describe(WorkAreaRefusal.BadName));
        }

        if (_store.Find(clean) != null)
        {
            return AtlasStrings.Format("areas.notStarted", WorkAreaStore.Describe(WorkAreaRefusal.DuplicateName));
        }

        if (_store.Areas.Count >= WorkAreaStore.MaxAreas)
        {
            return AtlasStrings.Format("areas.notStarted", WorkAreaStore.Describe(WorkAreaRefusal.TooMany));
        }

        Session.Begin(clean);
        ChangeStamp++;
        return AtlasStrings.Format("areas.modeDraw", clean);
    }

    public string CancelDraw()
    {
        if (!Session.Active)
        {
            return string.Empty;
        }

        Session.Cancel();
        ChangeStamp++;
        return AtlasStrings.Get("areas.drawCancelled");
    }

    /// <summary>One large-map frame while drawing. Returns the message to
    /// show, or null when nothing the player needs to read happened. A
    /// refused or over-long loop keeps the mode on under the same name, so the
    /// player simply draws again; only a refusal from the store itself (the
    /// name taken meanwhile, the world full) ends it.</summary>
    public string? HandleMapFrame(bool held, RoadPoint cursor)
    {
        WorkAreaDrawSession.Outcome outcome = Session.Observe(held, cursor, out List<RoadPoint>? stroke);
        switch (outcome)
        {
            case WorkAreaDrawSession.Outcome.Changed:
                ChangeStamp++;
                return null;
            case WorkAreaDrawSession.Outcome.Overflowed:
                // Like a refused shape, the mode stays on under the same name
                // (the session itself waits for a release first).
                ChangeStamp++;
                return AtlasStrings.Get("areas.tooLong");
            case WorkAreaDrawSession.Outcome.Completed:
                ChangeStamp++;
                return Complete(stroke!);
            default:
                return null;
        }
    }

    private string Complete(List<RoadPoint> stroke)
    {
        // The session ended with the stroke; the name it was begun under is
        // still the one the player chose.
        string name = Session.PendingName;
        WorkAreaRefusal drafted = WorkAreaDrafting.FromStroke(stroke, out WorkAreaPolygon? boundary, out WorkAreaShapeProblem problem);
        if (drafted != WorkAreaRefusal.None)
        {
            Session.Begin(name);
            string reason = drafted == WorkAreaRefusal.BadShape
                ? WorkAreaPolygon.Describe(problem)
                : WorkAreaStore.Describe(drafted);
            return AtlasStrings.Format("areas.refused", reason);
        }

        WorkAreaRefusal refusal = _store.TryCreate(
            name, WorkAreaContract.Purposes.WoodGathering, boundary!, _newId(), _clock(), out WorkArea? created);
        if (refusal != WorkAreaRefusal.None)
        {
            return AtlasStrings.Format("areas.notSaved", WorkAreaStore.Describe(refusal));
        }

        _selectedId = created!.Id;
        return AtlasStrings.Format("areas.saved", created.Name, SquareMetres(created));
    }

    public void Select(string id)
    {
        if (_store.Find(id) is { } area && string.Equals(area.Id, id, StringComparison.Ordinal))
        {
            _selectedId = area.Id;
            _deleteArmedId = null;
            ChangeStamp++;
        }
    }

    public string RenameSelected(string? newName)
    {
        _deleteArmedId = null;
        if (SelectedId is not { } id)
        {
            return AtlasStrings.Get("areas.selectFirst");
        }

        WorkAreaRefusal refusal = _store.Rename(id, newName, _clock());
        if (refusal != WorkAreaRefusal.None)
        {
            return AtlasStrings.Format("areas.notRenamed", WorkAreaStore.Describe(refusal));
        }

        ChangeStamp++;
        return AtlasStrings.Format("areas.renamed", _store.Find(id)!.Name);
    }

    /// <summary>Delete takes two clicks on the same selection; anything else
    /// in between disarms it.</summary>
    public string DeleteSelected()
    {
        if (SelectedId is not { } id)
        {
            _deleteArmedId = null;
            return AtlasStrings.Get("areas.selectFirst");
        }

        WorkArea area = _store.Find(id)!;
        if (!string.Equals(_deleteArmedId, id, StringComparison.Ordinal))
        {
            _deleteArmedId = id;
            return AtlasStrings.Format("areas.deleteArm", area.Name);
        }

        _deleteArmedId = null;
        string name = area.Name;
        _store.Delete(id);
        _selectedId = null;
        ChangeStamp++;
        return AtlasStrings.Format("areas.deleted", name);
    }

    public void Disarm() => _deleteArmedId = null;

    /// <summary>The first <paramref name="max"/> areas in store order, each
    /// with its list label, and how many more there are.</summary>
    public List<(string Id, string Label)> Rows(int max, out int hidden)
    {
        var rows = new List<(string, string)>();
        foreach (WorkArea area in _store.Areas)
        {
            if (rows.Count >= max)
            {
                break;
            }

            rows.Add((area.Id, AtlasStrings.Format("areas.row", area.Name, SquareMetres(area))));
        }

        hidden = Math.Max(0, _store.Areas.Count - rows.Count);
        return rows;
    }

    public string SelectionDetails()
    {
        if (SelectedId is not { } id || _store.Find(id) is not { } area)
        {
            return _store.Areas.Count == 0 ? AtlasStrings.Get("areas.none") : AtlasStrings.Get("areas.pick");
        }

        return AtlasStrings.Format(
            "areas.details",
            area.Name,
            SquareMetres(area),
            area.Boundary.Points.Count.ToString(CultureInfo.InvariantCulture));
    }

    private string NextDefaultName()
    {
        for (int index = 1; index <= WorkAreaStore.MaxAreas + 1; index++)
        {
            string candidate = AtlasStrings.Format("areas.defaultName", index.ToString(CultureInfo.InvariantCulture));
            if (_store.Find(candidate) == null)
            {
                return candidate;
            }
        }

        return AtlasStrings.Format("areas.defaultName", "?");
    }

    private static string SquareMetres(WorkArea area) =>
        Math.Round(area.Boundary.AreaSquareMetres).ToString("0", CultureInfo.InvariantCulture);
}

internal enum WorkAreaOutlineEmphasis
{
    Saved,
    Selected,
    Draft,
}

/// <summary>One line for the large-map outline layer, in world X/Z.</summary>
/// <summary>#296: the map badge. What a worker product says about the order
/// running in an area, if any.</summary>
internal enum WorkAreaOrderMark
{
    None,
    Working,
    NeedsAttention,
}

internal sealed class WorkAreaOutline
{
    public WorkAreaOutline(
        List<(float X, float Z)> points, bool closed, WorkAreaOutlineEmphasis emphasis, WorkAreaOrderMark mark = WorkAreaOrderMark.None)
    {
        Mark = mark;
        Points = points;
        Closed = closed;
        Emphasis = emphasis;
    }

    public IReadOnlyList<(float X, float Z)> Points { get; }

    /// <summary>A saved boundary is closed (last corner back to the first);
    /// the draft is the open line the player is still drawing.</summary>
    public bool Closed { get; }

    public WorkAreaOutlineEmphasis Emphasis { get; }

    /// <summary>#296: the order badge, shown as the outline's colour.</summary>
    public WorkAreaOrderMark Mark { get; }
}

/// <summary>#296 slice 2: what the outline layer draws. Saved areas first,
/// the selected one after them so it sits on top, the draft last. A local
/// presentation of the player's own data: nothing here is written anywhere.
/// </summary>
internal static class WorkAreaOutlines
{
    public static List<WorkAreaOutline> Collect(WorkAreaStore store, WorkAreaEditor? editor) =>
        Collect(store, editor, null);

    /// <param name="orders">#296: the latest <c>concernedcat.workorders/1</c>
    /// answer, or null when no worker product answered. Only an Ok answer
    /// marks an area.</param>
    public static List<WorkAreaOutline> Collect(WorkAreaStore store, WorkAreaEditor? editor, WorkOrderLookup? orders)
    {
        var outlines = new List<WorkAreaOutline>();
        string? selected = editor?.SelectedId;
        WorkArea? selectedArea = null;
        foreach (WorkArea area in store.Areas)
        {
            if (selected != null && string.Equals(area.Id, selected, StringComparison.Ordinal))
            {
                selectedArea = area;
                continue;
            }

            outlines.Add(new WorkAreaOutline(Corners(area), closed: true, WorkAreaOutlineEmphasis.Saved, MarkFor(orders, area.Id)));
        }

        if (selectedArea != null)
        {
            outlines.Add(new WorkAreaOutline(Corners(selectedArea), closed: true, WorkAreaOutlineEmphasis.Selected, MarkFor(orders, selectedArea.Id)));
        }

        if (editor != null && editor.Session.Active && editor.Session.Draft.Count > 0)
        {
            var draft = new List<(float X, float Z)>(editor.Session.Draft.Count);
            foreach (RoadPoint point in editor.Session.Draft)
            {
                draft.Add((point.X, point.Z));
            }

            outlines.Add(new WorkAreaOutline(draft, closed: false, WorkAreaOutlineEmphasis.Draft));
        }

        return outlines;
    }

    private static WorkAreaOrderMark MarkFor(WorkOrderLookup? orders, string areaId) => WorkAreaOrders.MarkFor(orders, areaId);

    private static List<(float X, float Z)> Corners(WorkArea area)
    {
        var corners = new List<(float X, float Z)>(area.Boundary.Points.Count);
        foreach (WorkAreaPoint point in area.Boundary.Points)
        {
            corners.Add((point.X, point.Z));
        }

        return corners;
    }
}

/// <summary>#296: what the map says about the order running in a work area,
/// from <c>concernedcat.workorders/1</c>. The words are the worker product's
/// own (its order panel's lines); nothing here invents a state, and a missing
/// or failed answer says nothing rather than "no order".</summary>
internal static class WorkAreaOrders
{
    public static WorkAreaOrderMark MarkFor(WorkOrderLookup? orders, string areaId)
    {
        PublishedWorkOrder? order = orders != null && orders.IsOk ? orders.ForArea(areaId) : null;
        return order == null
            ? WorkAreaOrderMark.None
            : order.NeedsAttention ? WorkAreaOrderMark.NeedsAttention : WorkAreaOrderMark.Working;
    }

    /// <summary>The lines for the selected area, or empty when there is
    /// nothing true to say (no worker product, or it did not answer).</summary>
    public static string Describe(WorkOrderLookup? orders, string? areaId)
    {
        if (orders == null || !orders.IsOk || string.IsNullOrEmpty(areaId))
        {
            return string.Empty;
        }

        PublishedWorkOrder? order = orders.ForArea(areaId!);
        if (order == null)
        {
            return AtlasStrings.Get("areas.noOrder");
        }

        var text = new System.Text.StringBuilder();
        text.Append(AtlasStrings.Format("areas.orderWorker", order.Worker, order.StateLine));
        foreach (string line in order.ProgressLines)
        {
            text.Append('\n').Append(line);
        }

        if (order.NeedsAttention && order.ReasonLine.Length > 0)
        {
            text.Append('\n').Append(AtlasStrings.Format("areas.orderAttention", order.ReasonLine));
        }

        return text.ToString();
    }

    /// <summary>A fingerprint of what the map shows, so the outline layer is
    /// redrawn only when it changes.</summary>
    public static string Signature(WorkOrderLookup? orders)
    {
        if (orders == null || !orders.IsOk)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        foreach (PublishedWorkOrder order in orders.Orders)
        {
            text.Append(order.AreaId).Append(order.NeedsAttention ? '!' : '.').Append('|');
        }

        return text.ToString();
    }
}
