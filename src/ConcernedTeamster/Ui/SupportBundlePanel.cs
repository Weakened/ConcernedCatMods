using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Support;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>The Support Bundle surface (CT-039), reached from the Cart
/// Status panel's Support button: one click composes and writes a
/// sanitized diagnostic file (versions, config, compatibility status,
/// sidecar summaries, this session's recovery events, and recent log
/// lines — see <see cref="Domain.Support.SupportBundleComposer"/> and
/// <see cref="Domain.Support.SupportBundleSanitizer"/> for what that
/// means and how it is enforced) and shows where it landed. A Report a
/// Bug button (CT-041) opens the public GitHub issue tracker on an
/// explicit click — the one and only outbound action this panel ever
/// takes, and it carries no Teamster data with it. Fail-closed
/// session-disable like every Teamster panel.</summary>
internal sealed class SupportBundlePanel
{
    private const float PanelWidth = 420f;
    private const float PanelHeight = 320f;

    private readonly ManualLogSource _log;
    private readonly Func<float> _uiScale;
    private readonly string _pluginVersion;
    private readonly TeamsterSettings _settings;
    private readonly LogTailRecorder _logTail;
    private readonly Func<TripRecordingService?> _tripsProvider;
    private bool _failed;
    private GameObject? _panel;
    private Text? _statusText;

    public SupportBundlePanel(
        ManualLogSource log,
        Func<float> uiScale,
        string pluginVersion,
        TeamsterSettings settings,
        LogTailRecorder logTail,
        Func<TripRecordingService?> tripsProvider)
    {
        _log = log;
        _uiScale = uiScale;
        _pluginVersion = pluginVersion;
        _settings = settings;
        _logTail = logTail;
        _tripsProvider = tripsProvider;
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    public void Toggle()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_panel == null && !EnsurePanel())
            {
                return;
            }

            bool show = !_panel!.activeSelf;
            _panel.SetActive(show);
            if (show && _statusText != null)
            {
                _statusText.text = TeamsterStrings.Get("support.readyToExport");
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void Hide()
    {
        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
        }
    }

    public void HandleFrame()
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
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void HandleExportClicked()
    {
        if (_failed || _statusText == null)
        {
            return;
        }

        try
        {
            (bool success, string? path, string? error) = SupportBundleExporter.Export(
                _pluginVersion, _settings, _tripsProvider(), _logTail, DateTime.UtcNow);
            _statusText.text = success
                ? TeamsterStrings.Format("support.exported", path!)
                : TeamsterStrings.Format("support.exportFailed", error ?? TeamsterStrings.Get("support.unknownError"));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void HandleReportBugClicked()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            Application.OpenURL(FeedbackLinks.IssuesUrl);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private bool EnsurePanel()
    {
        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
        {
            return false;
        }

        GUIManager gui = GUIManager.Instance;
        Font font = gui.AveriaSerifBold;
        Color headerColor = PanelStyle.Header;
        Color bodyColor = PanelStyle.Body;

        _panel = PanelStyle.CreateScaledWoodpanel(
            gui, GUIManager.CustomGUIFront.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 120f), PanelWidth, PanelHeight, _uiScale());

        gui.CreateText(
            TeamsterStrings.Get("support.title"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 19, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false);

        gui.CreateText(
            TeamsterStrings.Get("support.explainer"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f),
            font, 13, bodyColor, outline: true, Color.black, PanelWidth - 40f, 60f,
            addContentSizeFitter: false);

        _statusText = gui.CreateText(
            TeamsterStrings.Get("support.readyToExport"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -128f),
            font, 13, bodyColor, outline: true, Color.black, PanelWidth - 40f, 40f,
            addContentSizeFitter: false).GetComponent<Text>();
        _statusText.alignment = TextAnchor.UpperLeft;
        _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;

        gui.CreateText(
            TeamsterStrings.Get("support.feedbackExplainer"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -172f),
            font, 13, bodyColor, outline: true, Color.black, PanelWidth - 40f, 70f,
            addContentSizeFitter: false);

        GameObject reportBug = gui.CreateButton(
            TeamsterStrings.Get("support.reportBugButton"), _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), 150f, 28f);
        reportBug.GetComponent<Button>().onClick.AddListener(HandleReportBugClicked);

        GameObject export = gui.CreateButton(
            TeamsterStrings.Get("support.exportButton"), _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-60f, 26f), 110f, 28f);
        export.GetComponent<Button>().onClick.AddListener(HandleExportClicked);

        GameObject close = gui.CreateButton(
            TeamsterStrings.Get("ui.close"), _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(60f, 26f), 110f, 28f);
        close.GetComponent<Button>().onClick.AddListener(Hide);

        _panel.SetActive(false);
        return true;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        try
        {
            Hide();
        }
        catch
        {
            // Hiding is best-effort; the surface is already disabled.
        }

        _log.LogError(
            "Support Bundle panel UI failed and was disabled for this session: " +
            $"{exception.GetType().Name}: {exception.Message}");
    }
}
