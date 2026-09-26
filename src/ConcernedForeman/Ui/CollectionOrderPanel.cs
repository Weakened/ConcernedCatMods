using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.Settlement.Collection.Cooperation;
using TheConcernedCat.Workers;
using UnityEngine;
using UnityEngine.UI;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedForeman.Ui;

/// <summary>Thorstein's collection order, on screen (COOP-04): the amounts, the
/// area, the chest, who is working, survey and start, pause, resume, cancel and
/// releasing the cart - and honest progress per resource.
///
/// It owns no state of its own beyond what the player is typing. Every button
/// calls the collection runtime's own command surface (the same one the console
/// uses), and every line comes from the pure
/// <see cref="OrderPanelPresenter"/>, so what a player is told is tested without
/// the game. Built on the same Jötunn GUIManager calls the other products'
/// panels use, with the same contract: any exception disables this surface for
/// the session with one ERROR line and nothing else stops working.</summary>
internal sealed class CollectionOrderPanel : MonoBehaviour
{
    private const float PanelWidth = 420f;
    private const float PanelHeight = 520f;
    private const float RowHeight = 22f;
    private const int ProgressRows = 3;
    private const double RefreshSeconds = 0.25;

    private Func<bool> _enabled = () => false;
    private Func<string[], string> _collect = _ => string.Empty;
    private Func<OrderPanelFacts>? _facts;
    private Action? _pauseCooperation;
    private Action? _releaseCart;
    private ManualLogSource? _log;
    private readonly AttentionThrottle _notices = new AttentionThrottle(30f);

    private bool _failed;
    private GameObject? _button;
    private GameObject? _panel;
    private InputField? _stone;
    private InputField? _wood;
    private Toggle? _hold;
    private Toggle? _withGunnar;
    private Text? _state;
    private Text? _reason;
    private Text? _area;
    private Text? _destination;
    private Text? _participants;
    private Text[] _progress = Array.Empty<Text>();
    private Text? _output;
    private Button? _start;
    private Button? _pause;
    private Button? _resume;
    private Button? _cancel;
    private Button? _release;
    private bool _cancelArmed;
    private double _nextRefresh;

