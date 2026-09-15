using System;
using System.Globalization;

namespace TheConcernedCat.Settlement.Identity;

/// <summary>Validation for every identity in this layer.
///
/// Deliberately duplicated rather than shared with the companion layer's
/// equivalent. The two shared areas are adopted independently — a product may
/// want settlements and no companion, or the reverse — and a cross-reference
/// between them would quietly turn one `&lt;Compile Include&gt;` line into two.
/// Fifty lines is a cheaper price than that coupling.</summary>
internal static class SettlementSlug
{
    public const int MaxLength = 48;

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

    public static string Require(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "Settlement identities must be 1-" + MaxLength + " characters of a-z, 0-9 or '-', " +
                "without leading, trailing or doubled dashes. Received: '" + (value ?? "<null>") + "'.",
                parameterName);
        }

        return value!;
    }
}

/// <summary>A settlement the player designated. Separate from the world so one
/// world can hold more than one and they stay isolated.</summary>
internal readonly struct SettlementId : IEquatable<SettlementId>
{
    public SettlementId(string value)
    {
        Value = SettlementSlug.Require(value, nameof(value));
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public bool Equals(SettlementId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is SettlementId other && Equals(other);
    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value ?? "<empty>";
}

/// <summary>One unit of work the player approved: build this cottage here.</summary>
internal readonly struct OrderId : IEquatable<OrderId>
{
    public OrderId(string value)
    {
        Value = SettlementSlug.Require(value, nameof(value));
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public bool Equals(OrderId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is OrderId other && Equals(other);
    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value ?? "<empty>";
}

/// <summary>One actor doing the work. Deliberately not the same thing as a
/// resident: a worker is a role and a resident is a person who lives
/// somewhere, and #273 is explicit that named specialists and ordinary
/// labourers are different roles.</summary>
internal readonly struct WorkerId : IEquatable<WorkerId>
{
    public WorkerId(string value)
    {
        Value = SettlementSlug.Require(value, nameof(value));
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public bool Equals(WorkerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is WorkerId other && Equals(other);
    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value ?? "<empty>";
}

/// <summary>Somebody who lives in a finished building.</summary>
internal readonly struct ResidentId : IEquatable<ResidentId>
{
    public ResidentId(string value)
    {
        Value = SettlementSlug.Require(value, nameof(value));
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public bool Equals(ResidentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ResidentId other && Equals(other);
    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value ?? "<empty>";
}

/// <summary>The identity of one attempt to change something.
///
/// This is the type the whole conservation argument rests on. Every write that
/// moves material carries one, every journal entry records it, and every
/// operation keyed by one is idempotent — so a retry after a crash, a
/// reconnect or an ownership change repeats the <i>request</i> rather than the
/// <i>effect</i>. Without it, "did that reservation already happen?" has no
/// answer and the only safe behaviours are to lose materials or to duplicate
/// them.</summary>
internal readonly struct RequestId : IEquatable<RequestId>
{
    public RequestId(string value)
    {
        Value = SettlementSlug.Require(value, nameof(value));
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Builds a request id from an order and a monotonically
    /// increasing step. Deterministic on purpose: the same step of the same
    /// order produces the same id after a restart, which is what makes a
    /// replayed attempt recognisable as the same attempt.</summary>
    public static RequestId For(OrderId order, int step)
    {
        if (order.IsEmpty)
        {
            throw new ArgumentException("A request needs an owning order.", nameof(order));
        }

        if (step < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "Request steps start at zero.");
        }

        return new RequestId(order.Value + "-" + step.ToString(CultureInfo.InvariantCulture));
    }

    public bool Equals(RequestId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is RequestId other && Equals(other);
    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value ?? "<empty>";
}

/// <summary>Which world and which settlement a journal belongs to.
///
/// Journals are addressed by scope for the same reason companion sidecars are:
/// two settlements in two worlds must not be able to read or overwrite each
/// other, and a copied profile must be recognised rather than merged.</summary>
internal readonly struct SettlementScope : IEquatable<SettlementScope>
{
    public SettlementScope(long worldId, SettlementId settlement)
    {
        WorldId = worldId;
        Settlement = settlement;
    }

    public long WorldId { get; }

    public SettlementId Settlement { get; }

    public bool IsComplete => WorldId != 0L && !Settlement.IsEmpty;

    public string ToStorageKey()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException(
                "A settlement storage key requires a world and a settlement.");
        }

        return unchecked((ulong)WorldId).ToString("x16", CultureInfo.InvariantCulture)
            + "." + Settlement.Value;
    }

    public bool Equals(SettlementScope other)
    {
        return WorldId == other.WorldId && Settlement.Equals(other.Settlement);
    }

    public override bool Equals(object? obj) => obj is SettlementScope other && Equals(other);

    public override int GetHashCode()
    {
        return (WorldId.GetHashCode() * 397) ^ Settlement.GetHashCode();
    }

    public override string ToString()
    {
        return IsComplete ? ToStorageKey() : "<incomplete settlement scope>";
    }
}
