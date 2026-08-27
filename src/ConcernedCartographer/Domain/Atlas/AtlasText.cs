using System;
using System.Collections.Generic;
using System.Text;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Escaping for free-text fields inside line/tab-delimited sidecar
/// files: percent-encodes only the delimiter-dangerous characters, so
/// notes and names may contain tabs, newlines, and percent signs without
/// corrupting rows.</summary>
internal static class AtlasText
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(value!.Length);
        foreach (char character in value)
        {
            switch (character)
            {
                case '%':
                    builder.Append("%25");
                    break;
                case '\t':
                    builder.Append("%09");
                    break;
                case '\n':
                    builder.Append("%0A");
                    break;
                case '\r':
                    builder.Append("%0D");
                    break;
                case ',':
                    builder.Append("%2C");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    public static string Unescape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(value!.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '%' && index + 2 < value.Length &&
                TryParseHex(value[index + 1], value[index + 2], out char decoded))
            {
                builder.Append(decoded);
                index += 2;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Display sanitization for strings that came off the network:
    /// strips rich-text markup and control characters and caps the length,
    /// so a hostile author name cannot disrupt HUD rendering.</summary>
    public static string SanitizeDisplay(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(Math.Min(value!.Length, maxLength));
        bool inTag = false;
        foreach (char character in value)
        {
            if (character == '<')
            {
                inTag = true;
                continue;
            }

            if (character == '>')
            {
                inTag = false;
                continue;
            }

            if (inTag || char.IsControl(character))
            {
                continue;
            }

            builder.Append(character);
            if (builder.Length >= maxLength)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    public static string JoinTags(IEnumerable<string> tags)
    {
        var parts = new List<string>();
        foreach (string tag in tags)
        {
            string trimmed = tag.Trim();
            if (trimmed.Length > 0)
            {
                parts.Add(Escape(trimmed));
            }
        }

        return string.Join(",", parts);
    }

    public static List<string> SplitTags(string? joined)
    {
        var tags = new List<string>();
        if (string.IsNullOrEmpty(joined))
        {
            return tags;
        }

        foreach (string part in joined!.Split(','))
        {
            string tag = Unescape(part).Trim();
            if (tag.Length > 0 && !tags.Contains(tag))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static bool TryParseHex(char high, char low, out char decoded)
    {
        int highValue = HexValue(high);
        int lowValue = HexValue(low);
        if (highValue < 0 || lowValue < 0)
        {
            decoded = default;
            return false;
        }

        decoded = (char)((highValue << 4) | lowValue);
        return true;
    }

    private static int HexValue(char character)
    {
        if (character >= '0' && character <= '9')
        {
            return character - '0';
        }

        if (character >= 'A' && character <= 'F')
        {
            return character - 'A' + 10;
        }

        if (character >= 'a' && character <= 'f')
        {
            return character - 'a' + 10;
        }

        return -1;
    }
}
