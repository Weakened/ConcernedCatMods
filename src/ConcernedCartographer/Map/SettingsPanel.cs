using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Settings side panel (#102): privacy/crash reporting,
/// atlas backup and restore (restore requires a click-again
/// confirmation), the sanitized support bundle, and the road repair
/// tools as an Advanced section (each acts on the recorded road nearest
/// your character, exactly like `cc_roads`). Human support routing is
/// shown here; the console remains the scripting surface.</summary>
internal sealed class SettingsPanel : CcSidePanel
{
    private readonly Func<string[], string> _executeAtlas;
    private readonly Func<string[], string> _executeRoads;
    private readonly Action _openPrivacy;
    private Text? _output;
    private bool _restoreArmed;

    public SettingsPanel(
        ManualLogSource log,
        Func<string[], string> executeAtlas,
        Func<string[], string> executeRoads,
        Action openPrivacy)
        : base(log, "settings.title", 384f, 620f)
    {
        _executeAtlas = executeAtlas;
        _executeRoads = executeRoads;
        _openPrivacy = openPrivacy;
    }

    protected override void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y)
    {
        float left = -(Width - 44f) / 2f;
        float half = (Width - 44f) / 2f;

        AddButton(gui, "Privacy & crash reports…", 0f, y, Width - 44f, 30f, _openPrivacy);
        y -= 38f;

        AddButton(gui, "Back up atlas", left + (half * 0.5f), y, half - 4f, 28f, () =>
        {
            _restoreArmed = false;
            Report(_executeAtlas(new[] { "backup" }));
        });
        AddButton(gui, "Restore latest backup", left + (half * 1.5f), y, half - 4f, 28f, () =>
        {
            if (!_restoreArmed)
            {
                _restoreArmed = true;
                Report("Restoring replaces the CURRENT atlas with the newest backup (a safety backup is taken first). Click again to confirm.\n" +
                    _executeAtlas(new[] { "backups" }));
                return;
            }

            _restoreArmed = false;
            Report(_executeAtlas(new[] { "restore", "1" }));
        });
        y -= 34f;

        AddButton(gui, "Write sanitized support bundle", 0f, y, Width - 44f, 28f, () =>
        {
            _restoreArmed = false;
            Report(_executeAtlas(new[] { "support" }));
        });
        y -= 36f;

        // RC8-6: DEDICATED centered status block in the middle of the
        // panel. Every action reports into this framed, fixed-size box —
        // text truncates inside it and can never overlay the Advanced road
        // buttons below (which sit at fixed rows underneath the block).
        const float statusHeight = 150f;
        var statusFrame = new GameObject("CCSettingsStatus", typeof(RectTransform), typeof(Image));
        statusFrame.transform.SetParent(Panel!.transform, worldPositionStays: false);
        var frameRect = (RectTransform)statusFrame.transform;
        frameRect.anchorMin = new Vector2(0.5f, 1f);
        frameRect.anchorMax = new Vector2(0.5f, 1f);
        frameRect.anchoredPosition = new Vector2(0f, y - (statusHeight / 2f));
        frameRect.sizeDelta = new Vector2(Width - 40f, statusHeight);
        var frameImage = statusFrame.GetComponent<Image>();
        frameImage.color = new Color(0f, 0f, 0f, 0.45f);
        frameImage.raycastTarget = false;

        _output = gui.CreateText(
            "", statusFrame.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
            font, 11, Color.white, outline: false, Color.black, Width - 56f, statusHeight - 12f,
            addContentSizeFitter: false)
            .GetComponent<Text>();
        _output.alignment = TextAnchor.UpperLeft;
        _output.verticalOverflow = VerticalWrapMode.Truncate;
        _output.horizontalOverflow = HorizontalWrapMode.Wrap;
        y -= statusHeight + 10f;

        AddBody(gui, font, "Road repair (Advanced) — acts on the recorded road nearest your character:", 12, headerColor, ref y, 30f);

        // RC12 blocker 4 clearance: center-pivot buttons reach half their
        // height above their y.
        y -= 10f;

        string[] row1 = { "Status", "Kind", "Hide", "Unhide" };
        string[] row2 = { "Delete", "Split", "Join", "Rebuild" };
        float quarter = (Width - 44f) / 4f;
        for (int index = 0; index < 4; index++)
        {
            string op1 = row1[index].ToLowerInvariant();
            string op2 = row2[index].ToLowerInvariant();
            AddButton(gui, row1[index], left + (quarter * (index + 0.5f)), y, quarter - 4f, 26f,
                () => Report(_executeRoads(new[] { op1 })));
            AddButton(gui, row2[index], left + (quarter * (index + 0.5f)), y - 30f, quarter - 4f, 26f,
                () => Report(_executeRoads(new[] { op2 })));
        }

        y -= 60f;
        AddButton(gui, "Undo road edit", 0f, y, 160f, 26f, () => Report(_executeRoads(new[] { "undo" })));
        y -= 32f;

        AddBody(gui, font, AtlasStrings.Get("settings.emailLine"), 11, new Color(1f, 1f, 1f, 0.7f), ref y, 22f);
    }

    protected override void OnShown()
    {
        _restoreArmed = false;
        Report("Action results appear here.");
    }

    private void Report(string message)
    {
        if (_output != null)
        {
            _output.text = message;
        }
    }
}
