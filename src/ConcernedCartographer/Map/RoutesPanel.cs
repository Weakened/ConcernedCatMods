using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Routes side panel (#101): every shipped route feature as
/// real UI. Free Draw / Waypoints / Erase enter explicit visible map
/// modes (no modifier key needed once entered — the runtime consumes map
/// drag/clicks while a UI-owned mode is active), Finish/Escape end them,
/// and the route list selects by stable AtlasId so operations (rename,
/// style, status, ink color, lock, archive, delete/restore, split, merge,
/// measure, undo/redo) never depend on player proximity. The console
/// (`cc_routes`) remains a scriptable alias over the same operations.</summary>
internal sealed class RoutesPanel : CcSidePanel
{
    private const int RouteSlots = 6;

    // Curated route ink swatches (ARGB), plus a clear slot in the UI.
    private static readonly int[] Swatches =
    {
        unchecked((int)0xFFE8D058), // gold
        unchecked((int)0xFFD05840), // ember
        unchecked((int)0xFF58A8E8), // sky
        unchecked((int)0xFF68C868), // moss
        unchecked((int)0xFFC878E0), // thistle
        unchecked((int)0xFFF0F0F0), // chalk
    };

    private readonly Func<Runtime.RouteCommandHandler?> _handler;
    private InputField? _name;
    private Toggle? _snap;
    private Text? _modeStatus;
    private Text? _selectionStatus;
    private Text? _output;
    private readonly Button[] _routeButtons = new Button[RouteSlots];
    private readonly Text[] _routeLabels = new Text[RouteSlots];
    private readonly AtlasId[] _routeIds = new AtlasId[RouteSlots];
    private AtlasId _selected;
    private bool _hasSelection;
    private bool _mergeArmed;

    public RoutesPanel(ManualLogSource log, Func<Runtime.RouteCommandHandler?> handler)
        : base(log, "routes.title", 384f, 648f)
    {
        _handler = handler;
    }

    /// <summary>True while a UI-entered map mode is active (runtime input gate).</summary>
    public bool ModeActive
    {
        get
        {
            Runtime.RouteCommandHandler? handler = _handler();
            return handler is not null && handler.UiModeOwned &&
                handler.Mode != Runtime.RouteCommandHandler.MapMode.None;
        }
    }

    public override void HandleFrame()
    {
        // Escape first ends an active draw mode; only then does it close
        // the panel (base behavior). Typing in a field outranks both.
        if (IsVisible && ModeActive && Input.GetKeyDown(KeyCode.Escape) &&
            !CcTextFocus.EscapeShouldOnlyBlur())
        {
            _handler()?.UiStop();
            RefreshMode();
            return;
        }

        if (!IsVisible && ModeActive && !Minimap.IsOpen())
        {
            // The map (and panel) closed mid-mode; end the mode so the
            // input gate releases.
            _handler()?.UiStop();
        }

        base.HandleFrame();
    }

    /// <summary>A hidden panel must never leave a UI-owned mode running
    /// (#101 "explicit visible modes"): whatever hides the panel — Escape,
    /// another toolbar surface opening exclusively, map close — the mode
    /// ends with it, releasing map drag and the click gate.</summary>
    protected override void OnHidden()
    {
        if (ModeActive)
        {
            _handler()?.UiStop();
            RefreshMode();
        }

        base.OnHidden();
    }

    protected override void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y)
    {
        // v1 product framing (RC10 feedback 16): routes are manual map
        // planning/navigation overlays, never character automation.
        Text explainer = AddBody(gui, font, AtlasStrings.Get("routes.explainer"), 11,
            new Color(0.85f, 0.82f, 0.7f, 1f), ref y, 28f);
        explainer.alignment = TextAnchor.UpperCenter;

        GameObject nameField = gui.CreateInputField(
            Panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            InputField.ContentType.Standard, AtlasStrings.Get("routes.nameHint"), 13, Width - 44f, 28f);
        _name = nameField.GetComponent<InputField>();
        y -= 36f;

        float third = (Width - 44f) / 3f;
        float left = -(Width - 44f) / 2f;
        AddButton(gui, AtlasStrings.Get("routes.freeDraw"), left + (third * 0.5f), y, third - 4f, 28f,
            () => StartMode(RouteKind.Freehand));
        AddButton(gui, AtlasStrings.Get("routes.waypoints"), left + (third * 1.5f), y, third - 4f, 28f,
            () => StartMode(RouteKind.Waypoint));
        AddButton(gui, AtlasStrings.Get("routes.erase"), left + (third * 2.5f), y, third - 4f, 28f, () =>
        {
            _handler()?.UiStartErase();
            RefreshMode();
        });
        y -= 34f;

        AddButton(gui, AtlasStrings.Get("routes.finish"), left + 45f, y, 90f, 28f, () =>
        {
            _handler()?.UiStop();
            RefreshMode();
            RefreshList();
        });
        AddButton(gui, "Undo", left + 45f + 94f, y, 70f, 28f, () =>
        {
            if (_handler() is { } handler)
            {
                handler.UiUndo(out string summary);
                Report(summary);
                RefreshList();
            }
        });
        AddButton(gui, "Redo", left + 45f + 94f + 74f, y, 70f, 28f, () =>
        {
            if (_handler() is { } handler)
            {
                handler.UiRedo(out string summary);
                Report(summary);
                RefreshList();
            }
        });
        _snap = AddToggle(gui, font, Color.white, AtlasStrings.Get("routes.snap"), left + 258f, y, value =>
            _handler()?.UiSetSnap(value));
        y -= 34f;

        _modeStatus = AddBody(gui, font, "", 12, new Color(0.85f, 1f, 0.85f, 1f), ref y, 32f);
        _selectionStatus = AddBody(gui, font, "", 12, new Color(1f, 0.95f, 0.75f, 1f), ref y, 20f);

        for (int index = 0; index < RouteSlots; index++)
        {
            int captured = index;
            GameObject row = gui.CreateButton("", Panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), Width - 44f, 24f);
            _routeButtons[index] = row.GetComponent<Button>();
            _routeLabels[index] = row.GetComponentInChildren<Text>();
            _routeButtons[index].onClick.AddListener(() => RowClicked(captured));
            row.SetActive(false);
            y -= 27f;
        }

        y -= 6f;
        float fifth = (Width - 44f) / 5f;
        string[] opRow1 = { "Rename", "Style", "Status", "Lock", "Archive" };
        string[] opRow2 = { "Delete", "Restore", "Split", "Merge", "Measure" };
        for (int index = 0; index < 5; index++)
        {
            string op1 = opRow1[index];
            string op2 = opRow2[index];
            AddButton(gui, op1, left + (fifth * (index + 0.5f)), y, fifth - 4f, 26f, () => Operate(op1));
            AddButton(gui, op2, left + (fifth * (index + 0.5f)), y - 30f, fifth - 4f, 26f, () => Operate(op2));
        }

        y -= 64f;

        float swatchStep = (Width - 44f) / (Swatches.Length + 1);
        for (int index = 0; index < Swatches.Length; index++)
        {
            int argb = Swatches[index];
            Button swatch = AddButton(gui, "", left + (swatchStep * (index + 0.5f)), y, swatchStep - 6f, 22f, () =>
            {
                if (_hasSelection && _handler() is { } handler)
                {
                    Report(handler.UiSetColor(_selected, argb));
                }
            });
            var image = swatch.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color32(
                    (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF), 255);
            }
        }

        AddButton(gui, "✕", left + (swatchStep * (Swatches.Length + 0.5f)), y, swatchStep - 6f, 22f, () =>
        {
            if (_hasSelection && _handler() is { } handler)
            {
                Report(handler.UiSetColor(_selected, null));
            }
        });
        y -= 30f;

        _output = AddBody(gui, font, "", 12, Color.white, ref y, 46f);
    }

    protected override void OnShown()
    {
        if (_snap != null && _handler() is { } handler)
        {
            SetToggleSilently(_snap, handler.SnapEnabled);
        }

        _mergeArmed = false;
        RefreshList();
        RefreshMode();
    }

    private void StartMode(RouteKind kind)
    {
        if (_handler() is not { } handler)
        {
            return;
        }

        string name = handler.UiStart(kind, _name != null ? _name.text : "");
        Report(AtlasStrings.Format(kind == RouteKind.Freehand ? "routes.modeDraw" : "routes.modeWaypoint", name));
        RefreshMode();
        RefreshList();
    }

    private void RowClicked(int index)
    {
        if (_handler() is not { } handler)
        {
            return;
        }

        AtlasId clicked = _routeIds[index];
        if (_mergeArmed && _hasSelection && !clicked.Equals(_selected))
        {
            _mergeArmed = false;
            Report(handler.UiMerge(_selected, clicked));
            RefreshList();
            return;
        }

        _selected = clicked;
        _hasSelection = true;
        _mergeArmed = false;
        RefreshList();
    }

    private void Operate(string op)
    {
        if (_handler() is not { } handler)
        {
            return;
        }

        if (op == "Restore")
        {
            Report(handler.UiRestoreLatest());
            RefreshList();
            return;
        }

        if (!_hasSelection)
        {
            Report("Select a route in the list first.");
            return;
        }

        switch (op)
        {
            case "Rename":
                Report(handler.UiRename(_selected, _name != null ? _name.text : ""));
                break;
            case "Style":
                Report(handler.UiCycleStyle(_selected));
                break;
            case "Status":
                Report(handler.UiCycleStatus(_selected));
                break;
            case "Lock":
                Report(handler.UiToggleLock(_selected));
                break;
            case "Archive":
                Report(handler.UiToggleArchive(_selected));
                break;
            case "Delete":
                Report(handler.UiDelete(_selected));
                _hasSelection = false;
                break;
            case "Split":
                Report(handler.UiSplit(_selected));
                break;
            case "Merge":
                _mergeArmed = true;
                if (_handler()!.UiListRoutes(RouteSlots).Count < 2)
                {
                    _mergeArmed = false;
                    Report("Need a second route to merge.");
                    return;
                }

                Report(AtlasStrings.Format("routes.mergePick", RouteName(_selected)));
                return;
            case "Measure":
                Report(handler.UiMeasure(_selected));
                break;
        }

        RefreshList();
    }

    private string RouteName(AtlasId id)
    {
        foreach ((AtlasId rowId, string label) in _handler()?.UiListRoutes(RouteSlots) ?? new List<(AtlasId, string)>())
        {
            if (rowId.Equals(id))
            {
                int bracket = label.IndexOf(" [", StringComparison.Ordinal);
                return bracket > 0 ? label.Substring(0, bracket) : label;
            }
        }

        return "route";
    }

    private void RefreshList()
    {
        List<(AtlasId Id, string Label)> rows =
            _handler()?.UiListRoutes(RouteSlots) ?? new List<(AtlasId, string)>();
        bool selectionSeen = false;
        for (int index = 0; index < RouteSlots; index++)
        {
            bool used = index < rows.Count;
            _routeButtons[index].gameObject.SetActive(used);
            if (!used)
            {
                continue;
            }

            _routeIds[index] = rows[index].Id;
            bool selected = _hasSelection && rows[index].Id.Equals(_selected);
            selectionSeen |= selected;
            _routeLabels[index].text = (selected ? "» " : "") + Truncate(rows[index].Label, 44);
        }

        if (_hasSelection && !selectionSeen)
        {
            _hasSelection = false;
        }

        if (_selectionStatus != null)
        {
            _selectionStatus.text = _hasSelection
                ? $"Selected: {RouteName(_selected)} — Rename/Style/Status/ink apply to it"
                : "No route selected — click a route below to select it";
        }
    }

    private void RefreshMode()
    {
        if (_modeStatus == null || _handler() is not { } handler)
        {
            return;
        }

        _modeStatus.text = handler.Mode switch
        {
            Runtime.RouteCommandHandler.MapMode.Draw =>
                AtlasStrings.Format("routes.modeDraw", handler.ActiveRouteDisplayName),
            Runtime.RouteCommandHandler.MapMode.Waypoint =>
                AtlasStrings.Format("routes.modeWaypoint", handler.ActiveRouteDisplayName),
            Runtime.RouteCommandHandler.MapMode.Erase => AtlasStrings.Get("routes.modeErase"),
            _ => "",
        };
    }

    private void Report(string message)
    {
        if (_output != null)
        {
            _output.text = message;
        }

        RefreshMode();
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
