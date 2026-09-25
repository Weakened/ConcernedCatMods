using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>#374: a player's container permissions, reachable from a product.
///
/// <b>What makes these tests worth more than the library's own.</b>
/// `ConcernedNPC.Tests` already proves the permission model thoroughly - four
/// states, off by default, refusal paths both ways, conservation across an
/// interrupted transfer - and it does it by compiling the library's sources into
/// the test assembly, so `internal` is visible to it. That proves the model and
/// says nothing about whether a product can reach it. Before #374 no product
/// could: every type under `Storage` was internal, there was no
/// `InternalsVisibleTo` (and `SourceAuditTests` forbids one), and nothing in the
/// repository imported the namespace. "Off by default" was therefore a statement
/// about dead code.
///
/// This file is in a test project that references ConcernedNPC as an
/// <b>assembly</b>, not as source. Everything below is written against what a
/// product can actually see, so if the facade stopped being public these tests
/// would not compile.</summary>
public class ContainerPermissionTests
{
    private static readonly NpcPoint Chest = new NpcPoint(120.5f, 31.25f, -840.75f);
    private const int ChestPrefab = 1234567;

    // ------------------------------------------------------------------
    // The desk, across the assembly boundary
    // ------------------------------------------------------------------

    [Fact]
    public void AFreshDesk_HasNothingEnabled()
    {
        var desk = new NpcContainerDesk();

        Assert.Equal(0, desk.Count);
        Assert.False(desk.IsDirty);
        Assert.Equal(NpcContainerUse.Off, desk.Allowance(Chest, ChestPrefab));
        Assert.Empty(desk.Decisions);
    }

    [Fact]
    public void TheCycleIsTheOneOrderEveryRoleAgreesOn()
    {
        Assert.Equal(NpcContainerUse.Take, NpcContainerDesk.NextState(NpcContainerUse.Off));
        Assert.Equal(NpcContainerUse.Deposit, NpcContainerDesk.NextState(NpcContainerUse.Take));
        Assert.Equal(NpcContainerUse.Both, NpcContainerDesk.NextState(NpcContainerUse.Deposit));
        Assert.Equal(NpcContainerUse.Off, NpcContainerDesk.NextState(NpcContainerUse.Both));
    }

    [Fact]
    public void CyclingAChestWalksTheFourStatesAndForgetsItAtOff()
    {
        var desk = new NpcContainerDesk();

        Assert.Equal(NpcContainerUse.Take, desk.Cycle(Chest, ChestPrefab));
        Assert.Equal(1, desk.Count);
        Assert.Equal(NpcContainerUse.Deposit, desk.Cycle(Chest, ChestPrefab));
        Assert.Equal(NpcContainerUse.Both, desk.Cycle(Chest, ChestPrefab));

        // Off is the absence of a record, which is what makes an empty store
        // mean "nothing is enabled" rather than "nothing is known".
        Assert.Equal(NpcContainerUse.Off, desk.Cycle(Chest, ChestPrefab));
        Assert.Equal(0, desk.Count);
        Assert.Empty(desk.Decisions);
    }

    [Fact]
    public void AContainerWithNoRealPosition_CanNeverBeEnabled()
    {
        var desk = new NpcContainerDesk();
        var nowhere = new NpcPoint(float.NaN, 0f, 0f);

        Assert.False(NpcContainerDesk.CanBeRemembered(nowhere));
        Assert.False(desk.SetAllowance(nowhere, ChestPrefab, NpcContainerUse.Both));
        Assert.Equal(0, desk.Count);
    }

    [Fact]
    public void ADifferentPieceOnTheSameSpot_IsADifferentDecision()
    {
        var desk = new NpcContainerDesk();
        desk.SetAllowance(Chest, ChestPrefab, NpcContainerUse.Both);

        Assert.Equal(NpcContainerUse.Off, desk.Allowance(Chest, ChestPrefab + 1));
    }

    // ------------------------------------------------------------------
    // Persistence: the product owns the spelling (#374)
    // ------------------------------------------------------------------

