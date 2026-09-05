using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Net;

/// <summary>Single-shot logging gate (CT-029): the first time a key is seen
/// it returns true (log it), every repeat returns false. Keeps a hardened
/// input path from spamming the log once per frame when a remote source
/// keeps sending the same bad value. Bounded — past its cap it stops
/// admitting new keys (returns false) rather than growing without limit, so
/// a flood of distinct bad keys cannot exhaust memory either. Pure and
/// deterministic; the caller owns the actual log sink.</summary>
public sealed class OncePerKeyGate
{
    public const int DefaultCapacity = 512;

    private readonly HashSet<string> _seen = new();
    private readonly int _capacity;

    public OncePerKeyGate(int capacity = DefaultCapacity)
    {
        _capacity = capacity < 1 ? 1 : capacity;
    }

    public int Count => _seen.Count;

    /// <summary>True exactly once per distinct key. Returns false for a
    /// repeat, and false once the capacity is reached for an unseen key
    /// (bounded — a new key past the cap is silently suppressed, never
    /// admitted, so this can neither spam nor grow unbounded).</summary>
    public bool ShouldLog(string key)
    {
        if (_seen.Contains(key))
        {
            return false;
        }

        if (_seen.Count >= _capacity)
        {
            return false;
        }

        _seen.Add(key);
        return true;
    }

    /// <summary>Forgets all keys — used on world switch so the next world
    /// starts with a clean single-shot slate.</summary>
    public void Reset()
    {
        _seen.Clear();
    }
}
