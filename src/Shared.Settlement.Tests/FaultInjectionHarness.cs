using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;
using TheConcernedCat.Settlement.Worker;

namespace Shared.Settlement.Tests;

/// <summary>Thrown at a kill point: the process is gone from here on.</summary>
internal sealed class ProcessKilled : Exception
{
    public ProcessKilled(string step)
        : base("the process was killed at " + step)
    {
    }
}

/// <summary>Everything physical, and the one process running against it.
///
/// A kill is not an exception the code under test may react to: once a kill
/// point is reached, every later inventory call and every later write throws,
/// so nothing the code does after that instant can reach the "disk" or the
/// "world". A restart then loads the world from its last save and the journal
/// from its file, exactly as a real crash leaves them.</summary>
internal sealed class FakeWorld
{
    private readonly List<string> _steps = new();

    public List<FakeInventory> Inventories { get; } = new();

    public List<FakeToolInventory> ToolInventories { get; } = new();

    public const double InitialTime = 1000.0;

    /// <summary>The net world time.</summary>
    public double Clock { get; set; } = InitialTime;

    /// <summary>The net time stored by the last completed save; null until the
    /// world has been saved.</summary>
    public double? SavedTime { get; private set; }

    /// <summary>The net time a load starts from: the last save's, or a world
    /// that was never saved starting over from its beginning.</summary>
    public double LoadedTime => SavedTime ?? InitialTime;

    public bool Dead { get; private set; }

    public bool Authority { get; set; } = true;

    /// <summary>The named step the process dies at: the first time it is
    /// reached, or with a <c>#n</c> suffix the n-th time in this world — for
    /// steps that share a name, like a return's intention and its result, which
    /// are both <c>ToolReturned</c> rows.</summary>
    public string? KillAt { get; set; }

    /// <summary>Every step reached, in order — the ordering evidence.</summary>
    public IReadOnlyList<string> Steps => _steps;

    public void Step(string name)
    {
        if (Dead)
        {
            throw new ProcessKilled(name);
        }

        _steps.Add(name);
        int reached = 0;
        foreach (string step in _steps)
        {
            if (string.Equals(step, name, StringComparison.Ordinal))
            {
                reached++;
            }
        }

        if (string.Equals(KillAt, name, StringComparison.Ordinal)
            || string.Equals(KillAt, name + "#" + reached.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            Dead = true;
            throw new ProcessKilled(name);
        }
    }

    public void Guard()
    {
        if (Dead)
        {
            throw new ProcessKilled("after death");
        }
    }

    public void Tick(double seconds = 1.0) => Clock += seconds;

    public FakeInventory Inventory(string name, bool moves = false)
    {
        FakeInventory inventory = moves ? new FakeMovingInventory(this, name) : new FakeInventory(this, name);
        Inventories.Add(inventory);
        inventory.Snapshot();
        return inventory;
    }

    public FakeToolInventory Tools(string name)
    {
        var inventory = new FakeToolInventory(this, name);
        ToolInventories.Add(inventory);
        inventory.Snapshot();
        return inventory;
    }

    /// <summary>The snapshot half of a world save: after the marker hook ran.
    /// </summary>
    public void Snapshot()
    {
        foreach (FakeInventory inventory in Inventories)
        {
            inventory.Snapshot();
        }

        foreach (FakeToolInventory inventory in ToolInventories)
        {
            inventory.Snapshot();
        }

        SavedTime = Clock;
    }

    /// <summary>The process comes back: the world is its last save, and time
    /// resumes from there.</summary>
    public void Restart()
    {
        Dead = false;
        KillAt = null;
        foreach (FakeInventory inventory in Inventories)
        {
            inventory.Rollback();
        }

        foreach (FakeToolInventory inventory in ToolInventories)
        {
            inventory.Rollback();
        }

        Clock = LoadedTime;
    }
}

/// <summary>An engine inventory: counts per item, a capacity, and switches for
/// every fault the executor has to survive.</summary>
internal class FakeInventory : IInventoryPort
{
    private readonly Dictionary<MaterialItem, int> _counts = new();
    private Dictionary<MaterialItem, int> _saved = new();

    public FakeInventory(FakeWorld world, string name)
    {
        World = world;
        Name = name;
    }

    protected FakeWorld World { get; }

    public string Name { get; }

    public int Capacity { get; set; } = int.MaxValue;

    public bool Available { get; set; } = true;

    /// <summary>Units this inventory accepts per add, below what it says it can
    /// accept: a partial acceptance CanAccept did not predict.</summary>
    public int? AddLimit { get; set; }

