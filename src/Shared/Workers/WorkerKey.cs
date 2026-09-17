using System;

namespace TheConcernedCat.Workers;

/// <summary>Slug validation for worker identities. Deliberately duplicated from
/// the settlement and companion layers: shared areas are adopted independently,
/// and a cross-reference between them would turn one <c>&lt;Compile
/// Include&gt;</c> line into two.</summary>
internal static class WorkSlug
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
                "Worker identities must be 1-" + MaxLength + " characters of a-z, 0-9 or '-', without " +
                "leading, trailing or doubled dashes. Received: '" + (value ?? "<null>") + "'.",
                parameterName);
        }

        return value!;
    }
}

/// <summary>Which worker, across products: the product whose opted-in worker
/// runtime owns the body, and the worker's stable slug. <c>foreman/thorstein</c>,
/// <c>teamster/gunnar</c>.
///
/// Stable across sessions and reloads, unlike anything the game assigns: a ZDO
/// id is renumbered every time a world loads, so it can never be a worker's
/// identity.</summary>
internal readonly struct WorkerKey : IEquatable<WorkerKey>
{
    public const string ForemanProduct = "foreman";
    public const string TeamsterProduct = "teamster";

    public WorkerKey(string product, string worker)
    {
        Product = WorkSlug.Require(product, nameof(product));
        Worker = WorkSlug.Require(worker, nameof(worker));
    }

    /// <summary>The Foreman: collects and owns collection orders.</summary>
    public static WorkerKey Thorstein => new WorkerKey(ForemanProduct, "thorstein");

    /// <summary>The Teamster: hauls an assigned cart.</summary>
    public static WorkerKey Gunnar => new WorkerKey(TeamsterProduct, "gunnar");

    public string Product { get; }

    public string Worker { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Worker);

    /// <summary><c>product/worker</c>, the form carried in journals and across
    /// the capability boundary.</summary>
    public string Value => IsEmpty ? string.Empty : Product + "/" + Worker;

    public static bool TryParse(string? text, out WorkerKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int slash = text!.IndexOf('/');
        if (slash <= 0 || slash != text.LastIndexOf('/') || slash == text.Length - 1)
        {
            return false;
        }

        string product = text.Substring(0, slash);
        string worker = text.Substring(slash + 1);
        if (!WorkSlug.IsValid(product) || !WorkSlug.IsValid(worker))
        {
            return false;
        }

        key = new WorkerKey(product, worker);
        return true;
    }

    public bool Equals(WorkerKey other) =>
        string.Equals(Product, other.Product, StringComparison.Ordinal) &&
        string.Equals(Worker, other.Worker, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is WorkerKey other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => IsEmpty ? "<empty>" : Value;
}
