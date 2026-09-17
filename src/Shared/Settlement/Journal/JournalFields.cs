using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Storage;

namespace TheConcernedCat.Settlement.Journal;

/// <summary>The named fields of one schema-v3 custody row, in the order they
/// were written.
///
/// <b>Why named fields rather than columns.</b> The v1/v2 rows are positional,
/// and the entry row already paid for that: its variable-length stack list sits
/// at the end, so nothing could ever be added after it. The custody kinds carry
/// very different payloads — a whole order definition, a transfer intent, a
/// list of traced drops — and a positional layout per kind would make every
/// later addition a schema bump. <c>key=value</c> fields let a reader name what
/// is missing when a row is damaged, and let a row carry a field this build does
/// not know without the reader dropping it: unknown fields are kept and written
/// back unchanged, which is what "preserve forward data" asks for.
///
/// Values are escaped with <see cref="AtomicTextFile.Escape"/>, so they can
/// hold tabs, stars, line breaks and <c>=</c>; the key ends at the first
/// <c>=</c>.</summary>
internal sealed class JournalFields
{
    private readonly List<KeyValuePair<string, string>> _fields = new List<KeyValuePair<string, string>>();

    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _fields;

    public int Count => _fields.Count;

    public JournalFields Add(string key, string? value)
    {
        if (string.IsNullOrEmpty(key) || key.IndexOf('=') >= 0 || key.IndexOf('\t') >= 0)
        {
            throw new ArgumentException("A journal field needs a plain key.", nameof(key));
        }

        _fields.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
        return this;
    }

    public JournalFields Add(string key, int value) =>
        Add(key, value.ToString(CultureInfo.InvariantCulture));

    public JournalFields Add(string key, long value) =>
        Add(key, value.ToString(CultureInfo.InvariantCulture));

    public JournalFields Add(string key, float value) =>
        Add(key, value.ToString("R", CultureInfo.InvariantCulture));

    public JournalFields Add(string key, Guid value) =>
        Add(key, value == Guid.Empty ? string.Empty : value.ToString("N", CultureInfo.InvariantCulture));

    public JournalFields AddName<TEnum>(string key, TEnum value)
        where TEnum : struct
    {
        return Add(key, value.ToString());
    }

    /// <summary>The first value for a key, or false. A key written twice is
    /// damage the parser refuses before anything reads it.</summary>
    public bool TryGet(string key, out string value)
    {
        foreach (KeyValuePair<string, string> pair in _fields)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal))
            {
                value = pair.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public bool Has(string key) => TryGet(key, out _);

    public bool TryGetText(string key, out string value) => TryGet(key, out value);

    /// <summary>A required non-empty value.</summary>
    public bool TryGetRequired(string key, out string value) => TryGet(key, out value) && value.Length > 0;

    public bool TryGetInt(string key, out int value)
    {
        value = 0;
        return TryGet(key, out string text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetLong(string key, out long value)
    {
        value = 0L;
        return TryGet(key, out string text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>A finite float. NaN and infinity are damage: a position or a
    /// radius that is not a number cannot have been written by this build.
    /// </summary>
    public bool TryGetFloat(string key, out float value)
    {
        value = 0f;
        return TryGet(key, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value)
            && !float.IsInfinity(value);
    }

    /// <summary>A GUID, where an empty value means <see cref="Guid.Empty"/>.
    /// </summary>
    public bool TryGetGuid(string key, out Guid value)
    {
        value = Guid.Empty;
        if (!TryGet(key, out string text))
        {
            return false;
        }

        return text.Length == 0
            || Guid.TryParseExact(text, "N", out value);
    }

    /// <summary>An enum by its exact defined name. A number, an unknown name or
    /// a different case is damage: names are what travel, so that renumbering
    /// a member can never silently change what an old row means.</summary>
    public bool TryGetName<TEnum>(string key, out TEnum value)
        where TEnum : struct
    {
        value = default;
        if (!TryGet(key, out string text) || text.Length == 0 || char.IsDigit(text[0]) || text[0] == '-')
        {
            return false;
        }

        if (!Enum.TryParse(text, ignoreCase: false, out value))
        {
            return false;
        }

        return Enum.IsDefined(typeof(TEnum), value)
            && string.Equals(value.ToString(), text, StringComparison.Ordinal);
    }

    /// <summary>Encodes every field as <c>key=escaped value</c>.</summary>
    public IEnumerable<string> Encode()
    {
        foreach (KeyValuePair<string, string> pair in _fields)
        {
            yield return pair.Key + "=" + AtomicTextFile.Escape(pair.Value);
        }
    }

    /// <summary>Decodes fields from a row, starting at <paramref name="start"/>.
    /// False when a field has no key or a key repeats: both are damage, and
    /// guessing which duplicate was meant would be inventing data.</summary>
    public static bool TryDecode(string[] columns, int start, out JournalFields fields)
    {
        fields = new JournalFields();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int index = start; index < columns.Length; index++)
        {
            string column = columns[index];
            int equals = column.IndexOf('=');
            if (equals <= 0)
            {
                return false;
            }

            string key = column.Substring(0, equals);
            if (!seen.Add(key))
            {
                return false;
            }

            fields._fields.Add(new KeyValuePair<string, string>(
                key, AtomicTextFile.Unescape(column.Substring(equals + 1))));
        }

        return true;
    }
}