    /// <summary>Added to Add's return value: a port that misreports.</summary>
    public int AddReturnSkew { get; set; }

    public bool ThrowAfterAdd { get; set; }

    public bool ThrowBeforeRemove { get; set; }

    public bool ThrowAfterRemove { get; set; }

    /// <summary>Remove takes this many fewer than asked.</summary>
    public int RemoveShortfall { get; set; }

    public int Mutations { get; protected set; }

    public string Describe => Name;

    public bool IsAvailable
    {
        get
        {
            World.Guard();
            return Available;
        }
    }

    public int Total => _counts.Values.Sum();

    public int Count(MaterialItem item)
    {
        World.Guard();
        return Peek(item);
    }

    public int Peek(MaterialItem item) => _counts.TryGetValue(item, out int count) ? count : 0;

    public int CanAccept(MaterialItem item, int count)
    {
        World.Guard();
        long room = (long)Capacity - Total;
        return (int)Math.Max(0, Math.Min(count, room));
    }

    public int Add(MaterialItem item, int count)
    {
        World.Step(Name + ".add.before");
        int room = (int)Math.Max(0, Math.Min(int.MaxValue, (long)Capacity - Total));
        int accepted = Math.Min(count, Math.Min(room, AddLimit ?? int.MaxValue));
        Raw(item, accepted);
        World.Step(Name + ".add.after");
        if (ThrowAfterAdd)
        {
            throw new InvalidOperationException(Name + " failed after adding");
        }

        return accepted + AddReturnSkew;
    }

    public int Remove(MaterialItem item, int count)
    {
        World.Step(Name + ".remove.before");
        if (ThrowBeforeRemove)
        {
            throw new InvalidOperationException(Name + " failed before removing");
        }

        int removed = Math.Max(0, Math.Min(count - RemoveShortfall, Peek(item)));
        Raw(item, -removed);
        World.Step(Name + ".remove.after");
        if (ThrowAfterRemove)
        {
            throw new InvalidOperationException(Name + " failed after removing");
        }

        return removed;
    }

    /// <summary>Changes counts as the engine does, counting a mutation.</summary>
    public void Raw(MaterialItem item, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _counts[item] = Peek(item) + delta;
        Mutations++;
    }

    /// <summary>Something outside custody changed this inventory: the player,
    /// a creature, another mod.</summary>
    public void External(MaterialItem item, int count) => _counts[item] = count;

    public void Snapshot() => _saved = new Dictionary<MaterialItem, int>(_counts);

    public void Rollback()
    {
        _counts.Clear();
        foreach (KeyValuePair<MaterialItem, int> pair in _saved)
        {
            _counts[pair.Key] = pair.Value;
        }
    }
}

/// <summary>An engine inventory that can move items out of another in one
/// engine call, as vanilla <c>MoveItemToThis</c> does: add, then remove.
/// </summary>
internal sealed class FakeMovingInventory : FakeInventory, IInventoryMoveTarget
{
    public FakeMovingInventory(FakeWorld world, string name)
        : base(world, name)
    {
    }

    public bool ThrowBetweenAddAndRemove { get; set; }

    public bool CanMoveFrom(IInventoryPort source) => source is FakeInventory;

    public void MoveFrom(IInventoryPort source, MaterialItem item, int count)
    {
        var from = (FakeInventory)source;
        World.Step(Name + ".move.before");
        long room = (long)Capacity - Total;
        int moving = (int)Math.Max(0, Math.Min(Math.Min(count, from.Peek(item)), Math.Min(room, AddLimit ?? int.MaxValue)));

        Raw(item, moving);
        World.Step(Name + ".move.added");
        if (ThrowBetweenAddAndRemove)
        {
            throw new InvalidOperationException(Name + " failed between add and remove");
        }

        from.Raw(item, -moving);
        World.Step(Name + ".move.after");
    }
}

/// <summary>A tool inventory holding item instances.</summary>
internal sealed class FakeToolInventory : IToolInventory<object>
{
    private readonly FakeWorld _world;
    private readonly List<object> _items = new();
    private List<object> _saved = new();

    public FakeToolInventory(FakeWorld world, string name)
    {
        _world = world;
        Name = name;
    }

    public string Name { get; }

    public bool Full { get; set; }

    public bool RefuseAdd { get; set; }

    public bool RefuseRemove { get; set; }

    public IReadOnlyList<object> Items => _items;

    public void Put(object item) => _items.Add(item);

    public bool Contains(object item)
    {
        _world.Guard();
        return _items.Contains(item);
    }

