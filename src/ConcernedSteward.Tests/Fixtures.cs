using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedSteward.Domain;
using TheConcernedCat.ConcernedSteward.Domain.Scope;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedSteward.Tests;

/// <summary>An inventory the tests can arrange exactly, including the states
/// that are hard to reach in a live game: a chest that has gone away mid-trip,
/// one that will not answer how much is in it, one that throws while being
/// moved out of.
///
/// It really does conserve. <see cref="MoveTo"/> moves the smaller of what is
/// here and what will fit there, and the two counts are the only record — so a
/// test that asserts conservation is asserting about a model that could have
/// broken it.</summary>
internal sealed class FakeStore : IItemStorePort
{
    private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);

    internal FakeStore(string describe, params (string item, int count)[] contents)
    {
        Describe = describe;
        foreach ((string item, int count) in contents)
        {
            _items[item] = count;
        }
    }

    public string Describe { get; }

    public bool Available { get; set; } = true;

    /// <summary>Total slots' worth this store will accept, or -1 for unlimited.
    /// </summary>
    public int Capacity { get; set; } = -1;

    /// <summary>Set by a test: counting throws, the way a destroyed object
    /// does.</summary>
    public bool CountThrows { get; set; }

    /// <summary>Set by a test: asking for room throws.</summary>
    public bool RoomThrows { get; set; }

    /// <summary>Set by a test: the move throws part way, after taking from the
    /// source. The fault-injection seam for a mutation boundary.</summary>
    public bool MoveThrows { get; set; }

    /// <summary>Set by a test: the move silently loses what it moved, so the
    /// two deltas disagree and the step must read as uncertain.</summary>
    public bool MoveLoses { get; set; }

    public bool IsAvailable => Available;

    public IReadOnlyCollection<string> ItemNames
    {
        get
        {
            var names = new List<string>();
            foreach (KeyValuePair<string, int> entry in _items)
            {
                if (entry.Value > 0)
                {
                    names.Add(entry.Key);
                }
            }

            return names;
        }
    }

    public int Count(string fuelItemName)
    {
        if (CountThrows)
        {
            throw new InvalidOperationException("that container is not here any more");
        }

        return _items.TryGetValue(fuelItemName, out int count) ? count : 0;
    }

    public int RoomFor(string fuelItemName, int count)
    {
        if (RoomThrows)
        {
            throw new InvalidOperationException("that container could not be asked");
        }

        if (Capacity < 0)
        {
            return count;
        }

        int held = 0;
        foreach (int value in _items.Values)
        {
            held += value;
        }

        return Math.Max(0, Math.Min(count, Capacity - held));
    }

    public void MoveTo(IItemStorePort destination, string fuelItemName, int count)
    {
        var target = (FakeStore)destination;
        int here = Count(fuelItemName);
        int room = target.RoomFor(fuelItemName, count);
        int moved = Math.Min(Math.Min(count, here), room);

        if (MoveThrows)
        {
            // Taken from the source and not yet given to the destination: the
            // window a crash can land in, and the one the "add before remove"
            // ordering exists to keep out of the real adapter.
            _items[fuelItemName] = here - moved;
            throw new InvalidOperationException("the move was interrupted");
        }

        _items[fuelItemName] = here - moved;
        if (!MoveLoses)
        {
            target._items.TryGetValue(fuelItemName, out int there);
            target._items[fuelItemName] = there + moved;
        }
    }

    /// <summary>Puts items in directly, for arranging a fixture.</summary>
    internal void Put(string item, int count) => _items[item] = count;
}

/// <summary>Fires the tests can arrange, including the outcomes a real fire
/// only produces under conditions nobody can stage reliably.</summary>
internal sealed class FakeFires : IFuelTargetPort
{
    private readonly List<FuelTargetObservation> _targets = new List<FuelTargetObservation>();

    /// <summary>Every key <see cref="FeedOneUnit"/> was called for, in order, so
    /// a test can prove that feeding did not happen at all.</summary>
    internal List<string> Fed { get; } = new List<string>();

    internal int SurveyCount { get; private set; }

    /// <summary>What the next feed will answer. Null means "work it out from
    /// the target's own fuel level", which is what a real fire does.</summary>
    internal Queue<FeedOutcome> Scripted { get; } = new Queue<FeedOutcome>();

    /// <summary>Set by a test: the target vanishes between the scan and the
    /// revalidation.</summary>
    internal bool Gone { get; set; }

    internal bool SurveyThrows { get; set; }

    internal bool ObserveThrows { get; set; }

    internal bool FeedThrows { get; set; }

    internal void Add(FuelTargetObservation target) => _targets.Add(target);

