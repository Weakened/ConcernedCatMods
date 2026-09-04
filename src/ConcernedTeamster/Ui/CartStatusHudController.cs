using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
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
    private const float PanelHeight = 300f;
    private const float RowHeight = 26f;
    private const float RefreshPeriodSeconds = 0.25f;

    private TeamsterSettings? _settings;
    private ManualLogSource? _log;
    private CartTelemetryPump? _pump;
    private CargoManifestPanel? _manifestPanel;
    private bool _failed;

    private GameObject? _button;
    private GameObject? _panel;
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
                _selectedCartId = null;
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
            else if (_manifestPanel is { IsVisible: true } && now >= _nextSelectionRefreshTime)
            {
                // The manifest follows the same sticky selection the status
                // panel shows; keep it current even with the status closed.
                _nextSelectionRefreshTime = now + 1.0;
                CartStatusViewModel selection = CartStatusPresenter.Present(
                    _pump?.Telemetry, _selectedCartId, now, telemetryActive: _pump is not null);
                _selectedCartId = selection.SelectedCartId.Length > 0 ? selection.SelectedCartId : null;
            }

            _manifestPanel?.HandleFrame(now, _selectedCartId);
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

        _rows = new Text[7];
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

        GameObject manifest = gui.CreateButton(
            "Manifest", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-62f, 28f), 110f, 30f);
        manifest.GetComponent<Button>().onClick.AddListener(() => _manifestPanel?.Toggle());

        GameObject close = gui.CreateButton(
            "Close", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(62f, 28f), 110f, 30f);
        close.GetComponent<Button>().onClick.AddListener(() => _panel!.SetActive(false));

        _panel.SetActive(false);
        return true;
    }

    private void RefreshPanel(double nowSeconds)
    {
        CartStatusViewModel viewModel = CartStatusPresenter.Present(
            _pump?.Telemetry, _selectedCartId, nowSeconds, telemetryActive: _pump is not null);
        _selectedCartId = viewModel.SelectedCartId.Length > 0 ? viewModel.SelectedCartId : null;

        if (_rows.Length != 7 || _rows[0] == null)
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

            _manifestPanel?.Hide();
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
