using System;

namespace TheConcernedCat.Companions.Identity;

/// <summary>Validation for the stable textual half of a companion identity.
///
/// Every product, companion, and quest identity is a bounded lowercase
/// kebab-case slug. Slugs are chosen once and then treated as data: they end
/// up in sidecar rows and in file names, so they must never contain a path
/// separator, a field separator, a case distinction, or anything a future
/// display-name change could perturb. Display names live in localization,
/// never here.</summary>
internal static class IdentitySlug
{
    /// <summary>Upper bound on a slug. Three slugs plus separators must stay
    /// far below any practical path limit once combined into a file name.</summary>
    public const int MaxLength = 48;

    /// <summary>True when <paramref name="value"/> is a legal identity slug:
    /// 1..<see cref="MaxLength"/> characters of <c>a-z</c>, <c>0-9</c>, or
    /// <c>-</c>, neither starting nor ending with <c>-</c>, and never
    /// containing two consecutive dashes.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.Length > MaxLength)
        {
            return false;
        }

        if (value[0] == '-' || value[value.Length - 1] == '-')
        {
            return false;
        }

        char previous = '\0';
        foreach (char character in value)
        {
            bool allowed = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '-';
            if (!allowed)
            {
                return false;
            }

            if (character == '-' && previous == '-')
            {
                return false;
            }

            previous = character;
        }

        return true;
    }

    /// <summary>Throws with an actionable message when a slug is malformed.
    /// Used by the identity constructors, which are only ever called with
    /// authored constants, so a failure is a development error rather than a
    /// runtime condition to recover from.</summary>
    public static string Require(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "Identity slugs must be 1-" + MaxLength + " characters of a-z, 0-9 or '-', " +
                "without leading, trailing, or doubled dashes. Received: '" + (value ?? "<null>") + "'.",
                parameterName);
        }

        return value!;
    }
}
