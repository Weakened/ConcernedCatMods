using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;
using UnityEngine;
using UnityEngine.UI;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedTeamster.Ui.Hauling;

/// <summary>Puts Gunnar's panel on screen: one always-visible button at the
/// right edge while a world is up, and the panel's per-frame tick. Kept apart
/// from the panel itself so the panel stays a plain object the tests can drive.
/// Fail-closed: any exception disables the surface for the session, and hauling
/// and its console commands keep working.</summary>
internal sealed class GunnarHaulPanelHost : MonoBehaviour
{
    private ManualLogSource? _log;
    private GunnarHaulPanel? _panel;
    private Func<bool> _show = () => false;
    private Func<float> _uiScale = () => 1f;
    private GameObject? _button;
    private bool _failed;

    /// <param name="show">Whether the button belongs on screen at all: a world
    /// is up and Gunnar's hauling is switched on.</param>
    internal void Initialize(
        Func<bool> show, Func<float> uiScale, Func<HaulPanelFacts> facts, Func<HaulPanelCommand, string> haul,
        ManualLogSource log)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _uiScale = uiScale ?? throw new ArgumentNullException(nameof(uiScale));
        _log = log;
        _panel = new GunnarHaulPanel(log, uiScale, facts, haul);
    }

    private void Update()
    {
        if (_failed || _panel == null)
        {
            return;
        }

        try
        {
            bool show = _show() && GUIManager.Instance != null && GUIManager.CustomGUIFront != null;
            EnsureButton(show);
            if (!show)
            {
                _panel.Hide();
                return;
            }

            _panel.HandleFrame(Time.unscaledTimeAsDouble);
        }
        catch (Exception exception)
        {
            _failed = true;
            try
            {
                _panel.Hide();
                if (_button != null)
                {
                    _button.SetActive(false);
                }
            }
            catch (Exception)
            {
                // Already disabled.
            }

            _log?.LogError(
                "Gunnar's panel button failed and was disabled for this session: " + SafeFailure.Brief(exception));
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
                TeamsterStrings.Get(HaulPanelKeys.Button), GUIManager.CustomGUIFront.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-70f, 210f), 80f, 32f);
            _button.GetComponent<Button>().onClick.AddListener(() => _panel?.Toggle());
            PanelStyle.ApplyScale(_button, _uiScale());
        }

        if (_button.activeSelf != show)
        {
            _button.SetActive(show);
        }
    }
}
