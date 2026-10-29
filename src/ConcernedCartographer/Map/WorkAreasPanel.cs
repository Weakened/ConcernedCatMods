using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>#296 slice 2: the Work Areas side panel. Draw boundary enters a
/// visible UI-owned map mode (plain LMB, the runtime consumes map drag and
/// clicks while it is on); releasing the button saves the loop or says why
/// not and lets the player draw again; Escape, Cancel, hiding the panel or
/// closing the map ends it. The list selects by stable id. Every operation is
/// a <see cref="WorkAreaEditor"/> call, the same store the console uses.
/// Drawing or saving never starts work and names no worker; "Assign
/// Thorstein" is stated as not available rather than offered.</summary>
internal sealed class WorkAreasPanel : CcSidePanel
{
    private const int AreaSlots = 6;

    private readonly Func<WorkAreaEditor?> _editor;
    private readonly Func<bool> _outlinesVisible;
    private readonly Action _changed;
    private InputField? _name;
    private Text? _modeStatus;
    private Text? _details;
    private Text? _overflow;
    private Text? _output;
    private Text? _outlineNote;
    private readonly Button[] _rows = new Button[AreaSlots];
    private readonly Text[] _rowLabels = new Text[AreaSlots];
    private readonly string[] _rowIds = new string[AreaSlots];
    private long _renderedStamp = -1;

    /// <param name="changed">Called after anything the player did that may
    /// have changed the store or the outlines (the runtime saves and
    /// redraws).</param>
    public WorkAreasPanel(ManualLogSource log, Func<WorkAreaEditor?> editor, Func<bool> outlinesVisible, Action changed)
        : base(log, "areas.title", 384f, 520f)
    {
        _editor = editor;
        _outlinesVisible = outlinesVisible;
        _changed = changed;
    }

    /// <summary>True while the draw mode is on (the runtime's input gate).
    /// </summary>
    public bool ModeActive => _editor()?.Session.Active ?? false;

    public override void HandleFrame()
    {
        if (IsVisible && ModeActive && Input.GetKeyDown(KeyCode.Escape) &&
            !CcTextFocus.EscapeShouldOnlyBlur())
        {
            Report(_editor()?.CancelDraw() ?? string.Empty);
            _changed();
            return;
        }

        if (!IsVisible && ModeActive)
        {
            // The panel (or the map) closed mid-draw: end the mode so the
            // input gate releases and no half-drawn line is kept.
            _editor()?.CancelDraw();
            _changed();
        }

        if (IsVisible && _editor() is { } editor && editor.ChangeStamp != _renderedStamp)
        {
            _renderedStamp = editor.ChangeStamp;
            Refresh();
        }

        base.HandleFrame();
    }

    /// <summary>A message from the map frame (a saved or refused loop).
    /// </summary>
    public void Report(string message)
    {
        if (_output != null && !string.IsNullOrEmpty(message))
        {
            _output.text = message;
        }

        Refresh();
    }

    /// <summary>Adds a line under the last message (a save that failed).
    /// </summary>
    public void AppendNote(string note)
    {
        if (_output != null && !string.IsNullOrEmpty(note))
        {
            _output.text = string.IsNullOrEmpty(_output.text) ? note : _output.text + "\n" + note;
        }
    }

    protected override void OnHidden()
    {
        if (ModeActive)
        {
            _editor()?.CancelDraw();
            _changed();
        }

        _editor()?.Disarm();
        base.OnHidden();
    }

    protected override void OnShown()
    {
        _editor()?.Disarm();
        _renderedStamp = _editor()?.ChangeStamp ?? -1;
        Refresh();
    }

