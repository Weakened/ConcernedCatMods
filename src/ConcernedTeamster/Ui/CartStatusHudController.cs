using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using TheConcernedCat.ConcernedTeamster.Domain.Warnings;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>The discoverable Cart Status surface (CT-005): a small always-
/// visible "Cart" button at the right screen edge while in a world, opening
/// a draggable wood panel whose rows come entirely from the headless
/// presenter. Built on the same Jötunn GUIManager calls Concerned
/// Cartographer ships, with the same fail-closed contract: any UI exception
/// disables this surface for the session with one ERROR line and the rest
/// of the mod keeps working. Unity kills the UI GameObjects on scene
/// changes; construction is lazy and re-runs when the custom GUI root
/// reappears. No game object is read here — cart data arrives only through
/// the telemetry pump.</summary>
internal sealed class CartStatusHudController : MonoBehaviour
{
    private const float PanelWidth = 320f;
    private const float PanelHeight = 410f;
    private const float RowHeight = 26f;
    private const float RefreshPeriodSeconds = 0.25f;
    private const int RowCount = 9;

    private TeamsterSettings? _settings;
    private ManualLogSource? _log;
    private CartTelemetryPump? _pump;
    private CargoManifestPanel? _manifestPanel;
    private RecoveryGuidancePanel? _guidancePanel;
    private TripHistoryPanel? _tripPanel;
    private bool _failed;

    private GameObject? _button;
    private GameObject? _panel;
    private Text? _hudHint;
    private GameObject? _brakeButton;
    private Text? _brakeButtonText;
    private Text[] _rows = Array.Empty<Text>();
    private string? _selectedCartId;
    private double _nextRefreshTime;
    private double _nextSelectionRefreshTime;

    internal void Initialize(TeamsterSettings settings, ManualLogSource log, CartTelemetryPump? pump)
    {
        _settings = settings;
        _log = log;
        _pump = pump;
        _manifestPanel = new CargoManifestPanel(log);
        _guidancePanel = new RecoveryGuidancePanel(log);
        _tripPanel = new TripHistoryPanel(log);
        _tripPanel.BindPump(pump);
    }

    private void Update()
    {
        if (_failed || _settings is null)
        {
            return;
        }

        try
        {
            bool inWorld = CartAdapter.HasLocalPlayer();
            EnsureButton(inWorld);

            if (!inWorld)
            {
                if (_panel != null && _panel.activeSelf)
                {
                    _panel.SetActive(false);
                }

                _manifestPanel?.Reset();
                _guidancePanel?.Hide();
                _tripPanel?.Hide();
                _selectedCartId = null;
                if (_hudHint != null && _hudHint.gameObject.activeSelf)
                {
                    _hudHint.gameObject.SetActive(false);
                }

                return;
            }

            if (_settings.PanelShortcut.Value.IsDown())
            {
                TogglePanel();
            }

            double now = Time.unscaledTimeAsDouble;
            bool statusVisible = _panel != null && _panel.activeSelf;
            if (statusVisible)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _panel!.SetActive(false);
                    return;
                }

                if (now >= _nextRefreshTime)
                {
                    _nextRefreshTime = now + RefreshPeriodSeconds;
                    RefreshPanel(now);
                }
            }
            else if (now >= _nextSelectionRefreshTime &&
                (_manifestPanel is { IsVisible: true } || _settings.HudWarningHintsEnabled.Value))
            {
                // The manifest and the HUD hint follow the same sticky
                // selection the status panel shows; keep it current even
                // with the status closed.
                _nextSelectionRefreshTime = now + 1.0;
                CartStatusViewModel selection = CartStatusPresenter.Present(
                    _pump?.Telemetry, _selectedCartId, now, telemetryActive: _pump is not null);
                _selectedCartId = selection.SelectedCartId.Length > 0 ? selection.SelectedCartId : null;
            }

