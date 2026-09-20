using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A container as a role's adapter would present one: a name that means
/// something for one world load, a place, and an access that is re-read every
/// time somebody asks.</summary>
internal sealed class FakeContainer : INpcContainer
{
    private int _reads;

    internal FakeContainer(string key, NpcWorldEpoch epoch, NpcPoint position, string describe = "the supply chest")
    {
        Key = key;
        Epoch = epoch;
        Position = position;
        Describe = describe;
        Allowed = NpcContainerUse.Off;
        Sighting = new NpcContainerSighting(true, true, true, false, true, true, false, false);
    }

    public string Key { get; }

    public NpcWorldEpoch Epoch { get; }

    public NpcPoint Position { get; }

    public string Describe { get; }

    /// <summary>What the player allowed, as the permission book would answer.
    /// </summary>
    internal NpcContainerUse Allowed { get; set; }

    /// <summary>What the adapter saw in the world.</summary>
    internal NpcContainerSighting Sighting { get; set; }

    /// <summary>How many times access was asked for, so a test can prove it is
    /// asked at the moment of use rather than cached.</summary>
    internal int Reads => _reads;

    public NpcContainerAccess Access
    {
        get
        {
            _reads++;
            return NpcContainerGate.Judge(Allowed, Sighting);
        }
    }
}

/// <summary>A container whose every getter throws - a chest destroyed halfway
/// through the question.</summary>
internal sealed class BrokenContainer : INpcContainer
{
    public string Key => throw new System.InvalidOperationException("gone");

    public NpcPoint Position => throw new System.InvalidOperationException("gone");

    public string Describe => throw new System.InvalidOperationException("gone");

    public NpcWorldEpoch Epoch => throw new System.InvalidOperationException("gone");

    public NpcContainerAccess Access => throw new System.InvalidOperationException("gone");
}

internal static class Chests
{
    internal static NpcContainerPlace Place(float x, float y, float z, int prefab = 4321) =>
        new NpcContainerPlace(new NpcPoint(x, y, z), prefab);

    internal static FakeContainer Enabled(NpcWorldEpoch world, NpcContainerUse allowed, string key = "chest-17") =>
        new FakeContainer(key, world, new NpcPoint(10f, 2f, 10f)) { Allowed = allowed };
}

public class ContainerPermissionBookTests
{
    [Fact]
    public void EveryContainerStartsClosedToHelpers()
    {
        var book = new NpcContainerPermissionBook();

        Assert.Equal(NpcContainerUse.Off, book.Allowance(Chests.Place(1f, 2f, 3f)));
        Assert.Equal(0, book.Count);
        Assert.False(book.IsDirty);
    }

