using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using UnityEngine;
using UnityEngine.UI;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Ui;

/// <summary>Thorstein's tools (#295 S4): the visible hand-over the
/// introduction asks for, so that bringing him an axe and a hammer is not a
/// console exercise.
///
/// <b>It decides nothing and authorises nothing of its own.</b> Every button
/// sends the same words the console does:
/// <list type="bullet">
/// <item>bring him over: <c>cf_worker spawn</c>;</item>
/// <item>send him home: <c>cf_worker despawn</c>;</item>
/// <item>give: <c>cf_settle give axe|hammer</c>;</item>
/// <item>take back: <c>cf_settle takeback axe|hammer</c>.</item>
/// </list>
/// So a refusal is the console's refusal with the console's reason, and a
/// tool moves only through the existing handover (#299-#301): the real item in
/// the player's hand, its quality and durability kept, into his own body.
///
/// <b>Giving is two clicks.</b> The first click names the exact item in the
/// player's hand (<c>cf_settle preview</c>) and moves nothing. Only a second
/// click on the same Give moves it. That is #295's "explicit inventory
/// selection and transfer confirmation".
///
/// It is built on the same Jotunn <c>GUIManager</c> calls as the other Foreman
/// panels, with the same contract: any exception disables this surface for the
/// session with one ERROR line, and the console keeps working.</summary>
internal sealed class ThorsteinToolsPanel : MonoBehaviour
{
    private const float PanelWidth = 420f;
    private const float PanelHeight = 430f;
    private const double RefreshSeconds = 0.5;

    private Func<bool> _enabled = () => false;
    private SettlementRuntime? _runtime;
    private ManualLogSource? _log;

    private bool _failed;
    private string? _armed;
    private double _nextRefresh;
    private GameObject? _button;
    private GameObject? _panel;
    private Text? _story;
    private Text? _held;
    private Text? _output;
    private Button? _bring;
    private Button? _home;
    private Button? _giveAxe;
    private Button? _giveHammer;
    private Button? _takeAxe;
    private Button? _takeHammer;

    /// <param name="enabled">The settlement runtime is on: the button appears
    /// only then.</param>
    internal void Initialize(Func<bool> enabled, SettlementRuntime runtime, ManualLogSource log)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _log = log;
    }

    private void Update()
    {
        if (_failed || _runtime == null)
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
        SettlementRuntime runtime = _runtime!;
        // "view", never "status": a refresh only reads (#295 S4 review).
        SetText(_story, runtime.ExecuteSettlement(new[] { "quest", "view" }));
        SetText(_held, runtime.ExecuteSettlement(new[] { "tools" }));

        ThorsteinToolsPanelButtons buttons = ThorsteinToolsPanelPlan.Decide(runtime.ThorsteinToolsPanelFacts());
        SetEnabled(_bring, buttons.BringHim);
        SetEnabled(_home, buttons.SendHome);
        SetEnabled(_giveAxe, buttons.GiveAxe);
        SetEnabled(_giveHammer, buttons.GiveHammer);
        SetEnabled(_takeAxe, buttons.TakeAxe);
        SetEnabled(_takeHammer, buttons.TakeHammer);
        _armed = ThorsteinToolsPanelPlan.KeepArmed(_armed, buttons);
    }

    private void Give(string kind)
    {
        if (!ThorsteinToolsPanelPlan.IsConfirmation(_armed, kind))
        {
            string preview = _runtime!.ExecuteSettlement(new[] { "preview", kind });
            _armed = ThorsteinToolsPanelPlan.ArmAfterPreview(
                kind, preview, Runtime.Custody.CustodyTools.PreviewNamesTool);
            Report(preview);
            return;
        }

        _armed = null;
        Report(_runtime!.ExecuteSettlement(new[] { "give", kind }));
        _nextRefresh = 0d;
    }

    private void TakeBack(string kind)
    {
        _armed = null;
        Report(_runtime!.ExecuteSettlement(new[] { "takeback", kind }));
        _nextRefresh = 0d;
    }

    private void Worker(string verb)
    {
        _armed = null;
        Report(_runtime!.Execute(new[] { verb }));
        _nextRefresh = 0d;
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
                "His tools", GUIManager.CustomGUIFront.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 210f), 96f, 32f);
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
                _armed = null;
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
        _armed = null;
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
            new Vector2((PanelWidth / 2f) + 30f, -150f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(
            "Thorstein's tools", _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), font, 19, header, outline: true, Color.black, PanelWidth - 40f, 28f,
            addContentSizeFitter: false);

        float y = -58f;
        gui.CreateText(
            "Hold the tool in your hand. Give asks first, then gives on the second click.", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), font, 13, body,
            outline: false, Color.black, PanelWidth - 40f, 22f, addContentSizeFitter: false);
        y -= 30f;

        _bring = AddButton(gui, "Bring him over", -100f, y, 180f, () => Worker("spawn"));
        _home = AddButton(gui, "Send him home", 100f, y, 180f, () => Worker("despawn"));
        y -= 32f;
        _giveAxe = AddButton(gui, "Give axe", -100f, y, 180f, () => Give("axe"));
        _giveHammer = AddButton(gui, "Give hammer", 100f, y, 180f, () => Give("hammer"));
        y -= 32f;
        _takeAxe = AddButton(gui, "Take back axe", -100f, y, 180f, () => TakeBack("axe"));
        _takeHammer = AddButton(gui, "Take back hammer", 100f, y, 180f, () => TakeBack("hammer"));
        y -= 32f;
        AddButton(gui, "Close", 0f, y, 110f, Hide);
        y -= 36f;

        _story = Row(gui, font, header, ref y, 84f);
        _held = Row(gui, font, body, ref y, 24f);
        _output = Row(gui, font, new Color(0.85f, 0.85f, 0.85f, 1f), ref y, 84f);

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
            "Thorstein's tools panel failed and was disabled for this session (cf_settle give/takeback and " +
            "cf_worker spawn/despawn still work from the console): " + SafeFailure.Brief(exception));
    }
}
