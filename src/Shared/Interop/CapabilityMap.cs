using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.Interop;

/// <summary>How separately built Concerned Cat products call each other at
/// runtime, with no compile-time reference and no shared DLL (DECISIONS.md D7).
///
/// A provider plugin exposes one public instance property named
/// <see cref="PropertyName"/> of type <c>IReadOnlyDictionary&lt;string,
/// object&gt;</c>. Each value is a
/// <c>Func&lt;IReadOnlyDictionary&lt;string, string&gt;,
/// IReadOnlyDictionary&lt;string, string&gt;&gt;</c>, keyed by
/// <c>contractId/major</c>. Only mscorlib types cross, so the two assemblies
/// agree on every type even though each compiled this file separately - a
/// class or named delegate compiled into two products is two different CLR
/// types, and a static in one is invisible to the other.
///
/// Every endpoint catches its own exceptions and answers with a status; a
/// consumer wraps every call anyway and treats a failure as Unavailable.
/// </summary>
internal static class CapabilityMap
{
    public const string PropertyName = "ConcernedCatCapabilities";

    public static string KeyFor(string contractId, int major)
    {
        if (string.IsNullOrEmpty(contractId))
        {
            throw new ArgumentException("A contract id is required.", nameof(contractId));
        }

        if (major < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "Contract majors start at 1.");
        }

        return contractId + "/" + major.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The endpoint published for a contract major, or false when the
    /// map is missing, the key is absent or the value has another shape.
    /// </summary>
    public static bool TryGetEndpoint(
        IReadOnlyDictionary<string, object>? map,
        string contractId,
        int major,
        out Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? endpoint)
    {
        endpoint = null;
        if (map == null || !map.TryGetValue(KeyFor(contractId, major), out object? value))
        {
            return false;
        }

        endpoint = value as Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>;
        return endpoint != null;
    }

    /// <summary>Calls an endpoint and never throws: a provider exception, a null
    /// reply or a missing endpoint all come back as null, which a consumer
    /// treats as Unavailable.</summary>
    public static IReadOnlyDictionary<string, string>? TryCall(
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? endpoint,
        IReadOnlyDictionary<string, string> request)
    {
        if (endpoint == null)
        {
            return null;
        }

        try
        {
            return endpoint(request);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>How a consumer found a provider. Logged once per world session.
/// </summary>
internal enum CapabilityStatus
{
    Unspecified = 0,
    Available = 1,

    /// <summary>The provider plugin is not installed.</summary>
    Absent = 2,

    /// <summary>Installed, but older than the consumer's floor.</summary>
    VersionTooLow = 3,

    /// <summary>Installed, but the capability property is missing or has an
    /// unexpected shape.</summary>
    ProbeFailed = 4,

    /// <summary>The provider publishes no endpoint for this contract major.
    /// </summary>
    MajorMismatch = 5,
}

/// <summary>Reads and writes the string fields of one message. Enums travel as
/// their names, never as numbers, so a value added in a newer minor version is
/// recognisable as unknown instead of silently meaning something else.
/// </summary>
internal sealed class WireMessage
{
    private readonly Dictionary<string, string> _fields;

    private WireMessage(Dictionary<string, string> fields)
    {
        _fields = fields;
    }

    public static WireMessage Create() => new WireMessage(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>A defensive copy of what arrived. A null map reads as empty.
    /// </summary>
    public static WireMessage From(IReadOnlyDictionary<string, string>? wire)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (wire != null)
        {
            foreach (KeyValuePair<string, string> pair in wire)
            {
                if (pair.Key != null && pair.Value != null)
                {
                    fields[pair.Key] = pair.Value;
                }
            }
        }

        return new WireMessage(fields);
    }

    public int Count => _fields.Count;

    public IReadOnlyDictionary<string, string> ToWire() => new Dictionary<string, string>(_fields, StringComparer.Ordinal);

    public WireMessage Set(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A field key is required.", nameof(key));
        }

        _fields[key] = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    public WireMessage SetInt(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public WireMessage SetBool(string key, bool value) => Set(key, value ? "true" : "false");

    public WireMessage SetEnum<T>(string key, T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Only named values travel.");
        }

        return Set(key, value.ToString());
    }

    public WireMessage SetPoint(string key, float x, float y, float z)
    {
        return Set(
            key,
            x.ToString("R", CultureInfo.InvariantCulture) + ";" +
            y.ToString("R", CultureInfo.InvariantCulture) + ";" +
            z.ToString("R", CultureInfo.InvariantCulture));
    }

    public bool Has(string key) => _fields.ContainsKey(key);

    public bool TryGet(string key, out string value)
    {
        if (_fields.TryGetValue(key, out string? found) && found != null)
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public bool TryGetInt(string key, out int value)
    {
        value = 0;
        return TryGet(key, out string text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetBool(string key, out bool value)
    {
        value = false;
        if (!TryGet(key, out string text))
        {
            return false;
        }

        if (string.Equals(text, "true", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        return string.Equals(text, "false", StringComparison.Ordinal);
    }

    /// <summary>A defined name of <typeparamref name="T"/>, exactly. Numbers,
    /// unknown names, flags combinations and different casing are refused.
    /// </summary>
    public bool TryGetEnum<T>(string key, out T value) where T : struct, Enum
    {
        value = default;
        if (!TryGet(key, out string text) || text.Length == 0 || !IsName(text))
        {
            return false;
        }

        foreach (string name in Enum.GetNames(typeof(T)))
        {
            if (string.Equals(name, text, StringComparison.Ordinal))
            {
                value = (T)Enum.Parse(typeof(T), name);
                return true;
            }
        }

        return false;
    }

    public bool TryGetPoint(string key, out float x, out float y, out float z)
    {
        x = y = z = 0f;
        if (!TryGet(key, out string text))
        {
            return false;
        }

        string[] parts = text.Split(';');
        return parts.Length == 3 &&
            TryParseFinite(parts[0], out x) &&
            TryParseFinite(parts[1], out y) &&
            TryParseFinite(parts[2], out z);
    }

    private static bool IsName(string text)
    {
        if (!char.IsLetter(text[0]))
        {
            return false;
        }

        foreach (char character in text)
        {
            if (!char.IsLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseFinite(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        !float.IsNaN(value) && !float.IsInfinity(value);
}