    protected override void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y)
    {
        Text explainer = AddBody(gui, font, AtlasStrings.Get("areas.explainer"), 11,
            new Color(0.85f, 0.82f, 0.7f, 1f), ref y, 28f);
        explainer.alignment = TextAnchor.UpperCenter;
        y -= 12f;

        GameObject nameField = gui.CreateInputField(
            Panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            InputField.ContentType.Standard, AtlasStrings.Get("areas.nameHint"), 13, Width - 44f, 28f);
        _name = nameField.GetComponent<InputField>();
        _name.characterLimit = WorkAreaStore.MaxNameLength;
        y -= 36f;

        float half = (Width - 44f) / 2f;
        float left = -(Width - 44f) / 2f;
        AddButton(gui, AtlasStrings.Get("areas.draw"), left + (half * 0.5f), y, half - 4f, 28f, () =>
        {
            Report(_editor()?.BeginDraw(_name != null ? _name.text : string.Empty) ?? string.Empty);
            _changed();
        });
        AddButton(gui, AtlasStrings.Get("areas.cancel"), left + (half * 1.5f), y, half - 4f, 28f, () =>
        {
            Report(_editor()?.CancelDraw() ?? string.Empty);
            _changed();
        });
        y -= 34f;

        _modeStatus = AddBody(gui, font, "", 12, new Color(0.85f, 1f, 0.85f, 1f), ref y, 44f);
        _details = AddBody(gui, font, "", 12, new Color(1f, 0.95f, 0.75f, 1f), ref y, 30f);
        y -= 6f;

        for (int index = 0; index < AreaSlots; index++)
        {
            int captured = index;
            GameObject row = gui.CreateButton("", Panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), Width - 44f, 24f);
            _rows[index] = row.GetComponent<Button>();
            _rowLabels[index] = row.GetComponentInChildren<Text>();
            _rows[index].onClick.AddListener(() => RowClicked(captured));
            row.SetActive(false);
            y -= 27f;
        }

        _overflow = AddBody(gui, font, "", 11, new Color(0.85f, 0.82f, 0.7f, 1f), ref y, 18f);
        y -= 6f;

        AddButton(gui, AtlasStrings.Get("areas.rename"), left + (half * 0.5f), y, half - 4f, 26f, () =>
        {
            Report(_editor()?.RenameSelected(_name != null ? _name.text : string.Empty) ?? string.Empty);
            _changed();
        });
        AddButton(gui, AtlasStrings.Get("areas.delete"), left + (half * 1.5f), y, half - 4f, 26f, () =>
        {
            Report(_editor()?.DeleteSelected() ?? string.Empty);
            _changed();
        });
        y -= 34f;

        _output = AddBody(gui, font, "", 12, Color.white, ref y, 44f);
        AddBody(gui, font, AtlasStrings.Get("areas.assignUnavailable"), 11,
            new Color(0.75f, 0.75f, 0.75f, 1f), ref y, 30f);
        _outlineNote = AddBody(gui, font, AtlasStrings.Get("areas.outlineUnavailable"), 11,
            new Color(1f, 0.8f, 0.6f, 1f), ref y, 30f);
    }

    private void RowClicked(int index)
    {
        string? id = _rowIds[index];
        if (id != null && _editor() is { } editor)
        {
            editor.Select(id);
            _changed();
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_editor() is not { } editor || _details == null)
        {
            return;
        }

        List<(string Id, string Label)> rows = editor.Rows(AreaSlots, out int hidden);
        string? selected = editor.SelectedId;
        for (int index = 0; index < AreaSlots; index++)
        {
            bool used = index < rows.Count;
            _rows[index].gameObject.SetActive(used);
            _rowIds[index] = used ? rows[index].Id : null!;
            if (used)
            {
                bool isSelected = string.Equals(rows[index].Id, selected, StringComparison.Ordinal);
                _rowLabels[index].text = (isSelected ? "» " : "") + rows[index].Label;
            }
        }

        _details.text = editor.SelectionDetails();
        if (_overflow != null)
        {
            _overflow.text = hidden > 0 ? AtlasStrings.Format("areas.more", hidden) : string.Empty;
        }

        if (_modeStatus != null)
        {
            _modeStatus.text = editor.Session.Active
                ? AtlasStrings.Format("areas.modeDraw", editor.Session.PendingName)
                : string.Empty;
        }

        if (_outlineNote != null)
        {
            _outlineNote.gameObject.SetActive(!_outlinesVisible());
        }
    }
}
