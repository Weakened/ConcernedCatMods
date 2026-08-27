using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Deterministic pin search: plain words match name, notes, tags,
/// and category case-insensitively; power tokens narrow specific fields.
/// Supported tokens: name:, category:, tag:, icon:, status:, scope:,
/// source:, is:checked|unchecked|archived|deleted, near:x,z,radius.
/// A malformed token degrades to a plain word — an invalid query can never
/// hide data permanently because filters are display-only and saved views
/// re-evaluate on every use.</summary>
internal sealed class PinQuery
{
    private readonly List<string> _words = new();
    private readonly List<(string Field, string Value)> _tokens = new();
    private float _nearX;
    private float _nearZ;
    private float _nearRadius = -1f;

    public string Text { get; }

    private PinQuery(string text)
    {
        Text = text;
    }

    public static PinQuery Parse(string? text)
    {
        var query = new PinQuery((text ?? "").Trim());
        foreach (string rawPart in query.Text.Split(' '))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            int colon = part.IndexOf(':');
            if (colon > 0 && colon < part.Length - 1)
            {
                string field = part.Substring(0, colon).ToLowerInvariant();
                string value = part.Substring(colon + 1);
                if (field == "near")
                {
                    string[] coords = value.Split(',');
                    if (coords.Length == 3 &&
                        float.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                        float.TryParse(coords[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) &&
                        radius > 0f)
                    {
                        query._nearX = x;
                        query._nearZ = z;
                        query._nearRadius = radius;
                        continue;
                    }
                }
                else if (IsKnownField(field))
                {
                    query._tokens.Add((field, value.ToLowerInvariant()));
                    continue;
                }
            }

            query._words.Add(part.ToLowerInvariant());
        }

        return query;
    }

    public bool IsEmpty => _words.Count == 0 && _tokens.Count == 0 && _nearRadius <= 0f;

    public bool Matches(AtlasPin pin)
    {
        if (_nearRadius > 0f &&
            pin.Position.HorizontalDistanceTo(new RoadPoint(_nearX, 0f, _nearZ)) > _nearRadius)
        {
            return false;
        }

        foreach ((string field, string value) in _tokens)
        {
            if (!MatchesToken(pin, field, value))
            {
                return false;
            }
        }

        foreach (string word in _words)
        {
            if (!MatchesWord(pin, word))
            {
                return false;
            }
        }

        return true;
    }

    public List<AtlasPin> Filter(IEnumerable<AtlasPin> pins)
    {
        var results = new List<AtlasPin>();
        foreach (AtlasPin pin in pins)
        {
            if (Matches(pin))
            {
                results.Add(pin);
            }
        }

        return results;
    }

    private static bool IsKnownField(string field)
    {
        switch (field)
        {
            case "name":
            case "category":
            case "tag":
            case "icon":
            case "status":
            case "scope":
            case "source":
            case "is":
                return true;
            default:
                return false;
        }
    }

    private static bool MatchesToken(AtlasPin pin, string field, string value)
    {
        switch (field)
        {
            case "name":
                return pin.Name.ToLowerInvariant().Contains(value);
            case "category":
                return pin.Category.ToLowerInvariant().Contains(value);
            case "icon":
                return pin.IconId.ToLowerInvariant().Contains(value);
            case "status":
                return pin.Status.ToString().ToLowerInvariant() == value;
            case "scope":
                return pin.Scope.ToString().ToLowerInvariant() == value;
            case "source":
                return pin.Source.ToString().ToLowerInvariant() == value;
            case "tag":
                foreach (string tag in pin.Tags)
                {
                    if (tag.ToLowerInvariant().Contains(value))
                    {
                        return true;
                    }
                }

                return false;
            case "is":
                switch (value)
                {
                    case "checked":
                        return pin.Checked;
                    case "unchecked":
                        return !pin.Checked;
                    case "archived":
                        return pin.Archived;
                    case "deleted":
                        return pin.Deleted;
                    default:
                        return false;
                }

            default:
                return false;
        }
    }

    private static bool MatchesWord(AtlasPin pin, string word)
    {
        if (pin.Name.ToLowerInvariant().Contains(word) ||
            pin.Notes.ToLowerInvariant().Contains(word) ||
            pin.Category.ToLowerInvariant().Contains(word))
        {
            return true;
        }

        foreach (string tag in pin.Tags)
        {
            if (tag.ToLowerInvariant().Contains(word))
            {
                return true;
            }
        }

        return false;
    }
}
