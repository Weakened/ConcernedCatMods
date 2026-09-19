using System;

namespace TheConcernedCat.ConcernedNPC.Roles;

/// <summary>Slug validation for the halves of an <see cref="NpcIdentity"/>.
///
/// <b>These rules are not new and must never drift.</b> They are character for
/// character the rules of <c>WorkSlug</c> in <c>src/Shared/Workers/WorkerKey.cs</c>,
/// which is the validator behind every <c>WorkerKey</c> a shipped product holds.
/// The two exist separately only because that one is <c>internal</c> source
/// compiled into each product's own assembly and this one lives in a library
/// assembly, so neither can see the other.
///
/// Keeping them identical is what makes conversion at the boundary
/// <b>total</b>: every <c>WorkerKey</c> a product already holds parses here, and
/// every identity registered here formats back into a <c>WorkerKey</c>. If these
/// rules were ever loosened, a role could register an identity its own product
/// cannot round-trip through its journal; if they were tightened, an already
/// shipped worker would stop being addressable. <c>NpcSlugAgreementTests</c>
/// links the shipped <c>WorkerKey.cs</c> into the test assembly and asserts the
/// two agree on every input, so the drift fails the build rather than a
/// player's save.</summary>
public static class NpcSlug
{
    /// <summary>The same ceiling <c>WorkSlug.MaxLength</c> uses.</summary>
    public const int MaxLength = 48;

    /// <summary>1 to <see cref="MaxLength"/> characters of <c>a-z</c>, <c>0-9</c>
    /// or <c>-</c>, without leading, trailing or doubled dashes.</summary>
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
            if (!allowed || (character == '-' && previous == '-'))
            {
                return false;
            }

            previous = character;
        }

        return true;
    }

    internal static string Require(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "An NPC identity is 1-" + MaxLength + " characters of a-z, 0-9 or '-', without " +
                "leading, trailing or doubled dashes. Received: '" + (value ?? "<null>") + "'.",
                parameterName);
        }

        return value!;
    }
}