            _manifestPanel?.HandleFrame(now, _selectedCartId);
            _guidancePanel?.HandleFrame(now, _pump);
            _tripPanel?.HandleFrame(now, _pump);
            UpdateHudHint();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void EnsureButton(bool inWorld)
    {
        // Destroyed-on-scene-change GameObjects compare == null (Unity
        // operator), which re-triggers the lazy build exactly like
        // Cartographer's panels.
        if (_button == null)
        {
            if (!inWorld || GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
            {
                return;
            }

            _button = GUIManager.Instance.CreateButton(
                "Cart",
                GUIManager.CustomGUIFront.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-70f, 170f), 80f, 32f);
            _button.GetComponent<Button>().onClick.AddListener(TogglePanel);

            // The optional HUD warning hint lives under the button (CT-009);
            // it stays empty and inactive unless enabled and warning.
            _hudHint = GUIManager.Instance.CreateText(
                string.Empty, GUIManager.CustomGUIFront.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-160f, 140f),
                GUIManager.Instance.AveriaSerifBold, 14, new Color(1f, 0.85f, 0.5f, 1f),
                outline: true, Color.black, 300f, 40f, addContentSizeFitter: false)
                .GetComponent<Text>();
            _hudHint.alignment = TextAnchor.MiddleRight;
            _hudHint.gameObject.SetActive(false);
        }