    [Fact]
    public void ThePlayerCyclesTheFourStatesInOneOrder()
    {
        var book = new NpcContainerPermissionBook();
        NpcContainerPlace chest = Chests.Place(1f, 2f, 3f);

        Assert.Equal(NpcContainerUse.Take, book.Cycle(chest));
        Assert.Equal(NpcContainerUse.Deposit, book.Cycle(chest));
        Assert.Equal(NpcContainerUse.Both, book.Cycle(chest));
        Assert.Equal(NpcContainerUse.Off, book.Cycle(chest));
        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void TurningAContainerOffForgetsItSoAnEmptyBookMeansNothingIsEnabled()
    {
        var book = new NpcContainerPermissionBook();
        NpcContainerPlace chest = Chests.Place(1f, 2f, 3f);
        book.SetAllowance(chest, NpcContainerUse.Both);
        Assert.Equal(1, book.Count);

        Assert.True(book.SetAllowance(chest, NpcContainerUse.Off));
        Assert.Equal(0, book.Count);
        Assert.Equal(NpcContainerUse.Off, book.Allowance(chest));
        Assert.False(book.SetAllowance(chest, NpcContainerUse.Off));
    }

    [Fact]
    public void APermissionIsFoundAgainByPlaceAfterAReload()
    {
        // The reason a place is used rather than the game's own id: a world load
        // renumbers every saved object, so the number that named this chest last
        // night names a different one now. A chest does not move, so its place
        // and its piece type are an identity the save already preserves.
        var book = new NpcContainerPermissionBook();
        book.SetAllowance(Chests.Place(120.25f, 31.5f, -44.75f), NpcContainerUse.Both);

        var records = new List<NpcContainerPermission>(book.Allowed);
        NpcContainerPermissionBook reloaded = NpcContainerPermissionBook.Restore(records, out int dropped);

        Assert.Equal(0, dropped);
        Assert.False(reloaded.IsDirty);
        Assert.Equal(NpcContainerUse.Both, reloaded.Allowance(Chests.Place(120.25f, 31.5f, -44.75f)));

        // And a chest rebuilt on the same socket, a couple of centimetres off,
        // is the same chest and keeps its permission.
        Assert.Equal(NpcContainerUse.Both, reloaded.Allowance(Chests.Place(120.4f, 31.6f, -44.75f)));
    }

    [Fact]
    public void ADifferentChestOnTheSameSpotIsADifferentDecision()
    {
        var book = new NpcContainerPermissionBook();
        book.SetAllowance(Chests.Place(10f, 0f, 10f, prefab: 111), NpcContainerUse.Both);

        Assert.Equal(NpcContainerUse.Off, book.Allowance(Chests.Place(10f, 0f, 10f, prefab: 222)));
        Assert.Equal(NpcContainerUse.Off, book.Allowance(Chests.Place(12f, 0f, 10f, prefab: 111)));
        Assert.Equal(NpcContainerUse.Off, book.Allowance(Chests.Place(10f, 3f, 10f, prefab: 111)));

        // A prefab the role could not read matches any: refusing on a fact
        // nobody could establish would silently forget a permission the player
        // set.
        Assert.Equal(NpcContainerUse.Both, book.Allowance(Chests.Place(10f, 0f, 10f, prefab: 0)));
    }

    [Fact]
    public void ARecordThatCannotBeReadBecomesOffRatherThanAGuess()
    {
        var records = new List<NpcContainerPermission>
        {
            new NpcContainerPermission(Chests.Place(float.NaN, 0f, 0f), NpcContainerUse.Both),
            new NpcContainerPermission(Chests.Place(1f, 1f, 1f), NpcContainerUse.Off),
            new NpcContainerPermission(Chests.Place(5f, 0f, 5f), NpcContainerUse.Take),
            new NpcContainerPermission(Chests.Place(5.1f, 0f, 5f), NpcContainerUse.Deposit),
        };

        NpcContainerPermissionBook book = NpcContainerPermissionBook.Restore(records, out int dropped);

        Assert.Equal(3, dropped);
        Assert.Equal(1, book.Count);
        Assert.Equal(NpcContainerUse.Take, book.Allowance(Chests.Place(5f, 0f, 5f)));
        Assert.Equal(NpcContainerUse.Off, book.Allowance(Chests.Place(1f, 1f, 1f)));
    }

    [Fact]
    public void AnAllowanceNobodyChoseIsOff()
    {
        var book = new NpcContainerPermissionBook();
        NpcContainerPlace chest = Chests.Place(1f, 1f, 1f);

        // A value a cast or a damaged row could produce, with bits nobody
        // defined.
        Assert.False(book.SetAllowance(chest, (NpcContainerUse)8));
        Assert.Equal(NpcContainerUse.Off, book.Allowance(chest));

        Assert.True(book.SetAllowance(chest, (NpcContainerUse)9));
        Assert.Equal(NpcContainerUse.Take, book.Allowance(chest));
    }

    [Fact]
    public void AContainerWithNoRealPositionCanNeverBeEnabled()
    {
        var book = new NpcContainerPermissionBook();

        Assert.False(book.SetAllowance(Chests.Place(float.NaN, 0f, 0f), NpcContainerUse.Both));
        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void ClearForgetsEverythingAndSaysHowMuch()
    {
        var book = new NpcContainerPermissionBook();
        book.SetAllowance(Chests.Place(1f, 0f, 1f), NpcContainerUse.Take);
        book.SetAllowance(Chests.Place(9f, 0f, 9f), NpcContainerUse.Deposit);
        book.MarkClean();

        Assert.Equal(2, book.Clear());
        Assert.True(book.IsDirty);
        Assert.Equal(0, book.Clear());
    }
}

public class ContainerGateTests
{
    [Fact]
    public void TakingFromAnUnapprovedChestIsRefusedAndSaysSo()
    {
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Off);

        NpcContainerAuthorization answer = NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world);

        Assert.False(answer.IsGranted);
        Assert.Null(answer.Permit);
        Assert.Equal(NpcContainerRefusal.NotEnabled, answer.Refusal);
        Assert.Contains("the supply chest", answer.Reason);
    }

