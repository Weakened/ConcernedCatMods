using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Survey side panel, RC11 edition (blockers 8–13): the UI is
/// the primary surface — no player-facing copy points at the console.
/// Three views over one shared row grid with deliberate vertical spacing:
///
///  - PENDING: live scanner status, review rows (accept/reject), Scan
///    now, Accept all / Reject all (click-again confirm).
///  - REJECTED: the persistent rejected list — restore to review or
///    accept directly; rejected objects never re-offer on their own.
///  - RULES: view, enable/disable, delete, and add survey rules without
///    touching survey-rules.tsv (the file stays the shareable
///    import/export; Reload file remains for advanced use).
///
/// All mutations route through the same tested `cc_survey` paths.</summary>
internal sealed class SurveyPanel : CcSidePanel
{
    private const int Slots = 5;

    private enum View
    {
        Pending,
        Rejected,
        Rules,
    }

    // Icon choices for new rules, cycled by the Rules view's icon button.
    private static readonly (string IconId, string Category)[] AddChoices =
    {
        ("cc:resource", "Resources"),
        ("cc:mine", "Resources"),
        ("cc:dungeon", "Dungeons"),
        ("cc:objective", "Points of interest"),
        ("cc:danger", "Danger"),
        ("cc:farm", "Base"),
    };

    private readonly CartographerSettings _settings;
    private readonly Func<string[], string> _execute;
    private readonly Func<IReadOnlyList<SurveyEngine.Observation>> _observations;
    private readonly Func<SurveyEngine> _engine;
    private readonly Func<SurveyScanner?> _scanner;
    private Toggle? _enabled;
    private Text? _note;
    private Text? _status;
    private Text? _output;
    private readonly GameObject[] _rows = new GameObject[Slots];
    private readonly Text[] _rowLabels = new Text[Slots];
    private readonly Text[] _rowButtonALabels = new Text[Slots];
    private readonly Text[] _rowButtonBLabels = new Text[Slots];
    private readonly Button[] _viewButtons = new Button[3];
    private GameObject? _scanNowButton;
    private GameObject? _acceptAllButton;
    private GameObject? _rejectAllButton;
    private GameObject? _restoreAllButton;
    private GameObject? _addRuleField;
    private GameObject? _addRuleButton;
    private GameObject? _addIconButton;
    private Text? _addIconLabel;
    private GameObject? _reloadButton;
    private GameObject? _pageBack;
    private GameObject? _pageForward;
    private InputField? _addRuleInput;
    private View _view = View.Pending;
    private int _page;
    private int _addChoice;
    private string _pendingConfirm = "";
    private float _refreshElapsed;

    public SurveyPanel(
        ManualLogSource log,
        CartographerSettings settings,
        Func<string[], string> execute,
        Func<IReadOnlyList<SurveyEngine.Observation>> observations,
        Func<SurveyEngine> engine,
        Func<SurveyScanner?> scanner)
        : base(log, "survey.title", 384f, 648f)
    {
        _settings = settings;
        _execute = execute;
        _observations = observations;
        _engine = engine;
        _scanner = scanner;
    }