    internal void Replace(FuelTargetObservation target)
    {
        for (int index = 0; index < _targets.Count; index++)
        {
            if (_targets[index].Key.Equals(target.Key))
            {
                _targets[index] = target;
                return;
            }
        }

        _targets.Add(target);
    }

    public IReadOnlyList<FuelTargetObservation> Survey(SitePoint centre, float radius)
    {
        SurveyCount++;
        if (SurveyThrows)
        {
            throw new InvalidOperationException("the world could not be looked at");
        }

        // A fire that is gone is gone from the survey too, not only from the
        // revalidation. A fake that kept offering it would let a test pass
        // against a world that cannot exist.
        return Gone ? new List<FuelTargetObservation>() : new List<FuelTargetObservation>(_targets);
    }

    public bool TryObserve(FuelTargetKey key, out FuelTargetObservation observation)
    {
        observation = default;
        if (ObserveThrows)
        {
            throw new InvalidOperationException("that fire could not be looked at");
        }

        if (Gone)
        {
            return false;
        }

        foreach (FuelTargetObservation target in _targets)
        {
            if (target.Key.Equals(key))
            {
                observation = target;
                return true;
            }
        }

        return false;
    }

    public FeedMeasurement FeedOneUnit(FuelTargetKey key, IItemStorePort carrier)
    {
        Fed.Add(key.Value);
        if (FeedThrows)
        {
            throw new InvalidOperationException("adding fuel threw");
        }

        TryObserve(key, out FuelTargetObservation target);
        int before = carrier.Count(target.FuelItemName);

        FeedOutcome outcome = Scripted.Count > 0
            ? Scripted.Dequeue()
            : FuelMath.AcceptsOneUnit(target.Fuel, target.MaxFuel)
                ? FeedOutcome.Accepted
                : FeedOutcome.Declined;

        switch (outcome)
        {
            case FeedOutcome.Accepted:
                ((FakeStore)carrier).Put(target.FuelItemName, before - 1);
                Replace(Fuelled(target, target.Fuel + 1f));
                return new FeedMeasurement(
                    FeedOutcome.Accepted, before, before - 1, target.Fuel, target.Fuel + 1f,
                    "the fire took one " + target.FuelItemName);

            case FeedOutcome.Lost:
                // The unowned-object case: the item left his hands and the fire
                // gained nothing.
                ((FakeStore)carrier).Put(target.FuelItemName, before - 1);
                return new FeedMeasurement(
                    FeedOutcome.Lost, before, before - 1, target.Fuel, target.Fuel,
                    "one " + target.FuelItemName + " left his hands and the fire did not light by it");

            case FeedOutcome.Uncertain:
                return new FeedMeasurement(
                    FeedOutcome.Uncertain, before, before, target.Fuel, float.NaN,
                    "the result could not be read");

            default:
                return new FeedMeasurement(
                    FeedOutcome.Declined, before, before, target.Fuel, target.Fuel,
                    "the fire would take no more");
        }
    }

    internal static FuelTargetObservation Fuelled(in FuelTargetObservation target, float fuel) =>
        new FuelTargetObservation(
            target.Key, target.Position, target.FuelItemName, fuel, target.MaxFuel,
            target.CanRefill, target.InfiniteFuel, target.OwnedHere, target.AccessGranted);
}

/// <summary>Legs, without a body. Arrives by default, because a test about
/// withdrawing is not a test about walking.</summary>
internal sealed class FakeMotion : IStewardMotionPort
{
    internal List<SitePoint> WalkedTo { get; } = new List<SitePoint>();

    internal int Stops { get; private set; }

    public bool IsPresent { get; set; } = true;

    public SitePoint Position { get; set; }

    public WorkerWalkStatus WalkStatus { get; set; } = WorkerWalkStatus.Arrived;

    public WorkerDeferralReason DeferredReason { get; set; } = WorkerDeferralReason.None;

    public void WalkTo(SitePoint destination, float arrivalTolerance) => WalkedTo.Add(destination);

    public void Stop() => Stops++;
}

/// <summary>The pieces of one arranged Steward, so a test reads as the
/// situation it is about rather than as six lines of construction.</summary>
internal sealed class StewardFixture
{
    internal const string Wood = "$item_wood";
    internal const string Epoch = "epoch-1";

    internal StewardFixture(UpkeepLimits? limits = null)
    {
        Journal = new MemoryUpkeepJournal();
        Modes = new ActorModeOwner(StewardRole.Worker);
        Loop = new UpkeepLoop(limits ?? UpkeepLimits.Default, Journal, Modes, message => Reported.Add(message));
        Depot = new FakeStore("the supply chest", (Wood, 50));
        Pack = new FakeStore("the Steward's pack");
        Fires = new FakeFires();
        Motion = new FakeMotion();
        Scope = new StewardScope();
        Scope.UseIdentityEpoch(Epoch);
        Scope.Designate(
            DesignationRequest.Area(DesignationKind.SettlementArea, new SitePoint(0f, 0f, 0f), 32f),
            new AlwaysGranted());
        Scope.Designate(
            DesignationRequest.Container(new SitePoint(1f, 0f, 1f), "depot-key"),
            new AlwaysGranted());
    }