    [Fact]
    public void ADesignatedSourceThatOnlyPermitsDepositRefusesTheTakeAndSaysWhichWayRound()
    {
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Deposit);
        var assignment = new NpcContainerAssignment(chest, chest);

        NpcContainerAuthorization take = assignment.AuthorizeTake(world);
        NpcContainerAuthorization deposit = assignment.AuthorizeDeposit(world);

        Assert.Equal(NpcContainerRefusal.UseNotAllowed, take.Refusal);
        Assert.Contains("not taken from", take.Reason);
        Assert.True(deposit.IsGranted);
    }

    [Fact]
    public void BeingToldToUseAChestIsNotBeingAllowedToUseIt()
    {
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Off);
        var assignment = new NpcContainerAssignment(chest, chest);

        Assert.False(assignment.AuthorizeTake(world).IsGranted);
        Assert.False(assignment.AuthorizeDeposit(world).IsGranted);
    }

    [Fact]
    public void DepositingIntoAnArbitraryNearbyContainerIsNotPossible()
    {
        // The second forbidden behaviour, and the guarantee is structural: an
        // assignment holds the containers it was given and offers no way to get
        // a third. There is nothing here that searches, ranks or picks, so a
        // nearby chest that is wide open is still not the destination.
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer designated = new FakeContainer("designated", world, new NpcPoint(0f, 0f, 0f), "the shed")
        {
            Allowed = NpcContainerUse.Deposit,
        };
        FakeContainer nextDoor = new FakeContainer("next-door", world, new NpcPoint(1f, 0f, 0f), "the other chest")
        {
            Allowed = NpcContainerUse.Both,
        };

        var assignment = new NpcContainerAssignment(null, designated);

        Assert.Same(designated, assignment.Destination);
        Assert.Null(assignment.Source);
        Assert.Contains("the shed", assignment.AuthorizeDeposit(world).Permit!.Describe);
        Assert.Equal(0, nextDoor.Reads);
    }

    [Fact]
    public void AJobWithNoDestinationKeepsWhatItIsCarrying()
    {
        NpcWorldEpoch world = Identities.AWorld();
        var assignment = new NpcContainerAssignment(Chests.Enabled(world, NpcContainerUse.Take), null);

        NpcContainerAuthorization deposit = assignment.AuthorizeDeposit(world);

        Assert.False(deposit.IsGranted);
        Assert.Contains("stay with him", deposit.Reason);
    }

    [Fact]
    public void AnAssignmentThatNamesNothingIsNotAnAssignment()
    {
        Assert.Throws<System.ArgumentException>(() => new NpcContainerAssignment(null, null));
    }

    [Fact]
    public void TheWardAndThePrivacySettingAreTwoDifferentThingsToGoAndChange()
    {
        // The shipped gate collapses both into "access denied". The fix for one
        // is a guard stone and for the other is the chest, so a player told only
        // that it does not work has been told nothing.
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Both);

        chest.Sighting = new NpcContainerSighting(true, true, true, false, false, true, false, false);
        NpcContainerAuthorization warded = NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world);
        Assert.Equal(NpcContainerRefusal.WardDenied, warded.Refusal);
        Assert.Contains("ward", warded.Reason);

        chest.Sighting = new NpcContainerSighting(true, true, true, false, true, false, false, false);
        NpcContainerAuthorization privately = NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world);
        Assert.Equal(NpcContainerRefusal.PrivacyDenied, privately.Refusal);
        Assert.Contains("privacy", privately.Reason);
    }

    [Fact]
    public void ThisGateIsStricterThanTheShippedStewardCheckAndThatCostsSomebodyAWorkingDepot()
    {
        // Recorded as a test because it is a behaviour change with a price. The
        // Steward's depot check asks two questions - do we own it, is anybody in
        // it - and a depot inside somebody else's ward, or one set to Private
        // and built by another character, is written to today. Under this gate
        // it refuses. That is a permission hole closed, and a player whose depot
        // stops working needs the refusal to name the fix, which is why the two
        // are separate values above.
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer depot = Chests.Enabled(world, NpcContainerUse.Both);

        NpcContainerSighting stewardWouldAllow = new NpcContainerSighting(
            exists: true,
            identityMatches: true,
            ownedHere: true,
            inUse: false,
            wardAllows: false,
            privacyAllows: false,
            reachAsked: false,
            withinReach: false);
        depot.Sighting = stewardWouldAllow;

        Assert.False(NpcContainerGate.Authorize(depot, NpcContainerUse.Take, world).IsGranted);
    }

    [Fact]
    public void ReachOnlyRefusesWhenItWasAsked()
    {
        // Carried from the shipped gate unchanged: a caller with nobody standing
        // anywhere is not asking about reach, and reach is the one refusal that
        // is not a fault.
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Both);

        chest.Sighting = new NpcContainerSighting(true, true, true, false, true, true, reachAsked: false, withinReach: false);
        Assert.True(NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).IsGranted);

        chest.Sighting = new NpcContainerSighting(true, true, true, false, true, true, reachAsked: true, withinReach: false);
        Assert.Equal(
            NpcContainerRefusal.OutOfReach,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).Refusal);
    }

    [Fact]
    public void TheRefusalReportedIsTheFirstOneAPersonWouldWantToHear()
    {
        // Mechanical rather than a list of cases: the lowest-numbered refusal
        // that applies, and the enum is written in the order a person would ask.
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Deposit);

        // Wanting to take from a deposit-only chest that is also behind a ward:
        // the ward is not the thing to mention first.
        chest.Sighting = new NpcContainerSighting(true, true, true, false, false, true, false, false);
        Assert.Equal(
            NpcContainerRefusal.UseNotAllowed,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).Refusal);

        // But a chest that is not there outranks anything the player set.
        chest.Sighting = NpcContainerSighting.Gone();
        Assert.Equal(
            NpcContainerRefusal.Gone, NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).Refusal);

        // And a name that now finds something else outranks the player's switch.
        chest.Sighting = new NpcContainerSighting(true, false, true, false, true, true, false, false);
        Assert.Equal(
            NpcContainerRefusal.NotThisContainer,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).Refusal);
    }

    [Fact]
    public void AWardedChestIsNeverReportedAsOneThePlayerForgotToSwitchOn()
    {
        // The trap in reusing Access.Permits for this: it needs both halves, so
        // a fully enabled chest behind a ward reads as not enabled, and the
        // player goes and changes the wrong thing.
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Both);
        chest.Sighting = new NpcContainerSighting(true, true, true, false, false, true, false, false);

        Assert.Equal(
            NpcContainerRefusal.WardDenied,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).Refusal);
    }

    [Fact]
    public void PermissionIsReadAtTheMomentOfUseAndNeverCachedFromAnEarlierTick()
    {
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Both);

        Assert.True(NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).IsGranted);
        int readsAfterFirst = chest.Reads;

        chest.Sighting = new NpcContainerSighting(true, true, true, true, true, true, false, false);
        Assert.Equal(
            NpcContainerRefusal.InUse, NpcContainerGate.Authorize(chest, NpcContainerUse.Take, world).Refusal);
        Assert.True(chest.Reads > readsAfterFirst);
    }

    [Fact]
    public void AContainerNamedInAnotherWorldLoadIsRefusedRatherThanResolved()
    {
        NpcWorldEpoch yesterday = Identities.AWorld();
        NpcWorldEpoch today = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(yesterday, NpcContainerUse.Both);

        Assert.Equal(
            NpcContainerRefusal.NotThisContainer,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Take, today).Refusal);
        Assert.Equal(
            NpcContainerRefusal.NotThisContainer,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Take, NpcWorldEpoch.Unknown).Refusal);
    }

    [Fact]
    public void AContainerThatCannotAnswerHasNotSaidYes()
    {
        NpcWorldEpoch world = Identities.AWorld();

        Assert.Equal(
            NpcContainerRefusal.Gone, NpcContainerGate.Authorize(new BrokenContainer(), NpcContainerUse.Take, world).Refusal);
        Assert.Equal(NpcContainerRefusal.Gone, NpcContainerGate.Authorize(null, NpcContainerUse.Take, world).Refusal);
    }

    [Fact]
    public void APermitIsOneDirectionOfOneTransferAndNeverBoth()
    {
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Both);

        Assert.Equal(
            NpcContainerRefusal.UseNotAllowed,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Both, world).Refusal);
        Assert.Equal(
            NpcContainerRefusal.UseNotAllowed,
            NpcContainerGate.Authorize(chest, NpcContainerUse.Off, world).Refusal);
    }

    [Fact]
    public void APermitCannotBeMintedForAContainerThatRefuses()
    {
        // The mint is where the invariant lives, not the wrapper above it, so
        // reaching past the gate gets a caller nothing.
        NpcWorldEpoch world = Identities.AWorld();
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Off);

        Assert.Null(NpcContainerPermit.Issue(chest, NpcContainerUse.Take, world, out NpcContainerRefusal refusal));
        Assert.Equal(NpcContainerRefusal.NotEnabled, refusal);

        chest.Allowed = NpcContainerUse.Take;
        chest.Sighting = new NpcContainerSighting(true, true, false, false, true, true, false, false);
        Assert.Null(NpcContainerPermit.Issue(chest, NpcContainerUse.Take, world, out refusal));
        Assert.Equal(NpcContainerRefusal.NotOwnedHere, refusal);
    }

    [Fact]
    public void NothingOutsideThisPackageCanForgeAPermit()
    {
        Assert.Empty(typeof(NpcContainerPermit).GetConstructors());
        Assert.DoesNotContain(
            typeof(NpcContainerPermit).GetConstructors(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            constructor => constructor.IsAssembly || constructor.IsFamilyOrAssembly);
    }
}

