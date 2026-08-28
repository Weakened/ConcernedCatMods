using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The one-time crash-reporting consent dialog and the permanent
/// Atlas → Privacy surface (#97). Two modes over the same content (title,
/// question, what-is-sent, what-is-never-sent, provider line):
///
/// - FirstRun: shown once, on the player's first large-map open while
///   consent is Unknown (or after a material policy-version bump).
///   [Send anonymous crash reports] → Enabled, [No thanks] → Disabled,
///   [Learn more] opens PRIVACY.md without changing consent; Escape or
///   the map closing underneath counts as dismissal → Disabled.
/// - Settings: opened any time from the Atlas Drawer's Privacy button;
///   shows the current state with an immediate-effect toggle.
///
/// The answer is stored profile-level in the BepInEx config (never in
/// world saves) and is never re-asked automatically. Fail-closed: any UI
/// failure hides the panel and leaves consent unanswered (= disabled) —
/// gameplay is never blocked.</summary>
internal sealed class CrashConsentPanel
{
    private const float PanelWidth = 540f;
    private const float PanelHeight = 600f;
    private const float TextWidth = PanelWidth - 48f;

    private readonly ManualLogSource _log;
    private readonly CartographerSettings _settings;
    private GameObject? _panel;
    private GameObject? _firstRunRows;
    private GameObject? _settingsRows;
    private Text? _stateLine;
    private Text? _toggleLabel;
    private bool _firstRunMode;
    private bool _failed;