    protected override void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y)
    {
        float contentWidth = Width - 44f;
        float left = -contentWidth / 2f;

        _enabled = AddToggle(gui, font, headerColor, AtlasStrings.Get("survey.enable"), -150f, y, value =>
        {
            _settings.SurveyRulesEnabled.Value = value;
            Refresh();
        }, labelWidth: 170f);
        y -= 32f;

        float third = contentWidth / 3f;
        string[] viewLabels = { "Pending", "Rejected", "Rules" };
        for (int index = 0; index < viewLabels.Length; index++)
        {
            int captured = index;
            _viewButtons[index] = AddButton(gui, viewLabels[index], left + (third * (index + 0.5f)), y, third - 4f, 26f, () =>
            {
                _view = (View)captured;
                _page = 0;
                _pendingConfirm = "";
                Refresh();
            });
        }

        y -= 32f;

        _note = AddBody(gui, font, "", 12, Color.white, ref y, 30f);

        // RC11 blocker 8: the status block sits below the note with its own
        // reserved height, and the result rows start below IT — the four
        // zones can never overlap.
        _status = AddBody(gui, font, "", 12, new Color(0.85f, 1f, 0.85f, 1f), ref y, 60f);
        y -= 8f;

        for (int index = 0; index < Slots; index++)
        {
            int captured = index;
            var row = new GameObject("CCSurveyRow", typeof(RectTransform));
            row.transform.SetParent(Panel!.transform, worldPositionStays: false);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(contentWidth, 26f);

            Text label = gui.CreateText(
                "", row.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-34f, 0f),
                font, 12, Color.white, outline: false, Color.black, contentWidth - 72f, 26f, addContentSizeFitter: false)
                .GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            _rowLabels[index] = label;

            GameObject buttonA = gui.CreateButton("+", row.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-46f, 0f), 30f, 24f);
            _rowButtonALabels[index] = buttonA.GetComponentInChildren<Text>();
            buttonA.GetComponent<Button>().onClick.AddListener(() => RowAction(captured, primary: true));
            GameObject buttonB = gui.CreateButton("-", row.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), 30f, 24f);
            _rowButtonBLabels[index] = buttonB.GetComponentInChildren<Text>();
            buttonB.GetComponent<Button>().onClick.AddListener(() => RowAction(captured, primary: false));

            _rows[index] = row;
            row.SetActive(false);
            y -= 34f;
        }

        y -= 4f;
        float half = contentWidth / 2f;

        // Action row A (view-dependent widget sets share these positions;
        // only one view's widgets are active at a time).
        _scanNowButton = AddButton(gui, "Scan now", left + (half * 0.5f), y, half - 4f, 26f, () =>
        {
            if (!_settings.SurveyRulesEnabled.Value)
            {
                Report("Enable the survey first — scanning only runs while it is on.");
                return;
            }

            _scanner()?.RequestImmediateScan();
            Report("Scanning around you now; new observations appear above within a second.");
            Refresh();
        }).gameObject;
        _acceptAllButton = AddButton(gui, "Accept all", left + (half * 1.5f), y, half - 4f, 26f,
            () => ConfirmedBulk("accept")).gameObject;
        _restoreAllButton = AddButton(gui, "Restore all", left + (half * 0.5f), y, half - 4f, 26f, () =>
        {
            Report(_execute(new[] { "restore", "all" }));
            _page = 0;
            Refresh();
        }).gameObject;

        GameObject addField = gui.CreateInputField(
            Panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(left + (contentWidth - 80f) / 2f, y),
            InputField.ContentType.Standard, "new rule prefab pattern (bush*)", 12, contentWidth - 84f, 26f);
        _addRuleField = addField;
        _addRuleInput = addField.GetComponent<InputField>();
        _addRuleButton = AddButton(gui, "Add", left + contentWidth - 40f, y, 76f, 26f, AddRuleClicked).gameObject;
        y -= 30f;

        // Action row B.
        _rejectAllButton = AddButton(gui, "Reject all", left + (half * 0.5f), y, half - 4f, 26f,
            () => ConfirmedBulk("reject")).gameObject;
        _addIconButton = AddButton(gui, "", left + (half * 0.5f), y, half - 4f, 26f, () =>
        {
            _addChoice = (_addChoice + 1) % AddChoices.Length;
            RefreshAddIconLabel();
        }).gameObject;
        _addIconLabel = _addIconButton.GetComponentInChildren<Text>();
        _reloadButton = AddButton(gui, "Reload file", left + (half * 1.5f) - 36f, y, half - 76f, 26f, () =>
        {
            Report(_execute(new[] { "reload" }));
            _page = 0;
            Refresh();
        }).gameObject;
        _pageBack = AddButton(gui, "◀", left + contentWidth - 66f, y, 28f, 26f, () => Turn(-1)).gameObject;
        _pageForward = AddButton(gui, "▶", left + contentWidth - 34f, y, 28f, 26f, () => Turn(1)).gameObject;
        y -= 34f;

        _output = AddBody(gui, font, "", 12, Color.white, ref y, 44f);
    }

    private void AddRuleClicked()
    {
        string pattern = _addRuleInput != null ? _addRuleInput.text.Trim() : "";
        if (pattern.Length == 0)
        {
            Report("Type a prefab pattern first — e.g. 'greydwarf_root*' ('*' matches name prefixes).");
            return;
        }

        (string iconId, string category) = AddChoices[_addChoice];
        Report(_execute(new[] { "ruleadd", pattern, iconId, category }));
        if (_addRuleInput != null)
        {
            _addRuleInput.text = "";
        }

        Refresh();
    }

    private void Turn(int direction)
    {
        _page = Math.Max(0, _page + direction);
        Refresh();
    }

    protected override void OnShown()
    {
        if (_enabled != null)
        {
            SetToggleSilently(_enabled, _settings.SurveyRulesEnabled.Value);
        }

        _pendingConfirm = "";
        _page = 0;
        RefreshAddIconLabel();
        Refresh();
    }

    public override void HandleFrame()
    {
        base.HandleFrame();
        if (!IsVisible)
        {
            return;
        }

        // The status line carries live facts (sweep age, pending arrivals
        // from the background cadence); refresh once a second.
        _refreshElapsed += Time.unscaledDeltaTime;
        if (_refreshElapsed >= 1f)
        {
            _refreshElapsed = 0f;
            Refresh();
        }
    }

    private void RowAction(int index, bool primary)
    {
        int itemIndex = (_page * Slots) + index + 1;
        switch (_view)
        {
            case View.Pending:
                Report(_execute(new[] { primary ? "accept" : "reject", itemIndex.ToString() }));
                break;
            case View.Rejected:
                Report(_execute(new[] { primary ? "restore" : "acceptrejected", itemIndex.ToString() }));
                break;
            case View.Rules:
                if (primary)
                {
                    bool currentlyEnabled = ItemIsEnabledRule(itemIndex - 1);
                    Report(_execute(new[] { currentlyEnabled ? "ruleoff" : "ruleon", itemIndex.ToString() }));
                }
                else
                {
                    Report(_execute(new[] { "ruledel", itemIndex.ToString() }));
                }

                break;
        }

        Refresh();
    }

    private bool ItemIsEnabledRule(int ruleIndex)
    {
        IReadOnlyList<SurveyRule> rules = _engine().Rules.Rules;
        return ruleIndex >= 0 && ruleIndex < rules.Count && rules[ruleIndex].Enabled;
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

    private void RefreshAddIconLabel()
    {
        if (_addIconLabel != null)
        {
            (string iconId, string category) = AddChoices[_addChoice];
            _addIconLabel.text = $"{category} ({iconId})";
            _addIconLabel.fontSize = 11;
        }
    }

    private void Refresh()
    {
        SurveyEngine engine = _engine();
        int itemCount = _view switch
        {
            View.Pending => _observations().Count,
            View.Rejected => engine.Rejected.Count,
            _ => engine.Rules.Rules.Count,
        };
        int pageCount = Math.Max(1, (itemCount + Slots - 1) / Slots);
        _page = Math.Min(_page, pageCount - 1);

        RefreshChrome(pageCount);
        RefreshNoteAndStatus(engine, itemCount, pageCount);
        RefreshRows(engine);
    }

    private void RefreshChrome(int pageCount)
    {
        _scanNowButton?.SetActive(_view == View.Pending);
        _acceptAllButton?.SetActive(_view == View.Pending);
        _rejectAllButton?.SetActive(_view == View.Pending);
        _restoreAllButton?.SetActive(_view == View.Rejected);
        _addRuleField?.SetActive(_view == View.Rules);
        _addRuleButton?.SetActive(_view == View.Rules);
        _addIconButton?.SetActive(_view == View.Rules);
        _reloadButton?.SetActive(_view == View.Rules);
        bool paged = pageCount > 1;
        _pageBack?.SetActive(paged);
        _pageForward?.SetActive(paged);

        for (int index = 0; index < _viewButtons.Length; index++)
        {
            Text? label = _viewButtons[index] != null ? _viewButtons[index].GetComponentInChildren<Text>() : null;
            if (label != null)
            {
                string name = index == 0 ? "Pending" : index == 1 ? "Rejected" : "Rules";
                label.text = (int)_view == index ? "» " + name : name;
            }
        }
    }

    private void RefreshNoteAndStatus(SurveyEngine engine, int itemCount, int pageCount)
    {
        string pageSuffix = pageCount > 1 ? $" — page {_page + 1}/{pageCount}" : "";
        if (_note != null)
        {
            _note.text = _view switch
            {
                View.Pending => AtlasStrings.Get("survey.note"),
                View.Rejected => "Rejected observations stay here and never re-offer on their own.",
                _ => "Rules decide what nearby objects become observations.",
            };
        }

        if (_status == null)
        {
            return;
        }

        switch (_view)
        {
            case View.Pending:
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
                    scanState = $"Scanning continuously ({_settings.SurveyScanRadius.Value:0} m around you)";
                }

                string lastScan = scanner?.LastScanUtc is DateTime last
                    ? $"Last sweep {Math.Max(0, (int)(DateTime.UtcNow - last).TotalSeconds)}s ago: {scanner.LastScanExamined} checked, {scanner.LastScanAdded} new"
                    : "Last sweep: none finished yet this session";

                _status.text = scanState +
                    $"\n{lastScan}" +
                    $"\n{itemCount} pending{pageSuffix}" +
                    (itemCount > 0 ? " — accept (+) to pin, reject (−) to set aside:" : "");
                break;
            case View.Rejected:
                _status.text = $"{itemCount} rejected{pageSuffix}" +
                    (itemCount > 0 ? " — restore (↩) sends one back to review; accept (+) pins it:" : "");
                break;
            default:
                _status.text =
                    $"{engine.Rules.Rules.Count} rule(s), {engine.Rules.Blacklist.Count} blocked pattern(s){pageSuffix}" +
                    "\nToggle (⏻) or delete (✕) below; add with the pattern box." +
                    "\nsurvey-rules.tsv stays the shareable import/export.";
                break;
        }
    }

    private void RefreshRows(SurveyEngine engine)
    {
        IReadOnlyList<SurveyEngine.Observation> observations = _observations();
        for (int index = 0; index < Slots; index++)
        {
            int itemIndex = (_page * Slots) + index;
            bool used;
            switch (_view)
            {
                case View.Pending:
                    used = itemIndex < observations.Count;
                    if (used)
                    {
                        SurveyEngine.Observation observation = observations[itemIndex];
                        _rowLabels[index].text = $"{observation.SuggestedName} [{observation.Category}]";
                        _rowButtonALabels[index].text = "+";
                        _rowButtonBLabels[index].text = "−";
                    }

                    break;
                case View.Rejected:
                    used = itemIndex < engine.Rejected.Count;
                    if (used)
                    {
                        SurveyEngine.RejectedObservation entry = engine.Rejected[itemIndex];
                        _rowLabels[index].text = $"{entry.SuggestedName} [{entry.Category}]";
                        _rowButtonALabels[index].text = "↩";
                        _rowButtonBLabels[index].text = "+";
                    }

                    break;
                default:
                    used = itemIndex < engine.Rules.Rules.Count;
                    if (used)
                    {
                        SurveyRule rule = engine.Rules.Rules[itemIndex];
                        _rowLabels[index].text =
                            $"{(rule.Enabled ? "" : "(off) ")}{rule.Pattern} → {rule.Category}";
                        _rowButtonALabels[index].text = "⏻";
                        _rowButtonBLabels[index].text = "✕";
                    }

                    break;
            }

            _rows[index].SetActive(used);
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
