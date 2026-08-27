using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>A named atlas view preset: the query plus layer/cluster flags.
/// Views are user preferences (profile-level, not world data) and
/// re-evaluate on every apply, so a stale view can never hide entities
/// permanently.</summary>
internal sealed class SavedView
{
    public SavedView(string name, string query, bool showDirt, bool showPaved, bool showPins, bool clusterEnabled)
    {
        Name = name;
        Query = query;
        ShowDirt = showDirt;
        ShowPaved = showPaved;
        ShowPins = showPins;
        ClusterEnabled = clusterEnabled;
    }

    public string Name { get; }
    public string Query { get; }
    public bool ShowDirt { get; }
    public bool ShowPaved { get; }
    public bool ShowPins { get; }
    public bool ClusterEnabled { get; }
}

/// <summary>Escaped-TSV codec + ordered collection for saved views.</summary>
internal sealed class SavedViewStore
{
    public const string Header = "# ConcernedCartographer views v1";
    private const string RowMarker = "1";
    private const int FieldCount = 7;

    private readonly List<SavedView> _views = new();

    public IReadOnlyList<SavedView> Views => _views;
    public bool IsDirty { get; private set; }

    public void Save(SavedView view)
    {
        for (int index = 0; index < _views.Count; index++)
        {
            if (string.Equals(_views[index].Name, view.Name, StringComparison.OrdinalIgnoreCase))
            {
                _views[index] = view;
                IsDirty = true;
                return;
            }
        }

        _views.Add(view);
        IsDirty = true;
    }

    public bool TryGet(string name, out SavedView view)
    {
        foreach (SavedView candidate in _views)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                view = candidate;
                return true;
            }
        }

        view = null!;
        return false;
    }

    public bool Remove(string name)
    {
        for (int index = 0; index < _views.Count; index++)
        {
            if (string.Equals(_views[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _views.RemoveAt(index);
                IsDirty = true;
                return true;
            }
        }

        return false;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    public IEnumerable<string> Serialize()
    {
        yield return Header;
        foreach (SavedView view in _views)
        {
            yield return string.Join(
                "\t",
                AtlasText.Escape(view.Name),
                AtlasText.Escape(view.Query),
                view.ShowDirt ? "1" : "0",
                view.ShowPaved ? "1" : "0",
                view.ShowPins ? "1" : "0",
                view.ClusterEnabled ? "1" : "0",
                RowMarker);
        }
    }

    public static SavedViewStore Parse(IEnumerable<string> lines, out int malformedRows)
    {
        var store = new SavedViewStore();
        malformedRows = 0;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != FieldCount || parts[FieldCount - 1] != RowMarker ||
                !TryFlag(parts[2], out bool dirt) || !TryFlag(parts[3], out bool paved) ||
                !TryFlag(parts[4], out bool pins) || !TryFlag(parts[5], out bool cluster))
            {
                malformedRows++;
                continue;
            }

            string name = AtlasText.Unescape(parts[0]);
            if (name.Length == 0)
            {
                malformedRows++;
                continue;
            }

            store.Save(new SavedView(name, AtlasText.Unescape(parts[1]), dirt, paved, pins, cluster));
        }

        store.MarkClean();
        return store;
    }

    private static bool TryFlag(string value, out bool flag)
    {
        flag = value == "1";
        return value == "1" || value == "0";
    }
}
