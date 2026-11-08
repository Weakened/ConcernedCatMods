using System;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
namespace TheConcernedCat.ConcernedForeman.Ui;

/// <summary>Plain UI owner, ticked by the resident service. Owns only its GUI
/// objects; no extra motor, input loop or native body component.</summary>
internal sealed class ResidentPanel
{
    private readonly Func<string, string> _act;
    private readonly Action<Exception> _fail;
    private GameObject? _button, _panel;
    private Text? _text;
    internal ResidentPanel(Func<string, string> act, Action<Exception> fail) { _act = act; _fail = fail; }
    internal void Tick(bool visible)
    {
        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
        if (_button == null && visible)
        {
            _button = GUIManager.Instance.CreateButton("Residents", GUIManager.CustomGUIFront.transform,
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(70, 210), 110, 32);
            _button.GetComponent<Button>().onClick.AddListener(Toggle);
        }
        if (_button != null) _button.SetActive(visible);
        if (!visible && _panel != null) _panel.SetActive(false);
    }
    private void Toggle()
    {
        try
        {
            if (_panel == null) Build();
            if (_panel == null) return;
            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf) Report(_act("status"));
        }
        catch (Exception exception) { _fail(exception); }
    }
    private void Build()
    {
        var gui = GUIManager.Instance;
        _panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), Vector2.zero, 480, 300, draggable: true);
        gui.CreateText("One ordinary resident", _panel.transform, new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -25), gui.AveriaSerifBold, 20, Color.white, true, Color.black, 430, 30, false);
        _text = gui.CreateText("", _panel.transform, new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -110), gui.AveriaSerifBold, 16, Color.white, true, Color.black, 430, 140, false).GetComponent<Text>();
        Button(gui, "Reserve nearby bed", -120, -215, () => Report(_act("reserve")));
        Button(gui, "Confirm admission", 120, -215, () => Report(_act("admit")));
        Button(gui, "Refresh status", -120, -260, () => Report(_act("status")));
        Button(gui, "Close", 120, -260, () => _panel.SetActive(false));
        _panel.SetActive(false);
    }
    private void Button(GUIManager gui, string label, float x, float y, Action act)
    {
        var button = gui.CreateButton(label, _panel!.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(x, y), 210, 30);
        button.GetComponent<Button>().onClick.AddListener(() =>
        {
            try { act(); } catch (Exception exception) { _fail(exception); }
        });
    }
    private void Report(string message) { if (_text != null) _text.text = message; }
    internal void Dispose()
    {
        if (_panel != null) UnityEngine.Object.Destroy(_panel);
        if (_button != null) UnityEngine.Object.Destroy(_button);
        _panel = _button = null; _text = null;
    }
}