    internal MemoryUpkeepJournal Journal { get; }

    internal ActorModeOwner Modes { get; }

    internal UpkeepLoop Loop { get; }

    internal FakeStore Depot { get; }

    internal FakeStore Pack { get; }

    internal FakeFires Fires { get; }

    internal FakeMotion Motion { get; }

    internal StewardScope Scope { get; }

    internal List<string> Reported { get; } = new List<string>();

    internal bool TendingEnabled { get; set; } = true;

    internal WorkAuthorityVerdict Authority { get; set; } = WorkAuthorityVerdict.Granted;

    internal float Now { get; set; }

    /// <summary>A fire in the settlement, owned here, burning wood.</summary>
    internal FuelTargetObservation AddFire(
        string key, float fuel, float maxFuel = 10f, float x = 5f, float z = 5f,
        bool ownedHere = true, bool accessGranted = true, string? fuelName = null,
        bool canRefill = true, bool infiniteFuel = false, string? epoch = null)
    {
        var target = new FuelTargetObservation(
            new FuelTargetKey(key, epoch ?? Epoch),
            new SitePoint(x, 0f, z),
            fuelName ?? Wood,
            fuel,
            maxFuel,
            canRefill,
            infiniteFuel,
            ownedHere,
            accessGranted);
        Fires.Add(target);
        return target;
    }

    internal UpkeepTick Tick() => new UpkeepTick(
        Now, TendingEnabled, Authority, Scope.Resolve(), Fires, Depot, Pack, Motion);

    /// <summary>Advances the loop, moving the clock past the scan interval each
    /// time so an idle tick is never blocked on it.</summary>
    internal void Run(int steps = 1)
    {
        for (int step = 0; step < steps; step++)
        {
            Loop.Tick(Tick());
            Now += 20f;
        }
    }

    /// <summary>Ticks until the current trip closes: back to Idle with
    /// something withdrawn and nothing left in his hands.
    ///
    /// Robust on purpose. Counting ticks by hand makes a test assert about the
    /// number of phases as well as about the behaviour, and the moment a phase
    /// is added it fails for a reason nobody meant. It also stops BEFORE the
    /// next trip begins, which matters: the per-trip ledger resets when one
    /// does, so an assertion a tick late reads zero and looks like a
    /// conservation bug.</summary>
    internal void RunUntilTripEnds(int cap = 40)
    {
        for (int step = 0; step < cap; step++)
        {
            Loop.Tick(Tick());
            Now += 20f;
            if (Loop.Phase == UpkeepPhase.Idle && Loop.Custody.Carried == 0
                && (Loop.Custody.Withdrawn > 0 || Loop.Custody.Unaccounted > 0))
            {
                return;
            }

            if (Loop.Phase == UpkeepPhase.NeedsAttention)
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException(
            "the trip did not end within " + cap + " ticks; it is in " + Loop.Phase +
            " (" + Loop.Explanation + ")");
    }

    /// <summary>Advances the loop without moving the clock, for asserting on
    /// scan throttling.</summary>
    internal void RunSameInstant(int steps = 1)
    {
        for (int step = 0; step < steps; step++)
        {
            Loop.Tick(Tick());
        }
    }

    /// <summary>The conservation invariant, checked after anything that moves
    /// a unit. Wood in the world is the depot plus his pack plus what the fires
    /// burned; it never goes up.</summary>
    internal void AssertConserved(int startingStock)
    {
        FuelCustody custody = Loop.Custody;
        Assert.True(custody.Balances, "the custody ledger does not balance: " + custody.Describe());
        int accountedFor = Depot.Count(Wood) + Pack.Count(Wood) + custody.Burned + custody.Unaccounted;
        Assert.Equal(startingStock, accountedFor);
    }
}

internal sealed class AlwaysGranted : IDesignationSite
{
    public AreaAccess CheckAccess(SitePoint centre, float radius) => AreaAccess.Granted;
}

internal sealed class NeverGranted : IDesignationSite
{
    public AreaAccess CheckAccess(SitePoint centre, float radius) => AreaAccess.Denied;
}

internal sealed class Unanswerable : IDesignationSite
{
    public AreaAccess CheckAccess(SitePoint centre, float radius) => AreaAccess.Unavailable;
}