    [Fact]
    public void ADecisionSurvivesASaveAndALoad()
    {
        var desk = new NpcContainerDesk();
        desk.SetAllowance(Chest, ChestPrefab, NpcContainerUse.Deposit);
        Assert.True(desk.IsDirty);

        string written = ContainerPermissionRows.Serialize(desk.Decisions);
        desk.MarkClean();
        Assert.False(desk.IsDirty);

        var read = ContainerPermissionRows.Deserialize(written, out int dropped);
        Assert.Equal(0, dropped);
        NpcContainerDesk reloaded = NpcContainerDesk.Restore(read, out int droppedOnRestore);

        Assert.Equal(0, droppedOnRestore);
        Assert.Equal(1, reloaded.Count);
        Assert.Equal(NpcContainerUse.Deposit, reloaded.Allowance(Chest, ChestPrefab));
        // Restoring is not a change, so a load does not schedule a save.
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    public void AChestIsFoundAgainAFewCentimetresOff_AndNotAMetreOff()
    {
        // A rebuilt piece snaps to the same socket but not to the same float.
        var desk = new NpcContainerDesk();
        desk.SetAllowance(Chest, ChestPrefab, NpcContainerUse.Both);

        var read = ContainerPermissionRows.Deserialize(
            ContainerPermissionRows.Serialize(desk.Decisions), out _);
        NpcContainerDesk reloaded = NpcContainerDesk.Restore(read, out _);

        var nudged = new NpcPoint(Chest.X + 0.1f, Chest.Y + 0.2f, Chest.Z - 0.1f);
        Assert.Equal(NpcContainerUse.Both, reloaded.Allowance(nudged, ChestPrefab));

        var elsewhere = new NpcPoint(Chest.X + 1.5f, Chest.Y, Chest.Z);
        Assert.Equal(NpcContainerUse.Off, reloaded.Allowance(elsewhere, ChestPrefab));
    }

    [Fact]
    public void PositionsRoundTripExactly()
    {
        // `R` rather than a fixed number of decimals, because a permission is
        // matched within a tolerance and a value that loses its last digit every
        // save can walk out of that tolerance one save at a time.
        var awkward = new NpcPoint(1234.5679f, -0.000123f, 98765.43f);
        var desk = new NpcContainerDesk();
        desk.SetAllowance(awkward, ChestPrefab, NpcContainerUse.Take);

        var read = ContainerPermissionRows.Deserialize(
            ContainerPermissionRows.Serialize(desk.Decisions), out _);

        Assert.Equal(awkward.X, read[0].X);
        Assert.Equal(awkward.Y, read[0].Y);
        Assert.Equal(awkward.Z, read[0].Z);
    }

    [Fact]
    public void AnEmptyDeskStillWritesAHeader()
    {
        // A file with nothing but a header means "nothing is enabled", which is a
        // different fact from no file at all.
        string written = ContainerPermissionRows.Serialize(new NpcContainerDesk().Decisions);

        Assert.StartsWith(ContainerPermissionRows.Header, written);
        Assert.Empty(ContainerPermissionRows.Deserialize(written, out int dropped));
        Assert.Equal(0, dropped);
    }

    [Theory]
    // Every unreadable row is DROPPED and counted, never guessed at: a dropped
    // row is a forgotten permission, which only ever keeps an NPC out of a
    // chest. Guessing at a half-read row puts one inside it.
    [InlineData("nonsense")]
    [InlineData("1\t2\t3\t4")]
    [InlineData("1\t2\t3\t4\t5\t6")]
    [InlineData("x\t2\t3\t4\tboth")]
    [InlineData("1\t2\t3\tnotanumber\tboth")]
    [InlineData("1\t2\t3\t4\tsomethingelse")]
    [InlineData("NaN\t2\t3\t4\tboth")]
    [InlineData("Infinity\t2\t3\t4\tboth")]
    // `off` is the absence of a record, so a row claiming it should not exist.
    [InlineData("1\t2\t3\t4\toff")]
    public void AnUnreadableRowIsDroppedRatherThanGuessedAt(string row)
    {
        var decisions = ContainerPermissionRows.Deserialize(
            ContainerPermissionRows.Header + "\n" + row + "\n", out int dropped);

        Assert.Empty(decisions);
        Assert.Equal(1, dropped);
    }

    [Fact]
    public void AGoodRowBesideABadOneStillLoads()
    {
        var desk = new NpcContainerDesk();
        desk.SetAllowance(Chest, ChestPrefab, NpcContainerUse.Both);
        string good = ContainerPermissionRows.Serialize(desk.Decisions);

        var decisions = ContainerPermissionRows.Deserialize(
            good + "this row is broken\n", out int dropped);

        Assert.Single(decisions);
        Assert.Equal(1, dropped);
        Assert.Equal(NpcContainerUse.Both, NpcContainerDesk.Restore(decisions, out _)
            .Allowance(Chest, ChestPrefab));
    }

    [Fact]
    public void NoContentAtAllIsAFreshWorld_NotAnError()
    {
        Assert.Empty(ContainerPermissionRows.Deserialize(null, out int droppedForNull));
        Assert.Equal(0, droppedForNull);
        Assert.Empty(ContainerPermissionRows.Deserialize("", out int droppedForEmpty));
        Assert.Equal(0, droppedForEmpty);
    }

    [Fact]
    public void TheFourStatesAreSpelledOutForAPlayerWhoOpensTheFile()
    {
        Assert.Equal("off", ContainerPermissionRows.Name(NpcContainerUse.Off));
        Assert.Equal("take", ContainerPermissionRows.Name(NpcContainerUse.Take));
        Assert.Equal("deposit", ContainerPermissionRows.Name(NpcContainerUse.Deposit));
        Assert.Equal("both", ContainerPermissionRows.Name(NpcContainerUse.Both));
    }

    [Fact]
    public void EveryStateRoundTripsThroughItsOwnName()
    {
        foreach (NpcContainerUse use in new[]
                 { NpcContainerUse.Take, NpcContainerUse.Deposit, NpcContainerUse.Both })
        {
            var desk = new NpcContainerDesk();
            desk.SetAllowance(Chest, ChestPrefab, use);

            var read = ContainerPermissionRows.Deserialize(
                ContainerPermissionRows.Serialize(desk.Decisions), out int dropped);

            Assert.Equal(0, dropped);
            Assert.Equal(use, NpcContainerDesk.Restore(read, out _).Allowance(Chest, ChestPrefab));
        }
    }
}
