using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>The gate on a deposit (#381/#374, `DECISIONS.md` D15): what the
/// call site asks about Gunnar and the room before a container is resolved at
/// all.
///
/// The runtime that asks binds Unity, so every clause here would otherwise be
/// unexercisable - the same reason the pick's own gate is a game-free type. What
/// this deliberately does <b>not</b> test is the container's permission, which
/// is the shared library's permit mint and is asked at the moment of the move on
/// the container itself.</summary>
public sealed class DepositOrderGateTests
{
    private static DepositRequest Ready(
        bool featureEnabled = true,
        bool worldIsUp = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seamAvailable = true,
        bool workerPresent = true,
        bool workerRecordUnwritable = false,
        bool workerRecordUnreadable = false,
        bool transferInFlight = false,
        bool carryingSomething = true,
        bool destinationDesignated = true) =>
        new DepositRequest(
            featureEnabled,
            worldIsUp,
            authority,
            seamAvailable,
            workerPresent,
            workerRecordUnwritable,
            workerRecordUnreadable,
            transferInFlight,
            carryingSomething,
            destinationDesignated);

    [Fact]
    public void AReadyRequestIsAdmitted()
    {
        Assert.Equal(DepositRefusal.None, DepositOrderGate.Evaluate(Ready()));
    }

    [Fact]
    public void ADefaultedRequestIsRefused()
    {
        // The property that matters more than any single clause: a caller that
        // forgets to fill a field gets a refusal, never an admission.
        Assert.Equal(DepositRefusal.FeatureOff, DepositOrderGate.Evaluate(default));
    }

    [Fact]
    public void TheSwitchIsAskedFirst()
    {
        // Somebody who never turned this on is told that, rather than being told
        // about a worker or a chest, which are not their problem.
        Assert.Equal(
            DepositRefusal.FeatureOff,
            DepositOrderGate.Evaluate(Ready(
                featureEnabled: false,
                worldIsUp: false,
                authority: WorkAuthorityVerdict.Unspecified,
                seamAvailable: false,
                workerPresent: false,
                carryingSomething: false,
                destinationDesignated: false)));
    }

    [Fact]
    public void AnyAuthorityButGrantedRefuses()
    {
        // D15 gate 3, and the one that has to be re-asked in the frame of the
        // move rather than cached: a peer connecting mid-deposit ends it.
        //
        // Written over every value of the enum rather than a chosen few, so a
        // verdict added later is covered without anybody remembering to add it -
        // and so "only Granted admits" is the actual property asserted.
        foreach (WorkAuthorityVerdict authority in
                 System.Enum.GetValues(typeof(WorkAuthorityVerdict)))
        {
            DepositRefusal refusal = DepositOrderGate.Evaluate(Ready(authority: authority));
            if (authority == WorkAuthorityVerdict.Granted)
            {
                Assert.Equal(DepositRefusal.None, refusal);
            }
            else
            {
                Assert.Equal(DepositRefusal.WorkRefused, refusal);
            }
        }
    }

    [Fact]
    public void AShuttingDownWorldRefusesEvenWhileAuthorityStillSaysYes()
    {
        // The window the authority rule cannot see: the game's singletons still
        // answer while the runtime's own lifecycle has already reported the
        // world gone.
        Assert.Equal(
            DepositRefusal.WorldIsGoingAway,
            DepositOrderGate.Evaluate(Ready(worldIsUp: false)));
    }

    [Fact]
    public void AnUnwritableRecordIsNotReportedAsAnAbsentGunnar()
    {
        // The falsehood the pick's own gate exists to stop, and it would have
        // been reintroduced here for free: a body whose last change did not
        // persist IS absent as far as BoundBody is concerned, so "no worker" is
        // what a naive ordering produces - about a Gunnar standing in front of
        // the player, holding the thing.
        Assert.Equal(
            DepositRefusal.WorkerRecordUnwritable,
            DepositOrderGate.Evaluate(Ready(workerRecordUnwritable: true, workerPresent: false)));
    }

    [Fact]
    public void AnInertBodyIsNotReportedAsAnAbsentGunnarEither()
    {
        Assert.Equal(
            DepositRefusal.WorkerRecordUnreadable,
            DepositOrderGate.Evaluate(Ready(workerRecordUnreadable: true, workerPresent: false)));
    }

    [Fact]
    public void NeitherRecordStateCanBeDescribedAsNotHere()
    {
        // A property over both flags rather than two examples, which is how the
        // pick's own table is pinned and why the second omission was caught.
        foreach (bool unwritable in new[] { true, false })
        {
            foreach (bool unreadable in new[] { true, false })
            {
                if (!unwritable && !unreadable)
                {
                    continue;
                }

                DepositRefusal refusal = DepositOrderGate.Evaluate(
                    Ready(workerRecordUnwritable: unwritable,
                          workerRecordUnreadable: unreadable,
                          workerPresent: false));
                string said = DepositSentences.Describe(refusal, WorkAuthorityVerdict.Granted);

                Assert.NotEqual(DepositRefusal.NoWorker, refusal);
                Assert.DoesNotContain("not here", said);

                // And neither may point the player at the door that destroys
                // what he is holding.
                Assert.DoesNotContain("force", said);
            }
        }
    }

    [Fact]
    public void AGenuinelyAbsentGunnarStillReportsNoWorker()
    {
        Assert.Equal(
            DepositRefusal.NoWorker,
            DepositOrderGate.Evaluate(Ready(workerPresent: false)));
    }

    [Fact]
    public void ADepositWithNoChosenChestIsRefusedRatherThanGuessed()
    {
        // The clause that makes "no chest is chosen by proximity" true rather
        // than stated: with nothing designated there is nothing to fall back to,
        // so the gate refuses and the port is never reached.
        Assert.Equal(
            DepositRefusal.NoDestination,
            DepositOrderGate.Evaluate(Ready(destinationDesignated: false)));
    }

    [Fact]
    public void AMissingChestIsAskedAboutBeforeAnEmptyGunnar()
    {
        // Both wrong at once: the player is told to choose a chest, because that
        // is the thing they have to do either way.
        Assert.Equal(
            DepositRefusal.NoDestination,
            DepositOrderGate.Evaluate(Ready(destinationDesignated: false, carryingSomething: false)));
    }

    [Fact]
    public void CarryingNothingIsSaidOutLoudRatherThanPassingSilently()
    {
        Assert.Equal(
            DepositRefusal.NothingToDeposit,
            DepositOrderGate.Evaluate(Ready(carryingSomething: false)));
    }

    [Fact]
    public void ATransferAlreadyInFlightRefuses()
    {
        Assert.Equal(
            DepositRefusal.AlreadyWorking,
            DepositOrderGate.Evaluate(Ready(transferInFlight: true)));
    }

    [Fact]
    public void AMissingSeamRefusesBeforeAnythingIsCalled()
    {
        Assert.Equal(
            DepositRefusal.SeamUnavailable,
            DepositOrderGate.Evaluate(Ready(seamAvailable: false)));
    }

    // ------------------------------------------------------------------
    // The sentences. Both vocabularies, with the property that caught two
    // falsehoods in the pick's own table.
    // ------------------------------------------------------------------

    [Fact]
    public void EveryDepositRefusalHasASentenceOfItsOwn()
    {
        foreach (DepositRefusal refusal in System.Enum.GetValues(typeof(DepositRefusal)))
        {
            string said = DepositSentences.Describe(refusal, WorkAuthorityVerdict.Granted);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("nobody recorded", said);
        }
    }

    [Fact]
    public void EveryContainerRefusalHasASentenceOfItsOwn()
    {
        // The library decides which of these applies; this product owns the
        // wording, and a value with no case would leave a player with a chest
        // that does not work and no way to find out why.
        foreach (NpcContainerRefusal refusal in System.Enum.GetValues(typeof(NpcContainerRefusal)))
        {
            string said = DepositSentences.Describe(refusal);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("nobody recorded", said);
        }
    }

    [Fact]
    public void TheUnmarkedChestSentenceSaysWhatToGoAndDo()
    {
        // NotEnabled is the default state of every chest in the world, so this
        // is the sentence most players will actually see.
        string said = DepositSentences.Describe(NpcContainerRefusal.NotEnabled);
        Assert.Contains("not opened", said);
        Assert.Contains("key", said);
    }

    [Fact]
    public void TheTakeOnlyChestIsToldApartFromTheUnmarkedOne()
    {
        // Two different fixes - mark it at all, versus cycle it one further -
        // and the shared library keeps them as separate refusals precisely so a
        // player is not sent to the wrong one.
        Assert.NotEqual(
            DepositSentences.Describe(NpcContainerRefusal.NotEnabled),
            DepositSentences.Describe(NpcContainerRefusal.UseNotAllowed));
    }

    [Fact]
    public void ARefusedDepositNeverTellsThePlayerMaterialWasLost()
    {
        // Every container refusal leaves the remainder in him. None of these
        // sentences may suggest otherwise, because the one thing a player must
        // be able to rely on is that a refusal moved nothing.
        foreach (NpcContainerRefusal refusal in System.Enum.GetValues(typeof(NpcContainerRefusal)))
        {
            string said = DepositSentences.Describe(refusal);
            Assert.DoesNotContain("lost", said);
            Assert.DoesNotContain("destroyed", said);
            Assert.DoesNotContain("deleted", said);
        }
    }

    // ------------------------------------------------------------------
    // The cargo filter. The port's reading of what he carries is filtered by
    // this, so a widening here is a widening of what may be deposited.
    // ------------------------------------------------------------------

    [Fact]
    public void OnlyWhatCollectingYieldsIsOfferedForDeposit()
    {
        // An earlier version of the port read EVERY item with a drop prefab.
        // Gunnar happens to carry only picked material today, so "he deposits
        // what he collected" was true by accident - and the first tool a worker
        // body is ever issued would have gone into the player's chest with the
        // stone.
        Assert.True(GunnarCollectionAllowlist.IsCollectedYield("Stone"));
        Assert.True(GunnarCollectionAllowlist.IsCollectedYield("Wood"));

        Assert.False(GunnarCollectionAllowlist.IsCollectedYield("Hammer"));
        Assert.False(GunnarCollectionAllowlist.IsCollectedYield("AxeStone"));
        Assert.False(GunnarCollectionAllowlist.IsCollectedYield("Coins"));
        Assert.False(GunnarCollectionAllowlist.IsCollectedYield(string.Empty));
        Assert.False(GunnarCollectionAllowlist.IsCollectedYield(null));
    }

    [Fact]
    public void TheDepositFilterIsTheSameAllowlistThePickUses()
    {
        // One allowlist read in both directions, so widening what he may pick up
        // and widening what he may put down are one edit and cannot drift apart.
        foreach (CollectableKind kind in System.Enum.GetValues(typeof(CollectableKind)))
        {
            string yield = GunnarCollectionAllowlist.ExpectedYieldOf(kind);
            if (yield.Length == 0)
            {
                continue;
            }

            Assert.True(
                GunnarCollectionAllowlist.IsCollectedYield(yield),
                yield + " is a yield the pick allows but the deposit would refuse to move");
        }
    }

    [Fact]
    public void NoDestinationIsSaidTheSameWayInBothVocabularies()
    {
        // The gate's NoDestination and the library's NotDesignated are the same
        // situation reached from two directions, and both must say the thing
        // that is actually true: he never looks for the nearest chest.
        Assert.Contains(
            "nearest",
            DepositSentences.Describe(DepositRefusal.NoDestination, WorkAuthorityVerdict.Granted));
        Assert.Contains("nearest", DepositSentences.Describe(NpcContainerRefusal.NotDesignated));
    }
}