public class ContainerTransferTests
{
    private static NpcContainerPermit Permit(NpcWorldEpoch world, NpcContainerUse use = NpcContainerUse.Deposit)
    {
        FakeContainer chest = Chests.Enabled(world, NpcContainerUse.Both);
        NpcContainerPermit? permit = NpcContainerPermit.Issue(chest, use, world, out _);
        Assert.NotNull(permit);
        return permit!;
    }

    [Fact]
    public void OverflowStaysWithTheCarrierAndIsNeverDeleted()
    {
        // Forbidden behaviour: deleting overflow. Forty nails into a chest with
        // room for thirty moves thirty and keeps ten. What may move and what
        // stays always add back up to what was wanted.
        NpcTransferPlan plan = NpcTransferPlan.For(wanted: 40, availableAtSource: 40, roomAtDestination: 30);

        Assert.Equal(30, plan.Units);
        Assert.Equal(10, plan.Shortfall);
        Assert.Equal(plan.Wanted, plan.Units + plan.Shortfall);
        Assert.False(plan.IsWhole);
        Assert.False(plan.IsEmpty);
    }

    [Theory]
    [InlineData(10, 10, 10, 10, 0)]
    [InlineData(10, 4, 10, 4, 6)]
    [InlineData(10, 10, 0, 0, 10)]
    [InlineData(0, 10, 10, 0, 0)]
    [InlineData(10, -5, 10, 0, 10)]
    [InlineData(-3, 10, 10, 0, 0)]
    public void APlanNeverMovesMoreThanIsThereOrMoreThanWillFit(
        int wanted, int available, int room, int units, int shortfall)
    {
        NpcTransferPlan plan = NpcTransferPlan.For(wanted, available, room);

        Assert.Equal(units, plan.Units);
        Assert.Equal(shortfall, plan.Shortfall);
        Assert.Equal(plan.Wanted, plan.Units + plan.Shortfall);
    }