    public bool CanAdd(object item)
    {
        _world.Guard();
        return !Full;
    }

    public bool Add(object item)
    {
        _world.Step(Name + ".tool.add.before");
        if (RefuseAdd)
        {
            return false;
        }

        _items.Add(item);
        _world.Step(Name + ".tool.add.after");
        return true;
    }

    public bool Remove(object item)
    {
        _world.Step(Name + ".tool.remove.before");
        if (RefuseRemove || !_items.Remove(item))
        {
            return false;
        }

        _world.Step(Name + ".tool.remove.after");
        return true;
    }

    public void Snapshot() => _saved = new List<object>(_items);

    public void Rollback()
    {
        _items.Clear();
        _items.AddRange(_saved);
    }
}

/// <summary>The journal's disk: a real <see cref="JournalStore"/> in a temp
/// directory, with switches for failed and half-failed saves and kill points
/// named after the row being persisted.</summary>
internal sealed class FaultyDisk
{
    private readonly FakeWorld _world;
    private readonly Dictionary<JournalEntryKind, int> _failBeforeWriting = new();
    private readonly HashSet<JournalEntryKind> _failAfterWriting = new();

    public FaultyDisk(FakeWorld world, JournalStore store)
    {
        _world = world;
        Store = store;
    }

    public JournalStore Store { get; }

    public int Writes { get; private set; }

    /// <summary>The next <paramref name="times"/> saves of a row of this kind
    /// fail without writing.</summary>
    public void FailBeforeWriting(JournalEntryKind kind, int times = 1) => _failBeforeWriting[kind] = times;

    public void StopFailing(JournalEntryKind kind) => _failBeforeWriting.Remove(kind);

    /// <summary>The next save of a row of this kind writes the file and then
    /// reports failure — the half-failure #299's review found reported as a
    /// full one.</summary>
    public void FailAfterWriting(JournalEntryKind kind) => _failAfterWriting.Add(kind);

    public bool Persist(SettlementJournal journal)
    {
        string kind = journal.Entries.Count == 0 ? "nothing" : journal.Entries[journal.Entries.Count - 1].Kind.ToString();
        _world.Step("persist.before." + kind);

        JournalEntryKind last = journal.Entries.Count == 0 ? JournalEntryKind.OrderTransition : journal.Entries[journal.Entries.Count - 1].Kind;
        if (_failBeforeWriting.TryGetValue(last, out int remaining) && remaining > 0)
        {
            _failBeforeWriting[last] = remaining - 1;
            return false;
        }

        JournalStore.SaveReport report = Store.Save(journal);
        Writes++;
        _world.Step("persist.after." + kind);

        if (_failAfterWriting.Remove(last))
        {
            return false;
        }

        return report.Saved || !journal.IsDirty;
    }
}

/// <summary>One process: the journal loaded from disk and custody opened for
/// one world load, as the Foreman runtime does on world load.</summary>
internal sealed class CustodyProcess
{
    public static readonly SettlementScope Scope = new(worldId: 31337, settlement: new SettlementId("custody-camp"));

    private CustodyProcess(FakeWorld world, FaultyDisk disk, JournalStore.LoadReport load, CustodyCore core)
    {
        World = world;
        Disk = disk;
        LoadReport = load;
        Core = core;
    }

    public FakeWorld World { get; }

    public FaultyDisk Disk { get; }

    public JournalStore.LoadReport LoadReport { get; }

    public CustodyCore Core { get; }

    public SettlementJournal Journal => LoadReport.Journal;

    public MaterialCustodyLedger Ledger => Core.Ledger;

    public static CustodyProcess Start(FakeWorld world, FaultyDisk disk)
    {
        JournalStore.LoadReport report = disk.Store.Load(Scope);
        double loaded = world.LoadedTime;
        CustodyCore core = CustodyCore.Open(
            report.Journal,
            () => disk.Persist(report.Journal),
            () => world.Clock,
            new WorldLoad(loaded, Guid.NewGuid()),
            () => world.Authority);
        return new CustodyProcess(world, disk, report, core);
    }

    /// <summary>A world save: the marker hook, then the snapshot.</summary>
    public void SaveWorld()
    {
        World.Tick();
        Core.OnWorldSaveStarted(World.Clock);
        World.Snapshot();
        World.Tick();
    }