        if (_button.activeSelf != inWorld)
        {
            _button.SetActive(inWorld);
        }
    }

    private void TogglePanel()
    {
        try
        {
            if (_panel == null && !EnsurePanel())
            {
                return;
            }

            bool show = !_panel!.activeSelf;
            _panel.SetActive(show);
            if (show)
            {
                _nextRefreshTime = 0d;
                RefreshPanel(Time.unscaledTimeAsDouble);
            }
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
        var headerColor = new Color(0.9f, 0.8f, 0.6f, 1f);
        var bodyColor = new Color(0.85f, 0.85f, 0.82f, 1f);

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront.transform,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-(PanelWidth / 2f) - 30f, 0f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(
            "Cart Status", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 19, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false);

        _rows = new Text[RowCount];
        float y = -58f;
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index] = gui.CreateText(
                string.Empty, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y - (RowHeight / 2f)),
                font, 16, bodyColor, outline: false, Color.black, PanelWidth - 40f, RowHeight,
                addContentSizeFitter: false).GetComponent<Text>();
            _rows[index].alignment = TextAnchor.UpperLeft;
            _rows[index].verticalOverflow = VerticalWrapMode.Truncate;
            y -= RowHeight;
        }

        // CT-018: trip history is world-scoped, not cart-scoped, so its
        // button lives beside the brake row.
        GameObject trips = gui.CreateButton(
            "Trips", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(90f, 62f), 110f, 30f);
        trips.GetComponent<Button>().onClick.AddListener(() => _tripPanel?.Toggle(_pump));

        // CT-012: the explicit, visible brake control. Hidden unless the
        // selected cart is under local vanilla authority (fail closed).
        _brakeButton = gui.CreateButton(
            "Engage brake", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-70f, 62f), 150f, 30f);
        _brakeButtonText = _brakeButton.GetComponentInChildren<Text>();
        _brakeButton.GetComponent<Button>().onClick.AddListener(() =>
        {
            _pump?.Brake?.RequestToggle(_selectedCartId);
            RefreshPanel(Time.unscaledTimeAsDouble);
        });
        _brakeButton.SetActive(false);

        GameObject manifest = gui.CreateButton(
            "Manifest", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-102f, 28f), 96f, 30f);
        manifest.GetComponent<Button>().onClick.AddListener(() => _manifestPanel?.Toggle());

        GameObject guidance = gui.CreateButton(
            "Guidance", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), 96f, 30f);
        guidance.GetComponent<Button>().onClick.AddListener(() => _guidancePanel?.Toggle());

        GameObject close = gui.CreateButton(
            "Close", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(102f, 28f), 96f, 30f);
        close.GetComponent<Button>().onClick.AddListener(() => _panel!.SetActive(false));

        _panel.SetActive(false);
        return true;
    }

    private void RefreshPanel(double nowSeconds)
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            _pump?.Telemetry, _selectedCartId, nowSeconds, telemetryActive: _pump is not null);
        _selectedCartId = viewModel.SelectedCartId.Length > 0 ? viewModel.SelectedCartId : null;

        if (_rows.Length != RowCount || _rows[0] == null)
        {
            return;
        }

        _rows[0].text = viewModel.SourceLine;
        _rows[1].text = viewModel.MassLine;
        _rows[2].text = viewModel.BreakdownLine;
        _rows[3].text = viewModel.GradeLine;
        _rows[4].text = viewModel.SurfaceLine;
        _rows[5].text = viewModel.PullLine;
        _rows[6].text = viewModel.FreshnessLine;

        string warningLine = string.Empty;
        if (_pump is not null && _settings is not null && _settings.PanelWarningsEnabled.Value &&
            viewModel.SelectedCartId.Length > 0)
        {
            CartWarning? warning = _pump.TryGetWarning(viewModel.SelectedCartId);
            if (warning is not null)
            {
                warningLine = warning.ComposeLine();
            }
        }

        _rows[7].text = warningLine;

        // CT-013: the stuck diagnosis for the pulled cart (the sticky
        // selection prefers it). Read-only; evaluation lives in the pump.
        string diagnosticLine = string.Empty;
        if (_pump?.LatestDiagnostic is { } diagnostic &&
            viewModel.SelectedCartId.Length > 0 &&
            _pump.LatestDescentRisk?.CartId == viewModel.SelectedCartId)
        {
            diagnosticLine = diagnostic.ComposeLine();
        }

        _rows[8].text = diagnosticLine;
        RefreshBrakeButton(viewModel.SelectedCartId);
    }

    /// <summary>Shows the brake control only for a selected cart under
    /// local vanilla authority and not being pulled (or when the brake is
    /// engaged on it, so release is always reachable). Reads facts through
    /// the fail-closed adapter; any doubt hides the control.</summary>
    private void RefreshBrakeButton(string selectedCartId)
    {
        if (_brakeButton == null)
        {
            return;
        }

        BrakeService? brake = _pump?.Brake;
        bool visible = false;
        string label = "Engage brake";
        if (brake is not null && selectedCartId.Length > 0)
        {
            if (brake.EngagedCartId == selectedCartId)
            {
                visible = true;
                label = "Release brake";
            }
            else if (!brake.IsEngaged)
            {
                Domain.Brake.BrakeFacts facts = CartBrakeAdapter.ReadFacts(selectedCartId);
                visible = facts.CartExists && facts.IsLocalAuthority && !facts.IsAttached &&
                    facts.DistanceMeters <= Domain.Brake.BrakeLifecycle.EngageMaxDistanceMeters;
            }
        }

        if (_brakeButton.activeSelf != visible)
        {
            _brakeButton.SetActive(visible);
        }

        if (visible && _brakeButtonText != null && _brakeButtonText.text != label)
        {
            _brakeButtonText.text = label;
        }
    }

    /// <summary>Shows the current warning for the selected cart while it is
    /// being pulled, when the HUD hint is enabled. Reads only — evaluation
    /// happened on the snapshot inside the pump.</summary>
    private void UpdateHudHint()
    {
        if (_hudHint == null)
        {
            return;
        }

        string text = string.Empty;
        if (_settings is not null && _settings.HudWarningHintsEnabled.Value &&
            _pump is not null && _selectedCartId is not null &&
            _pump.Telemetry is { } telemetry &&
            telemetry.TryGetValue(_selectedCartId, out Domain.Carts.CartTelemetry selected) &&
            selected.IsPulledByLocalPlayer)
        {
            CartWarning? warning = _pump.TryGetWarning(_selectedCartId);
            if (warning is not null)
            {
                text = warning.ComposeLine();
            }
        }

        bool visible = text.Length > 0;
        if (_hudHint.gameObject.activeSelf != visible)
        {
            _hudHint.gameObject.SetActive(visible);
        }

        if (visible && _hudHint.text != text)
        {
            _hudHint.text = text;
        }
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        try
        {
            if (_panel != null)
            {
                _panel.SetActive(false);
            }

            if (_button != null)
            {
                _button.SetActive(false);
            }

            if (_hudHint != null)
            {
                _hudHint.gameObject.SetActive(false);
            }

            _manifestPanel?.Hide();
            _guidancePanel?.Hide();
            _tripPanel?.Hide();
        }
        catch
        {
            // Hiding is best-effort; the surface is already disabled.
        }

        _log?.LogError(
            "Cart Status UI failed and was disabled for this session " +
            $"(telemetry keeps running): {exception.GetType().Name}: {exception.Message}");
    }
}
