using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;
using UnityEngine;
using UnityEngine.UI;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedTeamster.Ui.Hauling;

/// <summary>Gunnar's cart panel (#317, COOP-04): assign the cart you are
/// looking at - with a confirmation that names it - choose where the load goes,
/// send him, stop him, have him detach and park, or release the cart. It also
/// shows what he is doing and the one reason he stopped.
///
/// The panel decides nothing: every line and every button state comes from the
/// pure <see cref="HaulPanelPresenter"/>, every word from the localization
/// catalog, and every action is the haul runtime's own command, the same one
/// the console calls. Fail-closed like every Teamster surface: one exception
/// disables it for the session with one ERROR line, and hauling itself keeps
/// working.</summary>
internal sealed class GunnarHaulPanel
{
    private const float PanelWidth = 380f;
    private const float PanelHeight = 420f;
    private const float RowHeight = 24f;
    private const int LineRows = 8;
    private const double RefreshSeconds = 0.25;

    private readonly ManualLogSource _log;
    private readonly Func<float> _uiScale;
    private readonly Func<HaulPanelFacts> _facts;
    private readonly Func<HaulPanelCommand, string> _haul;

    private bool _failed;
    private GameObject? _panel;
    private Text[] _lines = Array.Empty<Text>();
    private Text? _output;
    private Button? _assign;
    private Button? _release;
    private Button? _destination;
    private Button? _start;
    private Button? _stop;
    private Button? _detach;
    private bool _assignArmed;
    private bool _releaseArmed;
    private double _nextRefresh;

    /// <param name="facts">Read fresh from Gunnar's runtime each refresh.</param>
    /// <param name="haul">Carries out one panel command and answers in the
    /// player's words.</param>
    internal GunnarHaulPanel(ManualLogSource log, Func<float> uiScale, Func<HaulPanelFacts> facts, Func<HaulPanelCommand, string> haul)
    {
        _log = log;
        _uiScale = uiScale;
        _facts = facts;
        _haul = haul;
    }

    internal bool IsVisible => _panel != null && _panel.activeSelf;

