using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Interop;

/// <summary>What the ledger knows about an arriving request id.</summary>
internal enum HaulRequestCheck
{
    Unspecified = 0,

    /// <summary>Never seen in this provider epoch.</summary>
    New = 1,

    /// <summary>Seen with the same payload, but it was not accepted then (it
    /// was refused or stale): nothing happened, so it is evaluated again.
    /// </summary>
    SamePayloadNotAccepted = 2,

    /// <summary>Seen with the same payload and accepted: a retry of something
    /// that already happened. Answered AlreadySatisfied, never applied twice.
    /// </summary>
    SamePayloadAccepted = 3,

    /// <summary>Seen with a different payload: a consumer bug or a collision.
    /// Answered Rejected, never applied.</summary>
    DifferentPayload = 4,
}

/// <summary>Request-id idempotence for the haul provider (CONTRACTS.md §3.1).
///
/// The haul service applies each command it is given exactly once; this ledger
/// is what makes a consumer's retry after a lost reply safe. It is keyed by the
/// consumer's request id and compares payload fingerprints, the way the tool
/// ledger compares payloads and unlike the old custody ledger that silently
/// kept the first payload.
///
/// In memory for one provider epoch: a world reload mints a new epoch, every
/// request based on the old one is stale anyway, and the ledger starts empty.
/// Bounded, oldest first, so a long session cannot grow it without limit; the
/// bound is far above the few dozen requests one order makes.</summary>
internal sealed class HaulRequestLedger
{
    public const int DefaultCapacity = 512;

    private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private readonly Queue<string> _arrival = new Queue<string>();

    public HaulRequestLedger(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "The ledger holds at least one request.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => _entries.Count;

    public Guid Epoch { get; private set; }

    /// <summary>Moves the ledger to the provider's current epoch, forgetting
    /// every request of the previous one.</summary>
    public void UseEpoch(Guid epoch)
    {
        if (epoch == Epoch)
        {
            return;
        }

        _entries.Clear();
        _arrival.Clear();
        Epoch = epoch;
    }

    public HaulRequestCheck Check(string requestId, string fingerprint)
    {
        if (string.IsNullOrEmpty(requestId) || fingerprint == null)
        {
            throw new ArgumentException("A request id and a fingerprint are required.");
        }

        if (!_entries.TryGetValue(requestId, out Entry? entry))
        {
            return HaulRequestCheck.New;
        }

        if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return HaulRequestCheck.DifferentPayload;
        }

        return entry.Accepted ? HaulRequestCheck.SamePayloadAccepted : HaulRequestCheck.SamePayloadNotAccepted;
    }

    /// <summary>Remembers a request id with its payload. A second record of
    /// the same id keeps the first payload.</summary>
    public void Record(string requestId, string fingerprint)
    {
        if (string.IsNullOrEmpty(requestId) || fingerprint == null)
        {
            throw new ArgumentException("A request id and a fingerprint are required.");
        }

        if (_entries.ContainsKey(requestId))
        {
            return;
        }

        _entries[requestId] = new Entry(fingerprint);
        _arrival.Enqueue(requestId);
        while (_entries.Count > Capacity && _arrival.Count > 0)
        {
            _entries.Remove(_arrival.Dequeue());
        }
    }

    public void MarkAccepted(string requestId)
    {
        if (!string.IsNullOrEmpty(requestId) && _entries.TryGetValue(requestId, out Entry? entry))
        {
            entry.Accepted = true;
        }
    }

    private sealed class Entry
    {
        public Entry(string fingerprint)
        {
            Fingerprint = fingerprint;
        }

        public string Fingerprint { get; }

        public bool Accepted { get; set; }
    }
}
