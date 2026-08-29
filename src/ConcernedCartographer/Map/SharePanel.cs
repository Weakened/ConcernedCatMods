using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Share side panel (#102): the collaborative atlas without
/// the console. Shows what is scoped for sharing, broadcasts with one
/// click, lists inbox envelopes by sender, previews exactly what an
/// apply would change (including the names of anything it would DELETE),
/// and applies with an explicit Mine/Theirs conflict choice. Private
/// stays private; tombstone no-resurrection is untouched — this is a
/// veneer over the same tested `cc_sync` paths.</summary>
internal sealed class SharePanel : CcSidePanel
{
    private const int InboxSlots = 3;

    private readonly Func<string[], string> _execute;
    private readonly Func<List<string>> _inboxAuthors;
    private Text? _status;
    private Text? _output;
    private readonly GameObject[] _rows = new GameObject[InboxSlots];
    private readonly Text[] _rowLabels = new Text[InboxSlots];
    private readonly string[] _rowAuthors = new string[InboxSlots];
    private string _selectedAuthor = "";

    public SharePanel(ManualLogSource log, Func<string[], string> execute, Func<List<string>> inboxAuthors)
        : base(log, "share.title", 384f, 560f)
    {
        _execute = execute;
        _inboxAuthors = inboxAuthors;
    }

    protected override void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y)
    {
        _status = AddBody(gui, font, "", 12, Color.white, ref y, 60f);

        float left = -(Width - 44f) / 2f;
        AddButton(gui, AtlasStrings.Get("share.now"), left + 80f, y, 160f, 28f, () =>
        {
            Report(_execute(new[] { "share" }));
            RefreshStatus();
        });
        AddButton(gui, "Clear inbox", left + 250f, y, 130f, 28f, () =>
        {
            Report(_execute(new[] { "clear" }));
            _selectedAuthor = "";
            RefreshStatus();
        });
        y -= 36f;

        for (int index = 0; index < InboxSlots; index++)
        {
            int captured = index;
            GameObject row = gui.CreateButton("", Panel!.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), Width - 44f, 24f);
            _rows[index] = row;
            _rowLabels[index] = row.GetComponentInChildren<Text>();
            row.GetComponent<Button>().onClick.AddListener(() =>
            {
                _selectedAuthor = _rowAuthors[captured];
                Report(_execute(new[] { "preview", _selectedAuthor }));
                RefreshStatus();
            });
            row.SetActive(false);
            y -= 28f;
        }

        y -= 8f;
        AddButton(gui, "Apply (keep mine)", left + 92f, y, 184f, 28f, () => Apply("mine"));
        AddButton(gui, "Apply (take theirs)", left + 284f, y, 184f, 28f, () => Apply("theirs"));
        y -= 36f;

        _output = AddBody(gui, font, "", 11, Color.white, ref y, 190f);
    }

    protected override void OnShown()
    {
        _selectedAuthor = "";
        Report("Select an inbox entry to preview it. Nothing is ever applied automatically.");
        RefreshStatus();
    }

    private void Apply(string side)
    {
        if (_selectedAuthor.Length == 0)
        {
            Report("Preview an inbox entry first — Apply always follows a preview.");
            return;
        }

        Report(_execute(new[] { "apply", _selectedAuthor, side }));
        _selectedAuthor = "";
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_status != null)
        {
            _status.text = _execute(new[] { "status" });
        }

        List<string> authors = _inboxAuthors();
        for (int index = 0; index < InboxSlots; index++)
        {
            bool used = index < authors.Count;
            _rows[index].SetActive(used);
            if (used)
            {
                _rowAuthors[index] = authors[index];
                bool selected = authors[index] == _selectedAuthor;
                _rowLabels[index].text = (selected ? "» " : "") + $"Inbox: {authors[index]}";
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