    internal void Toggle()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_panel == null && !Build())
            {
                return;
            }

            bool show = !_panel!.activeSelf;
            _panel.SetActive(show);
            if (show)
            {
                _assignArmed = false;
                _releaseArmed = false;
                _nextRefresh = 0d;
                Report(_haul(HaulPanelCommand.Status));
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    internal void Hide()
    {
        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
        }
    }

    internal void HandleFrame(double nowSeconds)
    {
        if (_failed || !IsVisible)
        {
            return;
        }

        try
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }

            if (nowSeconds < _nextRefresh)
            {
                return;
            }

            _nextRefresh = nowSeconds + RefreshSeconds;
            Render(HaulPanelPresenter.Present(_facts()));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Render(HaulPanelView view)
    {
        for (int index = 0; index < _lines.Length; index++)
        {
            Text row = _lines[index];
            if (row == null)
            {
                continue;
            }

            if (index >= view.Lines.Count)
            {
                row.text = string.Empty;
                continue;
            }

            HaulPanelLine line = view.Lines[index];
            var arguments = new object[line.Arguments.Length];
            for (int argument = 0; argument < line.Arguments.Length; argument++)
            {
                // A phase or reason arrives as its own key; anything else is
                // already a person's words (a cart's description, a sentence).
                string value = line.Arguments[argument];
                arguments[argument] = TeamsterStrings.HasKey(value) ? TeamsterStrings.Get(value) : value;
            }

            row.text = arguments.Length == 0 ? TeamsterStrings.Get(line.Key) : TeamsterStrings.Format(line.Key, arguments);
        }

        SetEnabled(_assign, view.CanAssign);
        SetEnabled(_release, view.CanRelease);
        SetEnabled(_destination, view.CanSetDestination);
        SetEnabled(_start, view.CanStart);
        SetEnabled(_stop, view.CanStop);
        SetEnabled(_detach, view.CanDetach);
        if (!view.CanAssign)
        {
            _assignArmed = false;
            SetLabel(_assign, TeamsterStrings.Get(HaulPanelKeys.Assign));
        }

        if (!view.CanRelease)
        {
            _releaseArmed = false;
            SetLabel(_release, TeamsterStrings.Get(HaulPanelKeys.Release));
        }
    }

    /// <summary>Assigning names the cart first and asks again: a cart is never
    /// taken over by one click, and never by being nearest (CART-01).</summary>
    private void Assign()
    {
        if (!_assignArmed)
        {
            _assignArmed = true;
            SetLabel(_assign, TeamsterStrings.Get(HaulPanelKeys.Confirm));
            Report(_haul(HaulPanelCommand.Assign));
            return;
        }

        _assignArmed = false;
        SetLabel(_assign, TeamsterStrings.Get(HaulPanelKeys.Assign));
        Report(_haul(HaulPanelCommand.Confirm));
    }

    private void Release()
    {
        if (!_releaseArmed)
        {
            _releaseArmed = true;
            SetLabel(_release, TeamsterStrings.Get(HaulPanelKeys.ReleaseConfirm));
            return;
        }

        _releaseArmed = false;
        SetLabel(_release, TeamsterStrings.Get(HaulPanelKeys.Release));
        Report(_haul(HaulPanelCommand.Release));
    }

    private void Report(string message)
    {
        if (_output != null)
        {
            _output.text = message ?? string.Empty;
        }
    }

    private bool Build()
    {
        GUIManager gui = GUIManager.Instance;
        if (gui == null || GUIManager.CustomGUIFront == null)
        {
            return false;
        }

        Font font = gui.AveriaSerifBold;
        Color header = PanelStyle.Header;
        Color body = PanelStyle.Body;

        _panel = PanelStyle.CreateScaledWoodpanel(
            gui, GUIManager.CustomGUIFront.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-(PanelWidth / 2f) - 380f, 0f), PanelWidth, PanelHeight, _uiScale());

        gui.CreateText(
            TeamsterStrings.Get(HaulPanelKeys.Title), _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), font, 19, header, outline: true, Color.black, PanelWidth - 40f, 28f,
            addContentSizeFitter: false);

        float y = -58f;
        var rows = new List<Text>();
        for (int index = 0; index < LineRows; index++)
        {
            Text row = gui.CreateText(
                string.Empty, _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y - (RowHeight / 2f)), font, 14, index == 1 ? header : body, outline: true, Color.black,
                PanelWidth - 40f, RowHeight + 6f, addContentSizeFitter: false).GetComponent<Text>();
            row.alignment = TextAnchor.UpperLeft;
            row.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.verticalOverflow = VerticalWrapMode.Truncate;
            rows.Add(row);
            y -= RowHeight + 6f;
        }

        _lines = rows.ToArray();
        y -= 6f;

        _assign = AddButton(gui, HaulPanelKeys.Assign, -90f, y, 170f, Assign);
        _release = AddButton(gui, HaulPanelKeys.Release, 90f, y, 170f, Release);
        y -= 32f;
        _destination = AddButton(gui, HaulPanelKeys.Here, -90f, y, 170f, () => Report(_haul(HaulPanelCommand.SetDestination)));
        _start = AddButton(gui, HaulPanelKeys.Start, 90f, y, 170f, () => Report(_haul(HaulPanelCommand.Go)));
        y -= 32f;
        _stop = AddButton(gui, HaulPanelKeys.Stop, -90f, y, 170f, () => Report(_haul(HaulPanelCommand.Stop)));
        _detach = AddButton(gui, HaulPanelKeys.Detach, 90f, y, 170f, () => Report(_haul(HaulPanelCommand.Detach)));
        y -= 36f;

        _output = gui.CreateText(
            string.Empty, _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 30f),
            font, 13, body, outline: false, Color.black, PanelWidth - 40f, 60f, addContentSizeFitter: false)
            .GetComponent<Text>();
        _output.alignment = TextAnchor.UpperLeft;
        _output.horizontalOverflow = HorizontalWrapMode.Wrap;
        _output.verticalOverflow = VerticalWrapMode.Truncate;

        GameObject close = gui.CreateButton(
            TeamsterStrings.Get(HaulPanelKeys.Close), _panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 24f), 110f, 28f);
        close.GetComponent<Button>().onClick.AddListener(Hide);

        _panel.SetActive(false);
        return true;
    }

    private Button? AddButton(GUIManager gui, string labelKey, float x, float y, float width, Action onClick)
    {
        GameObject button = gui.CreateButton(
            TeamsterStrings.Get(labelKey), _panel!.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(x, y), width, 28f);
        Button? component = button.GetComponent<Button>();
        component?.onClick.AddListener(() =>
        {
            try
            {
                onClick();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        });

        return component;
    }

    private static void SetEnabled(Button? button, bool enabled)
    {
        if (button != null && button.interactable != enabled)
        {
            button.interactable = enabled;
        }
    }

    private static void SetLabel(Button? button, string label)
    {
        Text? text = button == null ? null : button.GetComponentInChildren<Text>();
        if (text != null && text.text != label)
        {
            text.text = label;
        }
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        try
        {
            Hide();
        }
        catch (Exception)
        {
            // Already disabled.
        }

        _log.LogError(
            "Gunnar's panel failed and was disabled for this session (hauling keeps working, and the console " +
            "commands remain): " + SafeFailure.Brief(exception));
    }
}
