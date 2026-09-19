using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedForeman.Ui;

/// <summary>The Build Orders menu (#380): pick Shelter, mark where it goes and
/// which way it faces, see what it really costs, and confirm.
///
/// <b>It decides nothing and it authorises nothing of its own.</b> Every button
/// is one word sent to <see cref="BuildOrderRuntime"/> - the same words
/// <c>cf_build</c> sends - so an order confirmed here and one confirmed from the
/// console are the same order, priced the same way, authorised by the same act.
/// A panel that could authorise something the console could not would be a
/// second authority, and #280 has exactly one.
///
/// <b>Confirm is deliberately two clicks.</b> It is the moment a player agrees
/// to a real amount of their own material leaving a chest, and the amount is on
/// screen in front of them when they agree to it. The first click shows the
/// price; the second is the authorisation.
///
/// Built on the same Jotunn <c>GUIManager</c> calls Thorstein's collection panel
/// uses, with the same contract: any exception disables this surface for the
/// session with one ERROR line, and build orders still work from the
/// console.</summary>
internal sealed class BuildOrderPanel : MonoBehaviour
{
    private const float PanelWidth = 420f;
    private const float PanelHeight = 300f;
    private const double RefreshSeconds = 0.5;

    private Func<bool> _enabled = () => false;
    private BuildOrderRuntime? _orders;
    private ManualLogSource? _log;

    private bool _failed;
    private bool _confirmArmed;
    private double _nextRefresh;
    private GameObject? _button;
    private GameObject? _panel;
    private Text? _state;
    private Text? _cost;
    private Text? _progress;
    private Text? _output;
    private Button? _confirm;
    private Button? _cancel;
    private Button? _turn;

    /// <param name="enabled">The settlement runtime is on: the button appears
    /// only then, so a player who installed Foreman for the diagnostics never
    /// sees it.</param>
    internal void Initialize(Func<bool> enabled, BuildOrderRuntime orders, ManualLogSource log)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _log = log;
    }

    private void Update()
    {
        if (_failed || _orders == null)
        {
            return;
        }

        try
        {
            bool show = _enabled() && Player.m_localPlayer != null && GUIManager.Instance != null &&
                GUIManager.CustomGUIFront != null;
            EnsureButton(show);
            if (!show)
            {
                Hide();
                return;
            }

            if (_panel == null || !_panel.activeSelf || Time.timeAsDouble < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.timeAsDouble + RefreshSeconds;
            Refresh();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Refresh()
    {
        BuildOrderRuntime orders = _orders!;
        SetText(_state, orders.Execute(new[] { "status" }));

        ShelterPlan plan = orders.Plan();
        SetText(
            _cost,
            plan.IsPlanned
                ? "It costs " + plan.Total.Describe() + " for " + plan.Pieces.Count + " pieces."
                : string.Empty);

        SetText(
            _progress,
            orders.IsAuthorised ? ConstructionSentences.Working(orders.Progress()) : string.Empty);

        bool marked = orders.Status != BuildOrderStatus.None;
        SetEnabled(_confirm, marked && !orders.IsAuthorised);
        SetEnabled(_cancel, marked);
        SetEnabled(_turn, marked && !orders.IsAuthorised);
        if (orders.IsAuthorised)
        {
            _confirmArmed = false;
        }
    }

    private void Mark()
    {
        _confirmArmed = false;
        Report(_orders!.Execute(new[] { "here" }));
        Refresh();
    }

    private void Turn()
    {
        Report(_orders!.Execute(new[] { "turn", "90" }));
        Refresh();
    }

    private void Confirm()
    {
        if (!_confirmArmed)
        {
            _confirmArmed = true;
            Report(
                _orders!.Execute(new[] { "preview" }) +
                " Click Confirm again to authorise it. Nothing leaves a container before you do.");
            return;
        }

        _confirmArmed = false;
        Report(_orders!.Execute(new[] { "confirm" }));
        Refresh();
    }

    private void Cancel()
    {
        _confirmArmed = false;
        Report(_orders!.Execute(new[] { "cancel" }));
        Refresh();
    }

    private void Report(string message) => SetText(_output, message ?? string.Empty);

    private void EnsureButton(bool show)
    {
        if (_button == null)
        {
            if (!show)
            {
                return;
            }

            _button = GUIManager.Instance.CreateButton(
                "Build orders", GUIManager.CustomGUIFront.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 130f), 96f, 32f);
            _button.GetComponent<Button>().onClick.AddListener(Toggle);
        }

        if (_button.activeSelf != show)
        {
            _button.SetActive(show);
        }
    }

    private void Toggle()
    {
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
                _confirmArmed = false;
                _nextRefresh = 0d;
                Report(string.Empty);
                Refresh();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Hide()
    {
        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
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
        var header = new Color(0.9f, 0.8f, 0.6f, 1f);
        Color body = Color.white;

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2((PanelWidth / 2f) + 30f, -200f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(
            "Build orders", _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), font, 19, header, outline: true, Color.black, PanelWidth - 40f, 28f,
            addContentSizeFitter: false);

        float y = -58f;
        gui.CreateText(
            "Shelter - floor, walls, a doorway, a roof and a bed.", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), font, 14, body,
            outline: false, Color.black, PanelWidth - 40f, 22f, addContentSizeFitter: false);
        y -= 30f;

        AddButton(gui, "Mark it here", -100f, y, 180f, Mark);
        _turn = AddButton(gui, "Turn 90", 100f, y, 180f, Turn);
        y -= 32f;
        _confirm = AddButton(gui, "Confirm", -130f, y, 110f, Confirm);
        _cancel = AddButton(gui, "Cancel", 0f, y, 110f, Cancel);
        AddButton(gui, "Close", 130f, y, 110f, Hide);
        y -= 36f;

        _state = Row(gui, font, header, ref y, 40f);
        _cost = Row(gui, font, body, ref y, 24f);
        _progress = Row(gui, font, body, ref y, 40f);
        _output = Row(gui, font, new Color(0.85f, 0.85f, 0.85f, 1f), ref y, 56f);

        _panel.SetActive(false);
        return true;
    }

    private Button? AddButton(GUIManager gui, string label, float x, float y, float width, Action onClick)
    {
        GameObject button = gui.CreateButton(
            label, _panel!.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y),
            width, 28f);
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

    private Text Row(GUIManager gui, Font font, Color colour, ref float y, float height)
    {
        Text text = gui.CreateText(
            string.Empty, _panel!.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y - (height / 2f)), font, 13, colour, outline: false, Color.black,
            PanelWidth - 40f, height, addContentSizeFitter: false).GetComponent<Text>();
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        y -= height + 4f;
        return text;
    }

    private static void SetText(Text? text, string value)
    {
        if (text != null && text.text != value)
        {
            text.text = value;
        }
    }

    private static void SetEnabled(Button? button, bool enabled)
    {
        if (button != null && button.interactable != enabled)
        {
            button.interactable = enabled;
        }
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        try
        {
            Hide();
            if (_button != null)
            {
                _button.SetActive(false);
            }
        }
        catch (Exception)
        {
            // The surface is already disabled.
        }

        _log?.LogError(
            "The build order panel failed and was disabled for this session (build orders still work " +
            "from the console with cf_build): " + exception.GetType().Name + ": " + exception.Message);
    }
}
