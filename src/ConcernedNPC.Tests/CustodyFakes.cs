using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>An inventory a test can break in exactly one way at a time.
///
/// The faults are the ones an engine inventory really produces: it goes away
/// mid-transfer, it refuses to be counted, it throws, it accepts fewer than it
/// said it would, and it silently swallows items. Each of them has to produce
/// a different honest answer, and none of them may lose material quietly.
/// </summary>
internal sealed class FakeInventory : INpcInventoryPort
{
    private readonly Dictionary<NpcMaterial, int> _counts = new Dictionary<NpcMaterial, int>();

    internal FakeInventory(string describe)
    {
        Describe = describe;
    }

    public string Describe { get; }

    public bool IsAvailable { get; set; } = true;

    /// <summary>Room, or null for "as much as you like".</summary>
    internal int? Room { get; set; }

    internal bool ThrowOnCount { get; set; }

    internal bool ThrowOnAdd { get; set; }

    internal bool ThrowOnRemove { get; set; }

    internal bool CountIsNegative { get; set; }

    /// <summary>Accepts this many fewer than asked, without saying so. The
    /// engine's add returning a number the inventory does not honour.</summary>
    internal int SwallowOnAdd { get; set; }

    /// <summary>Removes nothing while claiming to. The duplicate case: the
    /// destination gained and the source kept.</summary>
    internal bool RefuseToRemove { get; set; }

    internal int Adds { get; private set; }

    internal int Removes { get; private set; }

    /// <summary>What the source held at the moment the destination's add was
    /// called - the ordering proof: it must still be everything.</summary>
    internal Func<int>? WatchDuringAdd { get; set; }

    internal int Observed { get; private set; } = -1;

    internal FakeInventory With(NpcMaterial material, int count)
    {
        _counts[material] = count;
        return this;
    }

    public int Count(NpcMaterial material)
    {
        if (ThrowOnCount)
        {
            throw new InvalidOperationException("the inventory could not be read");
        }

        if (CountIsNegative)
        {
            return -1;
        }

        return _counts.TryGetValue(material, out int count) ? count : 0;
    }

    public int CanAccept(NpcMaterial material, int count) => Room.HasValue ? Math.Min(Room.Value, count) : count;

    public int Add(NpcMaterial material, int count)
    {
        Adds++;
        if (WatchDuringAdd != null)
        {
            Observed = WatchDuringAdd();
        }

        if (ThrowOnAdd)
        {
            throw new InvalidOperationException("the add failed");
        }

        int accepted = Math.Max(0, count - SwallowOnAdd);
        _counts.TryGetValue(material, out int held);
        _counts[material] = held + accepted;
        return count;
    }

    public int Remove(NpcMaterial material, int count)
    {
        Removes++;
        if (ThrowOnRemove)
        {
            throw new InvalidOperationException("the remove failed");
        }

        if (RefuseToRemove)
        {
            return 0;
        }

        _counts.TryGetValue(material, out int held);
        int removed = Math.Min(held, count);
        _counts[material] = held - removed;
        return removed;
    }
}

/// <summary>A record of what was written, and a switch for refusing to write
/// it.</summary>
internal sealed class FakeJournal : INpcCustodyJournal
{
    internal List<string> Written { get; } = new List<string>();

    public bool IsWritable
    {
        get
        {
            if (ThrowOnWritable)
            {
                throw new InvalidOperationException("the record could not be opened");
            }

            return Writable;
        }
    }

    internal bool Writable { get; set; } = true;

    internal bool RefuseIntent { get; set; }

    internal bool RefuseReceipt { get; set; }

    internal bool ThrowOnIntent { get; set; }

    internal bool ThrowOnWritable { get; set; }

    public bool TryRecordIntent(NpcTransferIntent intent)
    {
        if (ThrowOnIntent)
        {
            throw new InvalidOperationException("the record could not be opened");
        }

        if (RefuseIntent)
        {
            return false;
        }

        Written.Add("intent " + intent.Request.Value);
        return true;
    }

    public bool TryRecordReceipt(NpcTransferIntent intent, NpcTransferReceipt receipt)
    {
        if (RefuseReceipt)
        {
            return false;
        }

        Written.Add("receipt " + receipt.Request.Value + " " + receipt.Outcome);
        return true;
    }
}

internal sealed class FakeObserver : INpcCustodyObserver
{
    private readonly Dictionary<string, int?> _counts = new Dictionary<string, int?>(StringComparer.Ordinal);

    internal bool Throws { get; set; }

    internal FakeObserver Sees(NpcCustodyLocation location, NpcMaterial material, int? count)
    {
        _counts[Key(location, material)] = count;
        return this;
    }

    public int? Observe(NpcCustodyLocation location, NpcMaterial material)
    {
        if (Throws)
        {
            throw new InvalidOperationException("nothing could be looked at");
        }

        return _counts.TryGetValue(Key(location, material), out int? count) ? count : null;
    }

    private static string Key(NpcCustodyLocation location, NpcMaterial material) =>
        location.Place + "|" + location.Key + "|" + material.ItemName;
}

/// <summary>The pieces every custody test needs, named once.</summary>
internal static class Custody
{
    internal static NpcMaterial Timber => NpcMaterial.Of("timber");

    internal static NpcMaterial Nails => NpcMaterial.Of("nails");

    internal static string Job => "job-one";

    internal static string OtherJob => "job-two";

    internal static ReservationId Step(int step) => ReservationId.For(Job, step);

    internal static ReservationId OtherStep(int step) => ReservationId.For(OtherJob, step);

    internal static NpcCustodyLocation Body(NpcWorldEpoch epoch) =>
        new NpcCustodyLocation(NpcCustodyPlace.Carried, "the-body", epoch);

    internal static NpcCustodyLocation Chest(NpcWorldEpoch epoch, string key = "chest-a") =>
        new NpcCustodyLocation(NpcCustodyPlace.Stored, key, epoch);

    internal static NpcCustodyLocation Delivered(NpcWorldEpoch epoch) =>
        new NpcCustodyLocation(NpcCustodyPlace.Delivered, "the-depot", epoch);

    internal static NpcCustodyLocation Ground(NpcWorldEpoch epoch) =>
        new NpcCustodyLocation(NpcCustodyPlace.Ground, "a-drop", epoch);
}
