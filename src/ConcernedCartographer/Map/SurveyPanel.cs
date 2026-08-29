using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Survey side panel (#102): enable/disable the opt-in
/// survey rules, see rule/observation status, and review pending
/// observations — accept or reject each, or all at once with a
/// click-again confirmation. Drives the same tested paths as the
/// `cc_survey` console (which remains a scriptable alias). Nothing ever
/// becomes a marker until explicitly accepted.</summary>
internal sealed class SurveyPanel : CcSidePanel
{
    private const int Slots = 5;

    private readonly CartographerSettings _settings;
    private readonly Func<string[], string> _execute;
    private readonly Func<IReadOnlyList<SurveyEngine.Observation>> _observations;
    private Toggle? _enabled;
    private Text? _status;
    private Text? _output;
    private readonly GameObject[] _rows = new GameObject[Slots];
    private readonly Text[] _rowLabels = new Text[Slots];
    private string _pendingConfirm = "";

    public SurveyPanel(
        ManualLogSource log,
        CartographerSettings settings,
        Func<string[], string> execute,
        Func<IReadOnlyList<SurveyEngine.Observation>> observations)
        : base(log, "survey.title", 384f, 560f)
    {
        _settings = settings;
        _execute = execute;
        _observations = observations;
    }

    protected override void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y)
    {
        _enabled = AddToggle(gui, font, headerColor, AtlasStrings.Get("survey.enable"), -150f, y, value =>
        {
            _settings.SurveyRulesEnabled.Value = value;
            Refresh();
        });
        y -= 32f;

        AddBody(gui, font, AtlasStrings.Get("survey.note"), 12, Color.white, ref y, 32f);
        _status = AddBody(gui, font, "", 12, new Color(0.85f, 1f, 0.85f, 1f), ref y, 30f);

        float left = -(Width - 44f) / 2f;
        for (int index = 0; index < Slots; index++)
        {
            int captured = index;
            var row = new GameObject("CCSurveyRow", typeof(RectTransform));
            row.transform.SetParent(Panel!.transform, worldPositionStays: false);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(Width - 44f, 26f);

            Text label = gui.CreateText(
                "", row.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-34f, 0f),
                font, 12, Color.white, outline: false, Color.black, Width - 116f, 26f, addContentSizeFitter: false)
                .GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            _rowLabels[index] = label;

            GameObject accept = gui.CreateButton("+", row.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-46f, 0f), 30f, 24f);
            accept.GetComponent<Button>().onClick.AddListener(() => RowAction(captured, accept: true));
            GameObject reject = gui.CreateButton("-", row.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), 30f, 24f);
            reject.GetComponent<Button>().onClick.AddListener(() => RowAction(captured, accept: false));

            _rows[index] = row;
            row.SetActive(false);
            y -= 30f;
        }

        y -= 8f;
        float third = (Width - 44f) / 3f;
        AddButton(gui, "Accept all", left + (third * 0.5f), y, third - 4f, 26f, () => ConfirmedBulk("accept"));
        AddButton(gui, "Reject all", left + (third * 1.5f), y, third - 4f, 26f, () => ConfirmedBulk("reject"));
        AddButton(gui, "Reload rules", left + (third * 2.5f), y, third - 4f, 26f, () =>
        {
            Report(_execute(new[] { "reload" }));
            Refresh();
        });
        y -= 34f;

        _output = AddBody(gui, font, "", 12, Color.white, ref y, 44f);
    }

    protected override void OnShown()
    {
        if (_enabled != null)
        {
            SetToggleSilently(_enabled, _settings.SurveyRulesEnabled.Value);
        }

        _pendingConfirm = "";
        Refresh();
    }

    private void RowAction(int index, bool accept)
    {
        IReadOnlyList<SurveyEngine.Observation> observations = _observations();
        if (index >= observations.Count)
        {
            return;
        }

        Report(_execute(new[] { accept ? "accept" : "reject", (index + 1).ToString() }));
        Refresh();
    }

    private void ConfirmedBulk(string action)
    {
        if (_pendingConfirm != action)
        {
            _pendingConfirm = action;
            Report($"{AtlasStrings.Get("survey.confirm")} — {action} ALL pending observations.");
            return;
        }

        _pendingConfirm = "";
        Report(_execute(new[] { action, "all" }));
        Refresh();
    }

    private void Refresh()
    {
        IReadOnlyList<SurveyEngine.Observation> observations = _observations();
        if (_status != null)
        {
            _status.text = $"{observations.Count} pending observation(s). " +
                (_settings.SurveyRulesEnabled.Value ? "Scanning is ON." : "Scanning is OFF.");
        }

        for (int index = 0; index < Slots; index++)
        {
            bool used = index < observations.Count;
            _rows[index].SetActive(used);
            if (used)
            {
                SurveyEngine.Observation observation = observations[index];
                _rowLabels[index].text = $"{observation.SuggestedName} [{observation.Category}]";
            }
        }
    }

    private void Report(string message)
    {
        if (_output != null)
        {
            _output.text = message;
        }
    }
}
