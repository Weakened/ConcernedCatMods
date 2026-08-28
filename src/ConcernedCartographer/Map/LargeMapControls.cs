using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Discoverability layer on the vanilla large map (#95): a small
/// "CC Atlas [hotkey]" button that opens the Atlas Drawer, and a
/// contextual one-line hint ("P — Edit with Concerned Cartographer") shown
/// while the cursor is over an editable pin. Both parent to
/// Minimap.m_largeRoot, so they exist only while the large map is open and
/// die with it on teardown; nothing here touches vanilla behavior (right-
/// click delete and all other vanilla input stay untouched). Fail-closed:
/// any UI exception disables the layer for the session — hotkeys remain
/// the functional path.</summary>
internal sealed class LargeMapControls
{
    private readonly ManualLogSource _log;
    private GameObject? _atlasButton;
    private Text? _hintText;
    private bool _failed;

    /// <summary>Raised when the map button is clicked; the runtime routes
    /// it to the same toggle as the drawer hotkey.</summary>
    public Action? AtlasButtonClicked;

    public LargeMapControls(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>Builds the controls onto the open large map when needed.
    /// Cheap after the first call; rebuilds automatically after the map
    /// root was torn down (world switch, logout).</summary>
    public void EnsureBuilt(string drawerHotkeyName)
    {
        if (_failed || _atlasButton != null)
        {
            return;
        }

        try
        {
            GameObject? largeRoot = Minimap.instance != null ? Minimap.instance.m_largeRoot : null;
            if (largeRoot == null || !largeRoot.activeInHierarchy ||
                GUIManager.Instance == null)
            {
                return;
            }

            _atlasButton = GUIManager.Instance.CreateButton(
                AtlasStrings.Format("hud.atlasButton", drawerHotkeyName),
                largeRoot.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-110f, 90f), 170f, 30f);
            _atlasButton.GetComponent<Button>().onClick.AddListener(() => AtlasButtonClicked?.Invoke());

            _hintText = GUIManager.Instance.CreateText(
                "", largeRoot.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 68f),
                GUIManager.Instance.AveriaSerifBold, 16, new Color(1f, 0.95f, 0.75f, 1f),
                outline: true, Color.black, 700f, 26f, addContentSizeFitter: false)
                .GetComponent<Text>();
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.gameObject.SetActive(false);
        }
        catch (Exception exception)
        {
            _failed = true;
            _log.LogError($"Large-map controls failed and are disabled for this session (hotkeys still work): {exception}");
        }
    }

    /// <summary>Shows the contextual edit hint, or hides it for null/empty.</summary>
    public void SetHint(string? text)
    {
        if (_failed || _hintText == null)
        {
            return;
        }

        try
        {
            bool show = !string.IsNullOrEmpty(text);
            if (show && _hintText.text != text)
            {
                _hintText.text = text;
            }

            if (_hintText.gameObject.activeSelf != show)
            {
                _hintText.gameObject.SetActive(show);
            }
        }
        catch (Exception exception)
        {
            _failed = true;
            _log.LogError($"Large-map hint failed and is disabled for this session: {exception}");
        }
    }

    /// <summary>Drops references so a new map root rebuilds the controls.
    /// The old objects are destroyed with the map hierarchy itself.</summary>
    public void Reset()
    {
        _atlasButton = null;
        _hintText = null;
    }
}