    [Fact]
    public void ATransferWithoutPermissionRecordsNothing()
    {
        // Forbidden behaviour: bypassing custody. A receipt cannot be made
        // without the permission that authorised the move, and a permit cannot
        // be made for a container that refused.
        ContainerMoveResult receipt = ContainerMoveResult.Record(
            null, Identities.AWorld(), NpcTransferPlan.For(5, 5, 5), 5, 5);

        Assert.Equal(ContainerMoveOutcome.Refused, receipt.Outcome);
        Assert.Equal(0, receipt.Moved);
        Assert.False(receipt.IsSettled);
    }

    [Fact]
    public void AnInterruptedTransferNeverDuplicatesCargo()
    {
        // Forbidden behaviour: duplicating cargo on an interrupted transfer. The
        // permit is spent by the transfer that used it, so a job that comes back
        // not knowing whether its move happened cannot replay it - it has to ask
        // the container again, which re-reads it.
        NpcWorldEpoch world = Identities.AWorld();
        NpcContainerPermit permit = Permit(world);
        NpcTransferPlan plan = NpcTransferPlan.For(5, 5, 5);

        ContainerMoveResult first = ContainerMoveResult.Record(permit, world, plan, 5, 5);
        ContainerMoveResult replay = ContainerMoveResult.Record(permit, world, plan, 5, 5);

        Assert.Equal(ContainerMoveOutcome.Completed, first.Outcome);
        Assert.Equal(5, first.Moved);
        Assert.Equal(ContainerMoveOutcome.Refused, replay.Outcome);
        Assert.Equal(0, replay.Moved);
        Assert.Contains("already used", replay.Evidence);
    }