    public CustodyProcess Restart()
    {
        World.Restart();
        return Start(World, Disk);
    }
}

/// <summary>Shared fixtures for custody tests.</summary>
internal static class CustodyFixtures
{
    public static readonly Guid Epoch = new("5c2c6d7e-2a57-4a44-8d8e-3f3d9b5ac001");
    public static readonly OrderId Order = new("collect-1");
    public static readonly WorkerId Worker = new("thorstein");
    public static readonly MaterialItem Stone = MaterialItem.Of(CollectedResource.Stone);
    public static readonly MaterialItem Wood = MaterialItem.Of(CollectedResource.Wood);

    public static CustodyLocation WorkerAt(Guid? epoch = null) =>
        new(CustodyPlace.Worker, "foreman/thorstein", epoch ?? Guid.Empty);

    public static CustodyLocation ChestAt(Guid epoch) => new(CustodyPlace.Destination, "1:42", epoch);

    public static CustodyLocation CartAt(Guid epoch) => new(CustodyPlace.Cart, "1:77", epoch);

    public static CollectionOrderDefinition Definition(OrderId? order = null, int stone = 20, int wood = 0)
    {
        var quotas = new List<ResourceQuota> { new(CollectedResource.Stone, stone) };
        if (wood > 0)
        {
            quotas.Add(new ResourceQuota(CollectedResource.Wood, wood));
        }

        return new CollectionOrderDefinition(
            order ?? Order,
            Worker,
            quotas,
            new WorkScope(WorkScopeSource.DefaultCampCircle, new SitePoint(10f, 20f, 30f), 30f, "your bed", 3, Epoch),
            DeliveryTarget.ToContainer("1:42", Epoch, new SitePoint(12f, 20f, 30f)),
            ParticipationMode.Solo,
            "Tester");
    }

    public static SourceKey Source(string id = "1:500") =>
        new("Pickable_Stone", id, Epoch, new SitePoint(11f, 20f, 31f));

    /// <summary>Accepts the order and brings <paramref name="units"/> Stone into
    /// the worker's custody through a real pickup and take, as the collection
    /// loop does. Returns the drop id used.</summary>
    public static void Gather(CustodyProcess process, FakeInventory worker, int units, string drop = "1:900")
    {
        CustodyCore core = process.Core;
        if (!core.Ledger.TryGetOrder(Order, out CollectionOrderRecord _))
        {
            Assert.True(core.RecordAccepted(Definition(), out string refusal), refusal);
        }

        RequestId? pickup = core.BeginPickup(Order, Source(), out string why);
        Assert.True(pickup.HasValue, why);

        process.World.Step("pick");
        var result = new PickupResult(
            PickupOutcome.Picked, new[] { new SpawnedDrop(drop, Stone, units) }, string.Empty);
        Assert.True(core.FinishPickup(pickup!.Value, result, out why), why);

        var ground = new CustodyLocation(CustodyPlace.SourceGround, drop, Epoch);
        var take = new TransferIntent(
            CustodyIds.ForTransfer(Order, core.Ledger), Order, ground, WorkerAt(), Stone, units, core.Ledger.Revision);
        Assert.Equal(TransferOutcome.Unspecified, core.BeginTransfer(take, out why));

        process.World.Step("take.before");
        worker.Raw(Stone, units);
        process.World.Step("take.after");

        Assert.True(core.FinishTransfer(
            new TransferReceipt(take.Request, TransferOutcome.Completed, units, ground, "taken"), out why), why);
    }

    public static TransferIntent Intent(CustodyCore core, CustodyLocation from, CustodyLocation to, int count, MaterialItem? item = null) =>
        new(CustodyIds.ForTransfer(Order, core.Ledger), Order, from, to, item ?? Stone, count, core.Ledger.Revision);

    /// <summary>An observer over fake inventories: the worker and the cart can
    /// be seen, nothing else.</summary>
    public sealed class Observer : ICustodyObserver
    {
        private readonly FakeInventory? _worker;
        private readonly FakeInventory? _cart;
        private readonly Func<MaterialItem, int> _cartBaseline;

        public Observer(FakeInventory? worker, FakeInventory? cart = null, Func<MaterialItem, int>? cartBaseline = null)
        {
            _worker = worker;
            _cart = cart;
            _cartBaseline = cartBaseline ?? (_ => 0);
        }

        public int? Observe(CustodyLocation location, MaterialItem item)
        {
            switch (location.Place)
            {
                case CustodyPlace.Worker:
                    return _worker?.Peek(item);
                case CustodyPlace.Cart:
                    return _cart == null ? null : Math.Max(0, _cart.Peek(item) - _cartBaseline(item));
                default:
                    return null;
            }
        }
    }
}
