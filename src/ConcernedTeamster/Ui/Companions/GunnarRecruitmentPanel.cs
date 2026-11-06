using System;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
namespace TheConcernedCat.ConcernedTeamster.Ui.Companions;

/// <summary>Plain UI owner, ticked by the recruitment service. Owns only its GUI
/// objects; no extra motor, input loop or native body component.</summary>
internal sealed class GunnarRecruitmentPanel
{
    private readonly Func<string, string> _act;
    private readonly Action<Exception> _fail;
    private GameObject? _button, _panel;
    private Text? _text;
    internal GunnarRecruitmentPanel(Func<string, string> act, Action<Exception> fail) { _act = act; _fail = fail; }
    internal void Tick(bool visible)
    {
        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
        if (_button == null && visible)
        {
            _button = GUIManager.Instance.CreateButton(L("gunnar.recruit.ui.button"), GUIManager.CustomGUIFront.transform,
                new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(110, 250), 190, 32);
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
            if (_panel.activeSelf) _act(GunnarRecruitmentStrings.Cancel);
            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf) Report(_act(GunnarRecruitmentStrings.Status));
        }
        catch (Exception exception) { _fail(exception); }
    }
    private void Build()
    {
        var gui = GUIManager.Instance;
        _panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), Vector2.zero, 540, 360, draggable: true);
        gui.CreateText(L("gunnar.recruit.ui.title"), _panel.transform, new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -25), gui.AveriaSerifBold, 20, Color.white, true, Color.black, 430, 30, false);
        _text = gui.CreateText("", _panel.transform, new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -130), gui.AveriaSerifBold, 16, Color.white, true, Color.black, 490, 170, false).GetComponent<Text>();
        Button(gui, L("gunnar.recruit.ui.choose"), -130, -240, () => Report(_act(GunnarRecruitmentStrings.Choose)));
        Button(gui, L("gunnar.recruit.ui.confirm"), 130, -240, () => Report(_act(GunnarRecruitmentStrings.Confirm)));
        Button(gui, L("gunnar.recruit.ui.status"), -130, -285, () => Report(_act(GunnarRecruitmentStrings.Status)));
        Button(gui, L("gunnar.recruit.ui.cancel"), 130, -285, () => { _act(GunnarRecruitmentStrings.Cancel); _panel.SetActive(false); });
        Button(gui, L("gunnar.recruit.ui.offer"), 0, -330, () => Report(_act(GunnarRecruitmentStrings.Offer)));
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
    private static string L(string key) => GunnarRecruitmentStrings.Resolve(key,k=>TeamsterStrings.HasKey(k)?TeamsterStrings.Get(k):k);
    private void Report(string message) { if (_text != null) _text.text = message; }
    internal void Dispose()
    {
        if (_panel != null) UnityEngine.Object.Destroy(_panel);
        if (_button != null) UnityEngine.Object.Destroy(_button);
        _panel = _button = null; _text = null;
    }
}
