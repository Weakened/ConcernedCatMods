using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The in-memory pin table: durable identities, monotonic per-pin
/// revisions, durable-deletion tombstones, and a change stream that the
/// journal writer subscribes to. Pure — no game, IO, or Unity types.</summary>
internal sealed class PinStore
{
    private readonly Dictionary<Guid, AtlasPin> _pins = new();
    private readonly Func<DateTime> _clock;

    public PinStore(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public PinStore(IEnumerable<AtlasPin> pins, Func<DateTime>? clock = null)
        : this(clock)
    {
        foreach (AtlasPin pin in pins)
        {
            _pins[pin.Id.Value] = pin;
        }

        IsDirty = false;
    }

    /// <summary>Raised after every persisted-state change with the changed
    /// pin, so persistence can append a journal row immediately.</summary>
    public event Action<AtlasPin>? Changed;

    public bool IsDirty { get; private set; }

    public int Count => _pins.Count;

    /// <summary>All pins including tombstones and archived entries.</summary>
    public IEnumerable<AtlasPin> All => _pins.Values;

    /// <summary>Pins that exist from the player's point of view.</summary>
    public IEnumerable<AtlasPin> Living
    {
        get
        {
            foreach (AtlasPin pin in _pins.Values)
            {
                if (!pin.Deleted)
                {
                    yield return pin;
                }
            }
        }
    }

    public IEnumerable<AtlasPin> Tombstones
    {
        get
        {
            foreach (AtlasPin pin in _pins.Values)
            {
                if (pin.Deleted)
                {
                    yield return pin;
                }
            }
        }
    }

    public bool TryGet(AtlasId id, out AtlasPin pin)
    {
        return _pins.TryGetValue(id.Value, out pin!);
    }

    public AtlasPin Create(Action<AtlasPin>? initialize = null)
    {
        DateTime now = _clock();
        var pin = new AtlasPin(AtlasId.NewPin())
        {
            Revision = 1,
            CreatedUtc = now,
            ModifiedUtc = now,
        };
        initialize?.Invoke(pin);
        _pins[pin.Id.Value] = pin;
        Publish(pin);
        return pin;
    }

    /// <summary>In-place edit with revision bump. Identity never changes.</summary>
    public bool Mutate(AtlasId id, Action<AtlasPin> edit)
    {
        if (!_pins.TryGetValue(id.Value, out AtlasPin? pin))
        {
            return false;
        }

        edit(pin);
        pin.Revision++;
        pin.ModifiedUtc = _clock();
        Publish(pin);
        return true;
    }

    /// <summary>Durable deletion: the entity becomes a tombstone that keeps
    /// its identity and revision history so a deletion can never silently
    /// resurrect through sync or restore-by-accident.</summary>
    public bool Delete(AtlasId id)
    {
        return Mutate(id, pin =>
        {
            pin.Deleted = true;
            pin.DeletedUtc = _clock();
        });
    }

    public bool Restore(AtlasId id)
    {
        if (!_pins.TryGetValue(id.Value, out AtlasPin? pin) || !pin.Deleted)
        {
            return false;
        }

        return Mutate(id, restored =>
        {
            restored.Deleted = false;
            restored.DeletedUtc = null;
        });
    }

    /// <summary>Replay/sync entry: the incoming state wins only when its
    /// revision is strictly newer, so replays are idempotent and stale
    /// writers can never regress an entity.</summary>
    public bool Upsert(AtlasPin incoming)
    {
        if (_pins.TryGetValue(incoming.Id.Value, out AtlasPin? existing))
        {
            if (incoming.Revision <= existing.Revision)
            {
                return false;
            }

            existing.CopyFrom(incoming);
            Publish(existing);
            return true;
        }

        _pins[incoming.Id.Value] = incoming;
        Publish(incoming);
        return true;
    }

    /// <summary>Permanently removes tombstones older than the retention
    /// window. Living pins are never purged.</summary>
    public int PurgeTombstones(TimeSpan retention)
    {
        DateTime cutoff = _clock() - retention;
        var expired = new List<Guid>();
        foreach (AtlasPin pin in _pins.Values)
        {
            if (pin.Deleted && pin.DeletedUtc is DateTime deletedUtc && deletedUtc < cutoff)
            {
                expired.Add(pin.Id.Value);
            }
        }

        foreach (Guid key in expired)
        {
            _pins.Remove(key);
        }

        if (expired.Count > 0)
        {
            IsDirty = true;
        }

        return expired.Count;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    private void Publish(AtlasPin pin)
    {
        IsDirty = true;
        Changed?.Invoke(pin);
    }
}