    [Fact]
    public void APermitFromAPreviousWorldLoadRecordsNothing()
    {
        NpcWorldEpoch yesterday = Identities.AWorld();
        NpcContainerPermit permit = Permit(yesterday);

        ContainerMoveResult receipt = ContainerMoveResult.Record(
            permit, Identities.AWorld(), NpcTransferPlan.For(5, 5, 5), 5, 5);

        Assert.Equal(ContainerMoveOutcome.Refused, receipt.Outcome);
        Assert.False(permit.IsSpent);
    }

    [Fact]
    public void TwoSidesThatDoNotAgreeAreUncertainAndNothingIsPutRightAutomatically()
    {
        // Fewer arrived than left is material lost; more arrived than left is
        // material minted. Both are uncertain, nothing is credited, and there is
        // no compensating write anywhere in this type.
        NpcWorldEpoch world = Identities.AWorld();
        NpcTransferPlan plan = NpcTransferPlan.For(5, 5, 5);

        ContainerMoveResult lost = ContainerMoveResult.Record(Permit(world), world, plan, 5, 3);
        ContainerMoveResult minted = ContainerMoveResult.Record(Permit(world), world, plan, 3, 5);

        Assert.Equal(ContainerMoveOutcome.Uncertain, lost.Outcome);
        Assert.Equal(0, lost.Moved);
        Assert.Equal(2, lost.Discrepancy);

        Assert.Equal(ContainerMoveOutcome.Uncertain, minted.Outcome);
        Assert.Equal(0, minted.Moved);
        Assert.Equal(-2, minted.Discrepancy);
    }

    [Fact]
    public void MoreMovingThanWasPlannedIsUncertainBecauseSomethingElseWasWriting()
    {
        NpcWorldEpoch world = Identities.AWorld();

        ContainerMoveResult receipt = ContainerMoveResult.Record(
            Permit(world), world, NpcTransferPlan.For(5, 5, 5), 9, 9);

        Assert.Equal(ContainerMoveOutcome.Uncertain, receipt.Outcome);
        Assert.Equal(0, receipt.Moved);
    }

    [Fact]
    public void PartialAndEmptyMovesAreOrdinaryResultsRatherThanFailures()
    {
        NpcWorldEpoch world = Identities.AWorld();
        NpcTransferPlan plan = NpcTransferPlan.For(5, 5, 5);

        ContainerMoveResult partial = ContainerMoveResult.Record(Permit(world), world, plan, 3, 3);
        ContainerMoveResult nothing = ContainerMoveResult.Record(Permit(world), world, plan, 0, 0);

        Assert.Equal(ContainerMoveOutcome.Partial, partial.Outcome);
        Assert.Equal(3, partial.Moved);
        Assert.True(partial.IsSettled);

        Assert.Equal(ContainerMoveOutcome.Nothing, nothing.Outcome);
        Assert.Equal(0, nothing.Moved);
        Assert.True(nothing.IsSettled);
    }

    [Fact]
    public void ACountThatMovedTheWrongWayIsUncertain()
    {
        NpcWorldEpoch world = Identities.AWorld();

        ContainerMoveResult receipt = ContainerMoveResult.Record(
            Permit(world), world, NpcTransferPlan.For(5, 5, 5), -1, 0);

        Assert.Equal(ContainerMoveOutcome.Uncertain, receipt.Outcome);
        Assert.Equal(0, receipt.Moved);
    }

    [Fact]
    public void ADefaultReceiptIsNotASuccess()
    {
        ContainerMoveResult receipt = default;

        Assert.Equal(ContainerMoveOutcome.Unspecified, receipt.Outcome);
        Assert.False(receipt.IsSettled);
        Assert.Equal(0, receipt.Moved);
    }
}
