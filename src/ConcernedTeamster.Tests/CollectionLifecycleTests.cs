using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>The routing that decides whether material can be minted (#381).
///
/// <b>Why these are the load-bearing tests of the wiring.</b> The port has two
/// lifecycle verbs and they are not interchangeable: a job ending must keep the
/// unconfirmed-source record, because those sources still exist and may still be
/// mid-settle, and only a world going away may drop it. Getting them backwards
/// re-opens the defect where <c>begin - pick - forget - begin</c> on one source
/// yields a second full load out of nothing. The runtime that reports these
/// events binds Unity and no test here can load it, which is exactly why the
/// routing itself lives in a game-free type.</summary>
public sealed class CollectionLifecycleTests
{
    /// <summary>Records which verb was called, in order. Nothing else: the
    /// question is which one, not what it did.</summary>
    private sealed class Recorder : IPickLifecycle
    {
        public List<string> Calls { get; } = new List<string>();

        public void Forget() => Calls.Add("job");

        public void ForgetWorld() => Calls.Add("world");
    }

    [Fact]
    public void AWorldGoingAway_ForgetsTheWorld()
    {
        var recorder = new Recorder();
        var lifecycle = new CollectionLifecycle(recorder);

        lifecycle.ObserveWorld(true);
        Assert.Equal(PickForget.World, lifecycle.ObserveWorld(false));

        Assert.Equal(new[] { "world" }, recorder.Calls);
    }

    [Fact]
    public void AnOrderEnding_ForgetsTheJobAndNotTheWorld()
    {
        var recorder = new Recorder();
        var lifecycle = new CollectionLifecycle(recorder);

        lifecycle.ObserveWorld(true);
        Assert.Equal(PickForget.Job, lifecycle.OrderEnded());

        Assert.Equal(new[] { "job" }, recorder.Calls);
        Assert.True(lifecycle.WorldIsUp, "an order ending is not a world going away");
    }

    [Fact]
    public void TearingDown_ForgetsTheWorld()
    {
        var recorder = new Recorder();
        var lifecycle = new CollectionLifecycle(recorder);

        lifecycle.ObserveWorld(true);
        Assert.Equal(PickForget.World, lifecycle.Shutdown());

        Assert.Equal(new[] { "world" }, recorder.Calls);
        Assert.False(lifecycle.WorldIsUp);
    }

    [Fact]
    public void AWorldComingUp_ForgetsNothing()
    {
        var recorder = new Recorder();
        var lifecycle = new CollectionLifecycle(recorder);

        Assert.Equal(PickForget.Nothing, lifecycle.ObserveWorld(true));
        Assert.Equal(PickForget.Nothing, lifecycle.ObserveWorld(true));

        // Dropping the record is the minting direction, so it is never done on
        // the strength of an up-edge a flicker could also produce.
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void NoWorldWasEverSeen_SoNoUnloadIsInvented()
    {
        var recorder = new Recorder();
        var lifecycle = new CollectionLifecycle(recorder);

        Assert.Equal(PickForget.Nothing, lifecycle.ObserveWorld(false));
        Assert.Equal(PickForget.Nothing, lifecycle.ObserveWorld(false));

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void EveryFrameOfALoadedWorld_ForgetsNothing()
    {
        var recorder = new Recorder();
        var lifecycle = new CollectionLifecycle(recorder);

        for (int frame = 0; frame < 500; frame++)
        {
            Assert.Equal(PickForget.Nothing, lifecycle.ObserveWorld(true));
        }

        Assert.Empty(recorder.Calls);
    }

    // ------------------------------------------------------------------
    // The same routing over the real accounting: the mint, end to end.
    // ------------------------------------------------------------------

    /// <summary>The port's own mapping of the two verbs onto the accounting,
    /// spelled the same way the port spells it, so the routing above can be
    /// exercised against the real record rather than a counter.</summary>
    private sealed class AccountingLifecycle : IPickLifecycle
    {
        public PickAccounting Accounting { get; } = new PickAccounting();

        public void Forget() => Accounting.ForgetJob();

        public void ForgetWorld() => Accounting.ForgetWorld();
    }

    [Fact]
    public void ACancelledOrder_StillRefusesASecondPickOfTheSameSource()
    {
        var port = new AccountingLifecycle();
        var lifecycle = new CollectionLifecycle(port);
        lifecycle.ObserveWorld(true);

        Assert.Equal(PickGuard.None, port.Accounting.MayBegin("stone-7", 1, 10f));
        port.Accounting.Began("stone-7", 1, 10f);

        // The order is cancelled inside the settle window: the world has not yet
        // said the source is picked.
        lifecycle.OrderEnded();

        Assert.Equal(
            PickGuard.AwaitingConfirmation,
            port.Accounting.MayBegin("stone-7", 1, 10.5f));
    }

    [Fact]
    public void AWorldUnload_ReleasesTheRecordSoTheNextWorldIsNotRefused()
    {
        var port = new AccountingLifecycle();
        var lifecycle = new CollectionLifecycle(port);
        lifecycle.ObserveWorld(true);

        port.Accounting.Began("stone-7", 1, 10f);
        Assert.Equal(1, port.Accounting.AwaitingConfirmation);

        lifecycle.ObserveWorld(false);

        Assert.Equal(0, port.Accounting.AwaitingConfirmation);
        Assert.Equal(PickGuard.None, port.Accounting.MayBegin("stone-7", 1, 10.5f));
    }
}
