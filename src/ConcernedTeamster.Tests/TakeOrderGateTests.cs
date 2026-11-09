using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>The gate on a take from a marked chest (#381, owner decision
/// 2026-10-31, `DECISIONS.md` D20): what the call site asks about Gunnar, the
/// room and the order before a container is resolved at all.
///
/// What this deliberately does <b>not</b> test is the container's permission -
/// the player's Take or Both mark, ownership, in-use, ward, privacy and reach -
/// which is the shared library's permit mint, asked by the take port at the
/// moment of the move and tested where it lives.</summary>
public sealed class TakeOrderGateTests
{
    private static TakeRequest Ready(
        bool collectionEnabled = true,
        bool takeEnabled = true,
        bool worldIsUp = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seamAvailable = true,
        bool workerPresent = true,
        bool workerRecordUnwritable = false,
        bool workerRecordUnreadable = false,
        bool transferInFlight = false,
        bool interruptedTourOpen = false,
        bool sourceDesignated = true,
        string? cargo = "Stone",
        bool cargoKnown = true,
        int unitsWanted = 10,
        int? unitsThatFit = 10) =>
        new TakeRequest(
            collectionEnabled,
            takeEnabled,
            worldIsUp,
            authority,
            seamAvailable,
            workerPresent,
            workerRecordUnwritable,
            workerRecordUnreadable,
            transferInFlight,
            interruptedTourOpen,
            sourceDesignated,
            cargo,
            cargoKnown,
            unitsWanted,
            unitsThatFit);

    [Fact]
    public void AReadyRequestIsAdmitted()
    {
        Assert.Equal(TakeRefusal.None, TakeOrderGate.Evaluate(Ready()));
    }

    [Fact]
    public void ADefaultedRequestIsRefused()
    {
        Assert.Equal(TakeRefusal.CollectionOff, TakeOrderGate.Evaluate(default));
    }

    [Fact]
    public void TheTakeIsOffByDefault()
    {
        // D20: off by default, and a switch of its own.
        Assert.False(GunnarCollectionDefaults.ChestTakeEnabled);
        Assert.False(GunnarCollectionDefaults.CollectionEnabled);
    }

    [Fact]
    public void TheCollectionSwitchAloneDoesNotAllowATake()
    {
        // A player who let him put stones in a chest has not let him take
        // anything out of one.
        Assert.Equal(TakeRefusal.TakeOff, TakeOrderGate.Evaluate(Ready(takeEnabled: false)));
    }

    [Fact]
    public void TheTakeSwitchAloneDoesNotAllowATakeEither()
    {
        Assert.Equal(TakeRefusal.CollectionOff, TakeOrderGate.Evaluate(Ready(collectionEnabled: false)));
    }

    [Fact]
    public void TheSwitchesAreAskedBeforeAnythingElse()
    {
        Assert.Equal(
            TakeRefusal.CollectionOff,
            TakeOrderGate.Evaluate(Ready(
                collectionEnabled: false,
                takeEnabled: false,
                worldIsUp: false,
                authority: WorkAuthorityVerdict.Unspecified,
                seamAvailable: false,
                workerPresent: false,
                sourceDesignated: false,
                cargo: "",
                unitsWanted: 0,
                unitsThatFit: null)));
    }

    [Fact]
    public void AnyAuthorityButGrantedRefuses()
    {
        foreach (WorkAuthorityVerdict authority in System.Enum.GetValues(typeof(WorkAuthorityVerdict)))
        {
            TakeRefusal refusal = TakeOrderGate.Evaluate(Ready(authority: authority));
            Assert.Equal(authority == WorkAuthorityVerdict.Granted ? TakeRefusal.None : TakeRefusal.WorkRefused, refusal);
        }
    }

    [Fact]
    public void AShuttingDownWorldRefuses()
    {
        Assert.Equal(TakeRefusal.WorldIsGoingAway, TakeOrderGate.Evaluate(Ready(worldIsUp: false)));
    }

    [Fact]
    public void AMissingProbeRefuses()
    {
        Assert.Equal(TakeRefusal.SeamUnavailable, TakeOrderGate.Evaluate(Ready(seamAvailable: false)));
    }

    [Fact]
    public void ABodyThatCannotWriteDownWhatItHoldsIsHandedNothingMore()
    {
        // D15's containment, applied to the other direction: a body whose last
        // change did not persist is handed nothing more to hold.
        Assert.Equal(
            TakeRefusal.WorkerRecordUnwritable,
            TakeOrderGate.Evaluate(Ready(workerRecordUnwritable: true, workerPresent: false)));
        Assert.Equal(
            TakeRefusal.WorkerRecordUnreadable,
            TakeOrderGate.Evaluate(Ready(workerRecordUnreadable: true, workerPresent: false)));
    }

    [Fact]
    public void AnAbsentGunnarRefuses()
    {
        Assert.Equal(TakeRefusal.NoWorker, TakeOrderGate.Evaluate(Ready(workerPresent: false)));
    }

    [Fact]
    public void SomethingInFlightRefuses()
    {
        Assert.Equal(TakeRefusal.AlreadyWorking, TakeOrderGate.Evaluate(Ready(transferInFlight: true)));
    }

    [Fact]
    public void AnOpenTourPlanRefuses()
    {
        Assert.Equal(TakeRefusal.InterruptedTourOpen, TakeOrderGate.Evaluate(Ready(interruptedTourOpen: true)));
    }

    [Fact]
    public void ATakeWithNoChosenChestIsRefusedRatherThanGuessed()
    {
        // No nearest chest: with nothing designated there is nothing to fall
        // back to.
        Assert.Equal(TakeRefusal.NoSource, TakeOrderGate.Evaluate(Ready(sourceDesignated: false)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("Stone Wood")]
    [InlineData("Stone,Wood")]
    [InlineData("../Stone")]
    [InlineData("St*ne")]
    public void ANameThatIsNotExactlyOneItemTakesNothing(string? cargo)
    {
        // Asked before the game is: a name that cannot be one item is never
        // looked up, so no wildcard or list can be read as "everything".
        Assert.Equal(TakeRefusal.NoCargoNamed, TakeOrderGate.Evaluate(Ready(cargo: cargo, cargoKnown: true)));
    }

    [Fact]
    public void AnOverlongNameIsRefused()
    {
        Assert.False(TakeOrderGate.IsCargoName(new string('a', TakeOrderGate.LongestCargoName + 1)));
        Assert.True(TakeOrderGate.IsCargoName(new string('a', TakeOrderGate.LongestCargoName)));
    }

    [Fact]
    public void AnItemTheGameDoesNotHaveIsRefused()
    {
        // "all" is a well-formed name; it takes nothing because the game has no
        // item called that, never because it is read as everything.
        Assert.Equal(TakeRefusal.UnknownCargo, TakeOrderGate.Evaluate(Ready(cargo: "all", cargoKnown: false)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AskingForNothingIsRefused(int wanted)
    {
        Assert.Equal(TakeRefusal.NothingWanted, TakeOrderGate.Evaluate(Ready(unitsWanted: wanted)));
    }

    [Fact]
    public void AnUnreadableCarryLimitRefusesRatherThanAssumingRoom()
    {
        Assert.Equal(TakeRefusal.CarryUnreadable, TakeOrderGate.Evaluate(Ready(unitsThatFit: null)));
    }

    [Fact]
    public void NoRoomUnderTheCarryLimitRefuses()
    {
        Assert.Equal(TakeRefusal.NoCarryRoom, TakeOrderGate.Evaluate(Ready(unitsThatFit: 0)));
    }

    [Theory]
    [InlineData(10, 10, 10)]
    [InlineData(50, 12, 12)]
    [InlineData(3, 12, 3)]
    [InlineData(int.MaxValue, 225, 225)]
    [InlineData(0, 10, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(-5, 10, 0)]
    public void TheTakeNeverAsksForMoreThanFitsOrMoreThanWasWanted(int wanted, int fits, int asked)
    {
        Assert.Equal(asked, TakeOrderGate.UnitsToAsk(wanted, fits));
    }

    [Fact]
    public void EveryTakeRefusalHasASentenceOfItsOwn()
    {
        var seen = new HashSet<string>();
        foreach (TakeRefusal refusal in System.Enum.GetValues(typeof(TakeRefusal)))
        {
            string said = TakeSentences.Describe(refusal, WorkAuthorityVerdict.Granted);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("nobody recorded", said);
            if (refusal != TakeRefusal.WorkRefused)
            {
                Assert.True(seen.Add(said), "two refusals share a sentence: " + said);
            }
        }
    }

    [Fact]
    public void EveryContainerRefusalHasASentenceOfItsOwn()
    {
        foreach (NpcContainerRefusal refusal in System.Enum.GetValues(typeof(NpcContainerRefusal)))
        {
            string said = TakeSentences.Describe(refusal);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("nobody recorded", said);
        }
    }

    [Fact]
    public void ADepositOnlyChestIsToldApartFromAnUnmarkedOne()
    {
        string unmarked = TakeSentences.Describe(NpcContainerRefusal.NotEnabled);
        string depositOnly = TakeSentences.Describe(NpcContainerRefusal.UseNotAllowed);
        Assert.NotEqual(unmarked, depositOnly);
        Assert.Contains("take", depositOnly);
    }

    [Fact]
    public void ARefusedTakeNeverTellsThePlayerMaterialWasLost()
    {
        foreach (NpcContainerRefusal refusal in System.Enum.GetValues(typeof(NpcContainerRefusal)))
        {
            string said = TakeSentences.Describe(refusal);
            Assert.DoesNotContain("lost", said);
            Assert.DoesNotContain("destroyed", said);
            Assert.DoesNotContain("deleted", said);
        }
    }
}

/// <summary>What a take does to Gunnar's ledger (D20): only what was measured
/// arriving is acquired.</summary>
public sealed class TakeAccountingTests
{
    private static IReadOnlyList<KeyValuePair<string, int>> Of(string item, int units) =>
        new[] { new KeyValuePair<string, int>(item, units) };

    [Fact]
    public void AMeasuredTakeIsAcquiredOntoHisBack()
    {
        var account = new CollectionAccount();

        int acquired = account.RecordTake("take-1", new TakeResult(TakeOutcome.Taken, Of("CopperOre", 12)));

        Assert.Equal(12, acquired);
        Assert.Equal(12, account.Ledger.Acquired);
        Assert.Equal(12, account.Ledger.At("CopperOre", CargoPlace.Carried));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void APartTakeAcquiresOnlyWhatArrived()
    {
        var account = new CollectionAccount();

        account.RecordTake("take-1", new TakeResult(TakeOutcome.PartlyTaken, Of("Stone", 7)));

        Assert.Equal(7, account.Ledger.At("Stone", CargoPlace.Carried));
    }

    [Fact]
    public void ARefusedOrUnmeasurableTakeAcquiresNothing()
    {
        // Even when a caller hands over a list: an amount nobody can prove is
        // not an amount.
        foreach (TakeOutcome outcome in new[] { TakeOutcome.Refused, TakeOutcome.Uncertain, TakeOutcome.Unspecified })
        {
            var account = new CollectionAccount();

            int acquired = account.RecordTake("take-1", new TakeResult(outcome, Of("Stone", 5)));

            Assert.Equal(0, acquired);
            Assert.Equal(0, account.Ledger.Acquired);
        }
    }

    [Fact]
    public void ATakeRecordedTwiceIsAcquiredOnce()
    {
        var account = new CollectionAccount();
        var result = new TakeResult(TakeOutcome.Taken, Of("Stone", 5));

        account.RecordTake("take-1", result);
        int again = account.RecordTake("take-1", result);

        Assert.Equal(0, again);
        Assert.Equal(5, account.Ledger.Acquired);
    }

    [Fact]
    public void WhatHeTookCanBeDepositedAndStillBalances()
    {
        var account = new CollectionAccount();
        account.RecordTake("take-1", new TakeResult(TakeOutcome.Taken, Of("CopperOre", 12)));

        account.RecordDeposit("deposit-2", new DepositResult(DepositOutcome.Deposited, Of("CopperOre", 12)));

        Assert.Equal(0, account.Ledger.At("CopperOre", CargoPlace.Carried));
        Assert.Equal(12, account.Ledger.At("CopperOre", CargoPlace.Delivered));
        Assert.True(account.Ledger.IsConserved);
    }

    [Fact]
    public void ZeroAndUnnamedEntriesAreIgnored()
    {
        var account = new CollectionAccount();
        var moved = new[]
        {
            new KeyValuePair<string, int>("Stone", 0),
            new KeyValuePair<string, int>("", 4),
            new KeyValuePair<string, int>("Wood", -3),
        };

        Assert.Equal(0, account.RecordTake("take-1", new TakeResult(TakeOutcome.Taken, moved)));
        Assert.Equal(0, account.Ledger.Acquired);
    }
}
