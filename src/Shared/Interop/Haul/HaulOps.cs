using System;
using TheConcernedCat.Workers;

namespace TheConcernedCat.Interop.Haul;

/// <summary>The ops of <c>concernedcat.haul/1</c> as a closed set, so both
/// sides can switch on them. Never on the wire: an op travels as its
/// camelCase string in <see cref="HaulContract.Ops"/>, and a string that is not
/// one of those is an unknown op, not a guess.</summary>
internal enum HaulOp
{
    Unspecified = 0,
    Hello = 1,
    DescribeLease = 2,
    RequestHaul = 3,
    GetHaul = 4,
    AcknowledgeWait = 5,
    CancelHaul = 6,
}

internal static class HaulOps
{
    public static string WireName(HaulOp op)
    {
        switch (op)
        {
            case HaulOp.Hello:
                return HaulContract.Ops.Hello;
            case HaulOp.DescribeLease:
                return HaulContract.Ops.DescribeLease;
            case HaulOp.RequestHaul:
                return HaulContract.Ops.RequestHaul;
            case HaulOp.GetHaul:
                return HaulContract.Ops.GetHaul;
            case HaulOp.AcknowledgeWait:
                return HaulContract.Ops.AcknowledgeWait;
            case HaulOp.CancelHaul:
                return HaulContract.Ops.CancelHaul;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), "Only real ops travel.");
        }
    }

    /// <summary>Exact, ordinal: <c>RequestHaul</c> or <c>requesthaul</c> is an
    /// unknown op.</summary>
    public static bool TryParse(string? name, out HaulOp op)
    {
        switch (name)
        {
            case HaulContract.Ops.Hello:
                op = HaulOp.Hello;
                return true;
            case HaulContract.Ops.DescribeLease:
                op = HaulOp.DescribeLease;
                return true;
            case HaulContract.Ops.RequestHaul:
                op = HaulOp.RequestHaul;
                return true;
            case HaulContract.Ops.GetHaul:
                op = HaulOp.GetHaul;
                return true;
            case HaulContract.Ops.AcknowledgeWait:
                op = HaulOp.AcknowledgeWait;
                return true;
            case HaulContract.Ops.CancelHaul:
                op = HaulOp.CancelHaul;
                return true;
            default:
                op = HaulOp.Unspecified;
                return false;
        }
    }

    /// <summary>The ops that carry a request id, the provider epoch and an
    /// expected revision, and that the provider answers idempotently.</summary>
    public static bool IsMutating(HaulOp op) =>
        op == HaulOp.RequestHaul || op == HaulOp.AcknowledgeWait || op == HaulOp.CancelHaul;
}

/// <summary>The provider epoch as it travels: the provider's world-load epoch
/// in the 32-hex-digit form. A world reload on the provider mints a new one,
/// and every request based on the old one is answered Stale, because a haul, a
/// lease or a cart key from another world load names nothing.</summary>
internal static class HaulEpoch
{
    public static string Format(Guid epoch) => epoch.ToString("N");

    public static bool TryParse(string? text, out Guid epoch)
    {
        epoch = Guid.Empty;
        return text != null && text.Length == 32 && Guid.TryParseExact(text, "N", out epoch);
    }
}

/// <summary>Shape rules for free-text fields, shared by both sides so a
/// request the consumer builds is exactly a request the provider accepts.
/// Ids that name settlement things (orders, hauls, request attempts) are the
/// worker slug form; ids the provider mints (leases, cart session keys) are
/// opaque tokens with a length bound.</summary>
internal static class HaulFieldRules
{
    public const int MaxTokenLength = 64;

    public const int MaxVersionLength = 32;

    /// <summary>Details are for a person reading a log or a panel; the provider
    /// truncates, never refuses, a long one.</summary>
    public const int MaxDetailLength = 200;

    public static bool IsSlug(string? value) => WorkSlug.IsValid(value);

    /// <summary>1 to <paramref name="maxLength"/> characters, none of them
    /// whitespace or control characters.</summary>
    public static bool IsToken(string? value, int maxLength = MaxTokenLength)
    {
        if (string.IsNullOrEmpty(value) || value!.Length > maxLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static string TrimDetail(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return string.Empty;
        }

        string text = detail!.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= MaxDetailLength ? text : text.Substring(0, MaxDetailLength);
    }
}
