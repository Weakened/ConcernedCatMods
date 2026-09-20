using System;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace ConcernedTeamster.Tests;

/// <summary>Removing a worker body must not silently delete what it is carrying
/// (#381).
///
/// <b>The defect these exist for.</b> Retiring a body destroys its network
/// object, and a character's inventory lives in that object: nothing is dropped
/// on the ground. That was harmless until an ordered pick could put a stone into
/// Gunnar. `ct_haul retire` then became a way to delete gathered material
/// silently, and material conservation is this product's hard rule.
///
/// <b>Why refusal rather than dropping it.</b> Concerned Foreman's
/// `SETTLEMENT_AUTHORITY.md` §5a decided this shape already: <i>death</i> drops
/// through vanilla's own drop and records the units lost, while <i>despawn</i> -
/// the deliberate removal, which is what retire is - is refused while the worker
/// carries anything. A deliberate drop here would mean this product spawning item
/// instances, which no owner decision authorizes.
///
/// <b>Why the escape hatch can never be blocked.</b> Foreman pairs its refusal
/// with recovery commands; this slice has none, so a bare refusal would trap a
/// body - and retire is the only way to resolve a duplicate Gunnar.
/// <see cref="TheForcingWord_AlwaysGetsThrough"/> is that property.</summary>
public sealed class WorkerRetirementTests
{
    [Fact]
    public void CarryingSomething_RefusesRatherThanDestroyingIt()
    {
        // The row the whole guard exists for: a retire that proceeds here is a
        // retire that deletes gathered material with no record and no drop.
        RetireVerdict verdict = WorkerRetirement.Decide(forced: false, itemsHeld: 1, inventoryReadable: true);

        Assert.Equal(RetireVerdict.RefusedCarrying, verdict);
        Assert.False(
            WorkerRetirement.Allows(verdict),
            "a body carrying anything must not be removed without the player saying so");
    }

    [Fact]
    public void CarryingNothing_MayBeRetired()
    {
        RetireVerdict verdict = WorkerRetirement.Decide(forced: false, itemsHeld: 0, inventoryReadable: true);

        Assert.Equal(RetireVerdict.MayRetire, verdict);
        Assert.True(WorkerRetirement.Allows(verdict));
    }

    [Fact]
    public void AnUnreadableInventory_IsNotAnEmptyOne()
    {
        // Unknown refuses. "I could not count it" is not "there is nothing in
        // it", and treating them alike is the deleting direction.
        RetireVerdict verdict = WorkerRetirement.Decide(forced: false, itemsHeld: 0, inventoryReadable: false);

        Assert.Equal(RetireVerdict.RefusedUnreadable, verdict);
        Assert.False(WorkerRetirement.Allows(verdict));
    }

    [Fact]
    public void TheForcingWord_AlwaysGetsThrough()
    {
        // The anti-trap property. There is no way to empty a worker in this
        // slice, and retire is the only way to remove a duplicate body, so a
        // refusal that could not be overridden would be a worse defect than the
        // one being guarded.
        foreach (bool readable in new[] { true, false })
        {
            foreach (int held in new[] { -1, 0, 1, 2, 40 })
            {
                RetireVerdict verdict = WorkerRetirement.Decide(true, held, readable);
                Assert.True(
                    WorkerRetirement.Allows(verdict),
                    $"forced retire must never be refused (held={held}, readable={readable}) — " +
                    "otherwise the refusal traps the body");
            }
        }
    }

    [Fact]
    public void ForcingWhileCarrying_SaysTheMaterialIsLost()
    {
        RetireVerdict verdict = WorkerRetirement.Decide(forced: true, itemsHeld: 3, inventoryReadable: true);
        Assert.Equal(RetireVerdict.ForcedAndLost, verdict);

        string sentence = WorkerRetirement.Describe(verdict, 3);
        Assert.Contains("lost", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not dropped", sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForcingAnEmptyBody_IsJustAnOrdinaryRetire()
    {
        // Nothing to lose, so nothing is claimed to have been lost.
        Assert.Equal(
            RetireVerdict.MayRetire,
            WorkerRetirement.Decide(forced: true, itemsHeld: 0, inventoryReadable: true));
    }

    [Fact]
    public void ForcingAnUnreadableBody_ProceedsAndWarns()
    {
        Assert.Equal(
            RetireVerdict.ForcedAndLost,
            WorkerRetirement.Decide(forced: true, itemsHeld: 0, inventoryReadable: false));
    }

    [Fact]
    public void ARefusalNamesTheWayOut()
    {
        string refusal = WorkerRetirement.Describe(RetireVerdict.RefusedCarrying, 2);
        Assert.Contains(WorkerRetirement.ForcingWord, refusal, StringComparison.Ordinal);
        Assert.Contains("destroy", refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnspecifiedVerdict_NeverAllows()
    {
        Assert.False(WorkerRetirement.Allows(RetireVerdict.Unspecified));
        Assert.False(WorkerRetirement.Allows((RetireVerdict)97));
    }

    [Theory]
    [InlineData(true, new[] { "retire", "force" })]
    [InlineData(true, new[] { "retire", "--force" })]
    [InlineData(true, new[] { "retire", "FORCE" })]
    [InlineData(true, new[] { "retire", " force " })]
    [InlineData(false, new[] { "retire" })]
    [InlineData(false, new[] { "retire", "yes" })]
    [InlineData(false, new[] { "retire", "forced" })]
    [InlineData(false, new[] { "force" })]
    [InlineData(false, new string[0])]
    public void TheForcingWord_IsSpelledOrItIsNotForcing(bool expected, string[] args)
    {
        // `force` at index 0 is the subcommand itself, never the confirmation:
        // the word has to come after it.
        Assert.Equal(expected, WorkerRetirement.IsForcing(args, 1));
    }

    [Fact]
    public void NoArgumentsAtAll_IsNotForcing()
    {
        Assert.False(WorkerRetirement.IsForcing(null, 1));
        Assert.False(WorkerRetirement.IsForcing(new[] { "retire", null! }, 1));
    }
}