    public CrashConsentPanel(ManualLogSource log, CartographerSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    /// <summary>True when the one-time dialog still needs to be offered.</summary>
    public bool NeedsFirstRunPrompt =>
        _settings.CrashReportingConsent.Value == CrashConsentState.Unknown ||
        _settings.AcceptedPrivacyPolicyVersion.Value < CrashReportingConfig.ConsentPolicyVersion;

    public void ShowFirstRun()
    {
        Show(firstRun: true);
    }

    public void ShowSettings()
    {
        Show(firstRun: false);
    }

    /// <summary>Escape (or the large map closing underneath the first-run
    /// dialog) counts as dismissing it: consent becomes Disabled, exactly
    /// like [No thanks]. In settings mode it simply closes.</summary>
    public void HandleFrame()
    {
        if (!IsVisible)
        {
            return;
        }

        try
        {
            if (Input.GetKeyDown(KeyCode.Escape) || !Minimap.IsOpen())
            {
                if (_firstRunMode)
                {
                    Choose(CrashConsentState.Disabled);
                }
                else
                {
                    Hide();
                }
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Show(bool firstRun)
    {
        if (!EnsureBuilt())
        {
            return;
        }

        try
        {
            _firstRunMode = firstRun;
            _firstRunRows!.SetActive(firstRun);
            _settingsRows!.SetActive(!firstRun);
            if (!firstRun)
            {
                UpdateStateWidgets();
            }

            _panel!.SetActive(true);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Hide()
    {
        if (_panel != null)
        {
            _panel.SetActive(false);
        }
    }

    private void Choose(CrashConsentState state)
    {
        try
        {
            _settings.CrashReportingConsent.Value = state;
            _settings.AcceptedPrivacyPolicyVersion.Value = CrashReportingConfig.ConsentPolicyVersion;
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Could not persist the crash-reporting choice: {exception.Message}");
        }

        Hide();
    }

    private void ToggleFromSettings()
    {
        CrashConsentState next = _settings.CrashReportingConsent.Value == CrashConsentState.Enabled
            ? CrashConsentState.Disabled
            : CrashConsentState.Enabled;
        try
        {
            _settings.CrashReportingConsent.Value = next;
            _settings.AcceptedPrivacyPolicyVersion.Value = CrashReportingConfig.ConsentPolicyVersion;
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Could not persist the crash-reporting choice: {exception.Message}");
        }

        UpdateStateWidgets();
    }

    private void UpdateStateWidgets()
    {
        bool on = _settings.CrashReportingConsent.Value == CrashConsentState.Enabled;
        if (_stateLine != null)
        {
            _stateLine.text = AtlasStrings.Format("privacy.settingsState", on ? "ON" : "OFF");
        }

        if (_toggleLabel != null)
        {
            _toggleLabel.text = AtlasStrings.Get(on ? "privacy.turnOff" : "privacy.turnOn");
        }
    }

    private static void OpenPrivacyPolicy()
    {
        try
        {
            Application.OpenURL(CrashReportingConfig.PrivacyPolicyUrl);
        }
        catch
        {
            // No browser is not an error; PRIVACY.md ships with the docs.
        }
    }

    private bool EnsureBuilt()
    {
        if (_failed)
        {
            return false;
        }

        if (_panel != null)
        {
            return true;
        }

        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
        {
            return false;
        }

        try
        {
            Build();
            return _panel != null;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return false;
        }
    }

    private void Build()
    {
        GUIManager gui = GUIManager.Instance;
        Font font = gui.AveriaSerifBold;
        var headerColor = new Color(0.9f, 0.8f, 0.6f, 1f);

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront!.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
            PanelWidth, PanelHeight, draggable: false);

        gui.CreateText(
            AtlasStrings.Get("privacy.consentTitle"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f),
            font, 19, headerColor, outline: true, Color.black, TextWidth, 30f, addContentSizeFitter: false);

        CreateBody(gui, font, AtlasStrings.Get("privacy.consentQuestion"), 14, Color.white, -78f, 44f);
        CreateBody(gui, font, AtlasStrings.Get("privacy.consentSent"), 12, new Color(0.85f, 1f, 0.85f, 1f), -140f, 70f);
        CreateBody(gui, font, AtlasStrings.Get("privacy.consentNever"), 12, new Color(1f, 0.85f, 0.8f, 1f), -228f, 100f);
        CreateBody(gui, font, AtlasStrings.Get("privacy.consentProvider"), 12, new Color(1f, 1f, 1f, 0.75f), -330f, 24f);

        _firstRunRows = new GameObject("CCConsentFirstRun", typeof(RectTransform));
        _firstRunRows.transform.SetParent(_panel.transform, worldPositionStays: false);
        FillRect(_firstRunRows);
        GameObject accept = gui.CreateButton(
            AtlasStrings.Get("privacy.consentYes"), _firstRunRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -390f), 320f, 38f);
        accept.GetComponent<Button>().onClick.AddListener(() => Choose(CrashConsentState.Enabled));
        GameObject decline = gui.CreateButton(
            AtlasStrings.Get("privacy.consentNo"), _firstRunRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-90f, -440f), 150f, 32f);
        decline.GetComponent<Button>().onClick.AddListener(() => Choose(CrashConsentState.Disabled));
        GameObject learnFirstRun = gui.CreateButton(
            AtlasStrings.Get("privacy.consentLearnMore"), _firstRunRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(90f, -440f), 150f, 32f);
        learnFirstRun.GetComponent<Button>().onClick.AddListener(OpenPrivacyPolicy);

        _settingsRows = new GameObject("CCConsentSettings", typeof(RectTransform));
        _settingsRows.transform.SetParent(_panel.transform, worldPositionStays: false);
        FillRect(_settingsRows);
        _stateLine = gui.CreateText(
            "", _settingsRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -382f),
            font, 14, Color.white, outline: false, Color.black, TextWidth, 26f, addContentSizeFitter: false)
            .GetComponent<Text>();
        _stateLine.alignment = TextAnchor.MiddleCenter;
        GameObject toggle = gui.CreateButton(
            "", _settingsRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-90f, -424f), 150f, 32f);
        _toggleLabel = toggle.GetComponentInChildren<Text>();
        toggle.GetComponent<Button>().onClick.AddListener(ToggleFromSettings);
        GameObject learnSettings = gui.CreateButton(
            AtlasStrings.Get("privacy.consentLearnMore"), _settingsRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(90f, -424f), 150f, 32f);
        learnSettings.GetComponent<Button>().onClick.AddListener(OpenPrivacyPolicy);
        GameObject close = gui.CreateButton(
            AtlasStrings.Get("workbench.close"), _settingsRows.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), 120f, 32f);
        close.GetComponent<Button>().onClick.AddListener(Hide);
        _settingsRows.SetActive(false);

        _panel.SetActive(false);
    }

    private void CreateBody(GUIManager gui, Font font, string text, int size, Color color, float y, float height)
    {
        Text body = gui.CreateText(
            text, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            font, size, color, outline: false, Color.black, TextWidth, height, addContentSizeFitter: false)
            .GetComponent<Text>();
        body.alignment = TextAnchor.UpperLeft;
    }

    private static void FillRect(GameObject target)
    {
        var rect = (RectTransform)target.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        _log.LogError($"Crash-reporting consent panel failed and was disabled for this session (reporting stays off until answered elsewhere): {exception}");
    }
}
