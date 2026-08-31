using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Survey side panel (#102, RC8): enable/disable the opt-in
/// survey rules, see the full pipeline status — scanner state, rule
/// counts, last scan, pending count — trigger an immediate scan, and
/// review pending observations (accept/reject each, or all at once with a
/// click-again confirmation). Drives the same tested paths as the
/// `cc_survey` console (which remains a scriptable alias). Nothing ever
/// becomes a marker until explicitly accepted; an accepted observation
/// becomes a visible map pin immediately through the targeted pin sync.</summary>
internal sealed class SurveyPanel : CcSidePanel
{
    private const int Slots = 5;

    private readonly CartographerSettings _settings;
    private readonly Func<string[], string> _execute;
    private readonly Func<IReadOnlyList<SurveyEngine.Observation>> _observations;
    private readonly Func<SurveyEngine> _engine;
    private readonly Func<SurveyScanner?> _scanner;
    private Toggle? _enabled;
    private Text? _status;
    private Text? _output;
    private readonly GameObject[] _rows = new GameObject[Slots];
    private readonly Text[] _rowLabels = new Text[Slots];
    private string _pendingConfirm = "";
    private float _refreshElapsed;

    public SurveyPanel(
        ManualLogSource log,
        CartographerSettings settings,
        Func<string[], string> execute,
        Func<IReadOnlyList<SurveyEngine.Observation>> observations,
        Func<SurveyEngine> engine,
        Func<SurveyScanner?> scanner)
        : base(log, "survey.title", 384f, 600f)
    {
        _settings = settings;
        _execute = execute;
        _observations = observations;
        _engine = engine;
        _scanner = scanner;
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

        // Dedicated status block: scanner state, rules, last scan, pending.
        _status = AddBody(gui, font, "", 12, new Color(0.85f, 1f, 0.85f, 1f), ref y, 64f);

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
        float half = (Width - 44f) / 2f;
        AddButton(gui, "Scan now", left + (half * 0.5f), y, half - 4f, 26f, () =>
        {
            if (!_settings.SurveyRulesEnabled.Value)
            {
                Report("Enable the survey first — the scanner only runs while it is on.");
                return;
            }

            _scanner()?.RequestImmediateScan();
            Report("Scanning around you now; new observations appear above within a second.");
            Refresh();
        });
        AddButton(gui, "Reload rules", left + (half * 1.5f), y, half - 4f, 26f, () =>
        {
            Report(_execute(new[] { "reload" }));
            Refresh();
        });
        y -= 30f;

        AddButton(gui, "Accept all", left + (half * 0.5f), y, half - 4f, 26f, () => ConfirmedBulk("accept"));
        AddButton(gui, "Reject all", left + (half * 1.5f), y, half - 4f, 26f, () => ConfirmedBulk("reject"));
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

    public override void HandleFrame()
    {
        base.HandleFrame();
        if (!IsVisible)
        {
            return;
        }

        // The status line carries live facts (last scan age, pending
        // arrivals from the background cadence); refresh once a second.
        _refreshElapsed += Time.unscaledDeltaTime;
        if (_refreshElapsed >= 1f)
        {
            _refreshElapsed = 0f;
            Refresh();
        }
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
            SurveyEngine engine = _engine();
            SurveyScanner? scanner = _scanner();
            string scanState;
            if (scanner is { DisabledForSession: true })
            {
                scanState = "Scanner FAILED this session (see log)";
            }
            else if (!_settings.SurveyRulesEnabled.Value)
            {
                scanState = "Scanning OFF — enable above to start";
            }
            else
            {
                scanState = $"Scanning ON (every {_settings.SurveyScanIntervalSeconds.Value:0}s, {_settings.SurveyScanRadius.Value:0} m around you)";
            }

            string lastScan = scanner?.LastScanUtc is DateTime last
                ? $"Last scan {Math.Max(0, (int)(DateTime.UtcNow - last).TotalSeconds)}s ago: {scanner.LastScanExamined} object(s) checked, {scanner.LastScanAdded} new"
                : "Last scan: none yet this session";

            _status.text = scanState +
                $"\n{engine.Rules.Rules.Count} rule(s), {engine.Rules.Blacklist.Count} blocked pattern(s) (survey-rules.tsv)" +
                $"\n{lastScan}" +
                $"\n{observations.Count} pending observation(s)" +
                (observations.Count > 0 ? " — accept (+) to pin, reject (-) to drop:" : "");
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