    /// <param name="enabled">The settlement runtime is on: the button appears
    /// only then, so a player who only wants the diagnostics never sees it.</param>
    /// <param name="collect">The collection runtime's command surface
    /// (<c>status</c>, <c>preview</c>, <c>start</c>, <c>pause</c>,
    /// <c>resume</c>, <c>cancel</c>).</param>
    /// <param name="facts">Everything the presenter needs, read fresh.</param>
    /// <param name="pauseCooperation">Stops Gunnar where he is when the player
    /// pauses; null without the cooperative delivery.</param>
    /// <param name="releaseCart">Ends this order's haul and has Gunnar park the
    /// cart; null without the cooperative delivery.</param>
    internal void Initialize(
        Func<bool> enabled,
        Func<string[], string> collect,
        Func<OrderPanelFacts> facts,
        Action? pauseCooperation,
        Action? releaseCart,
        ManualLogSource log)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _collect = collect ?? throw new ArgumentNullException(nameof(collect));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _pauseCooperation = pauseCooperation;
        _releaseCart = releaseCart;
        _log = log;
    }

    private bool IsVisible => _panel != null && _panel.activeSelf;

    private void Update()
    {
        if (_failed || _facts == null)
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

            if (!IsVisible)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            if (now < _nextRefresh)
            {
                return;
            }

            _nextRefresh = now + RefreshSeconds;
            Render(OrderPanelPresenter.Present(_facts(), _notices, Time.time));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Render(OrderPanelView view)
    {
        SetText(_state, view.StateLine);
        SetText(_reason, view.ReasonLine);
        SetText(_area, view.AreaLine);
        SetText(_destination, view.DestinationLine);
        SetText(_participants, view.ParticipantsLine);
        for (int index = 0; index < _progress.Length; index++)
        {
            SetText(_progress[index], index < view.ProgressLines.Count ? view.ProgressLines[index] : string.Empty);
        }

        SetEnabled(_start, view.CanStart);
        SetEnabled(_pause, view.CanPause);
        SetEnabled(_resume, view.CanResume);
        SetEnabled(_cancel, view.CanCancel);
        SetEnabled(_release, view.CanReleaseCart && _releaseCart != null);
        if (!view.CanCancel)
        {
            _cancelArmed = false;
        }

        if (view.Notice != null)
        {
            Report(view.Notice);
            _log?.LogInfo("Collection order: " + view.Notice);
        }
    }

    private void Start()
    {
        var arguments = new List<string> { "start", Amount(_stone), Amount(_wood) };
        if (_hold != null && _hold.isOn)
        {
            arguments.Add("hold");
        }

        if (_withGunnar != null && _withGunnar.isOn)
        {
            // Asks the collection runtime for a cooperative order; it refuses
            // the word if it cannot give one, and the panel then shows Solo.
            arguments.Add("gunnar");
        }

        Report(_collect(arguments.ToArray()));
    }

    private void Pause()
    {
        Report(_collect(new[] { "pause" }));
        _pauseCooperation?.Invoke();
    }

    private void Cancel()
    {
        if (!_cancelArmed)
        {
            _cancelArmed = true;
            Report(
                "Cancelling is not a refund: what is delivered stays delivered, what is in the cart stays in the cart, " +
                "and what Thorstein carries stays with him. Click Cancel again to confirm.");
            return;
        }

        _cancelArmed = false;
        Report(_collect(new[] { "cancel" }));
    }

    private void ReleaseCart()
    {
        _releaseCart?.Invoke();
        Report("Gunnar was asked to stop, detach and park the cart. The cart stays assigned to him.");
    }

    private static string Amount(InputField? field)
    {
        string text = field != null ? field.text : string.Empty;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value.ToString(CultureInfo.InvariantCulture)
            : "0";
    }

    private void Report(string message)
    {
        if (_output != null)
        {
            _output.text = message ?? string.Empty;
        }
    }

    private void EnsureButton(bool show)
    {
        if (_button == null)
        {
            if (!show)
            {
                return;
            }

            _button = GUIManager.Instance.CreateButton(
                "Thorstein", GUIManager.CustomGUIFront.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 170f), 96f, 32f);
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
                _cancelArmed = false;
                _nextRefresh = 0d;
                Report(_collect(new[] { "status" }));
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
            new Vector2((PanelWidth / 2f) + 30f, 0f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(
            "Thorstein's collection order", _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), font, 19, header, outline: true, Color.black, PanelWidth - 40f, 28f,
            addContentSizeFitter: false);

        float y = -60f;
        Label(gui, font, header, "Stone", -140f, y);
        _stone = Field(gui, -60f, y, "20");
        Label(gui, font, header, "Wood", 30f, y);
        _wood = Field(gui, 110f, y, "30");
        y -= 34f;

        _hold = Check(gui, font, body, "Hold it for me instead of a chest", -170f, y);
        y -= 26f;
        _withGunnar = Check(gui, font, body, "With Gunnar and his cart", -170f, y);
        y -= 30f;

        _start = AddButton(gui, "Survey and start", -100f, y, 180f, Start);
        AddButton(gui, "Preview area", 100f, y, 180f, () => Report(_collect(new[] { "preview" })));
        y -= 32f;
        _pause = AddButton(gui, "Pause", -130f, y, 110f, Pause);
        _resume = AddButton(gui, "Resume", 0f, y, 110f, () => Report(_collect(new[] { "resume" })));
        _cancel = AddButton(gui, "Cancel", 130f, y, 110f, Cancel);
        y -= 32f;
        _release = AddButton(gui, "Release the cart", -100f, y, 180f, ReleaseCart);
        AddButton(gui, "Close", 100f, y, 180f, Hide);
        y -= 36f;

        _state = Row(gui, font, header, ref y, RowHeight + 6f);
        _reason = Row(gui, font, new Color(1f, 0.85f, 0.6f, 1f), ref y, RowHeight + 12f);
        _participants = Row(gui, font, body, ref y, RowHeight);
        _area = Row(gui, font, body, ref y, RowHeight + 8f);
        _destination = Row(gui, font, body, ref y, RowHeight + 8f);
        var progress = new List<Text>();
        for (int index = 0; index < ProgressRows; index++)
        {
            progress.Add(Row(gui, font, body, ref y, RowHeight + 8f));
        }

        _progress = progress.ToArray();
        _output = Row(gui, font, new Color(0.85f, 0.85f, 0.85f, 1f), ref y, 70f);

        _panel.SetActive(false);
        return true;
    }

    private void Label(GUIManager gui, Font font, Color colour, string text, float x, float y) =>
        gui.CreateText(
            text, _panel!.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), font, 15, colour,
            outline: true, Color.black, 70f, 24f, addContentSizeFitter: false);

    private InputField? Field(GUIManager gui, float x, float y, string value)
    {
        GameObject field = gui.CreateInputField(
            _panel!.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y),
            placeholderText: value, fontSize: 15, width: 70f, height: 26f);
        InputField? input = field.GetComponent<InputField>();
        if (input != null)
        {
            input.text = value;
            input.contentType = InputField.ContentType.IntegerNumber;
        }

        return input;
    }

    private Toggle? Check(GUIManager gui, Font font, Color colour, string label, float x, float y)
    {
        GameObject toggle = gui.CreateToggle(_panel!.transform, 22f, 22f);
        var rect = (RectTransform)toggle.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        Text text = gui.CreateText(
            label, _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(x + 16f + 130f, y), font, 13, colour, outline: false, Color.black, 260f, 24f,
            addContentSizeFitter: false).GetComponent<Text>();
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        return toggle.GetComponentInChildren<Toggle>();
    }

    private Button? AddButton(GUIManager gui, string label, float x, float y, float width, Action onClick)
    {
        GameObject button = gui.CreateButton(
            label, _panel!.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), width, 28f);
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
            new Vector2(0f, y - (height / 2f)), font, 13, colour, outline: false, Color.black, PanelWidth - 40f, height,
            addContentSizeFitter: false).GetComponent<Text>();
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
            "The collection order panel failed and was disabled for this session (orders still work from the " +
            "console): " + SafeFailure.Brief(exception));
    }
}
