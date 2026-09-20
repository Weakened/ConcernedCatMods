using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

namespace ConcernedTeamster.Tests;

/// <summary>The worker body's own inventory record (#381).
///
/// <b>The defect these exist for.</b> The game never saves a non-player
/// character's inventory, and the body itself <i>is</i> saved - Gunnar stays in a
/// world until he is retired. So a stone he picked up was destroyed by a zone
/// unload, a relog or a world reload while the body came back empty: no refusal,
/// no record, no drop. That is the same loss the retire verb refuses, reached by
/// a door nobody has to open.
///
/// <b>Zero migration is the load rule.</b> A body saved before the field existed
/// has no stored package, and that must be an ordinary empty inventory - never a
/// refusal, never a fault.</summary>
public sealed class WorkerInventoryRecordTests
{
    [Fact]
    public void ABodySavedBeforeTheFieldExisted_LoadsEmptyRatherThanFaulting()
    {
        Assert.Equal(WorkerRecordLoad.LoadEmpty, WorkerInventoryRecord.Decide(null));
    }

    [Fact]
    public void AnEmptyStoredPackage_IsAlsoJustAnEmptyInventory()
    {
        Assert.Equal(WorkerRecordLoad.LoadEmpty, WorkerInventoryRecord.Decide(new byte[0]));
    }

    [Fact]
    public void AStoredPackage_IsLoaded()
    {
        Assert.Equal(WorkerRecordLoad.LoadStored, WorkerInventoryRecord.Decide(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void NoReadEverRefuses()
    {
        // The whole point of the rule: reading the record decides how to load,
        // never whether to work. A refusal here would strand a perfectly good
        // body saved by an older build.
        foreach (byte[]? stored in new byte[]?[] { null, new byte[0], new byte[] { 0 }, new byte[512] })
        {
            WorkerRecordLoad load = WorkerInventoryRecord.Decide(stored);
            Assert.True(
                load == WorkerRecordLoad.LoadEmpty || load == WorkerRecordLoad.LoadStored,
                "reading the stored inventory must always end in a load");
            Assert.NotEqual(WorkerRecordLoad.Unspecified, load);
        }
    }

    [Fact]
    public void RevisionZeroMeansItHasNeverSaved()
    {
        // Starting at one keeps "never written" distinguishable from "written
        // once", which is what makes the field worth reading at all.
        Assert.Equal(1, WorkerInventoryRecord.Next(0));
        Assert.Equal(2, WorkerInventoryRecord.Next(1));
        Assert.Equal(1, WorkerInventoryRecord.Next(-7));
    }

    // -- a change that did not reach the object (#381, review minor 4) --
    //
    // `LastChangePersisted` was written and never read: the record knew a write
    // had failed and nothing asked. So a pick whose write failed was followed by
    // another pick, and another, each adding to a live inventory the stored
    // package was no longer keeping up with, and every one of them lost on the
    // next load. `Trust` is what both call sites now ask, and these are its
    // tests.

    [Fact]
    public void ABodyWhoseLastChangeDidNotReachItsObject_IsNotHandedAnythingMore()
    {
        Assert.Equal(WorkerRecordTrust.Refuses, WorkerInventoryRecord.Trust(
            hasRecord: true, isLoaded: true, lastChangePersisted: false));
    }

    [Fact]
    public void TheRefusalIsNotSticky_AChangeThatDoesPersistIsTrustedAgain()
    {
        // The property that matters is that this holds no memory: asked again
        // with the flag back up, the same body is trusted again. A caller that
        // remembered the refusal would latch a body off for the rest of the
        // session, which is not what a failed write justifies.
        Assert.Equal(WorkerRecordTrust.Refuses, WorkerInventoryRecord.Trust(true, true, false));
        Assert.Equal(WorkerRecordTrust.Trusted, WorkerInventoryRecord.Trust(true, true, true));
        Assert.Equal(WorkerRecordTrust.Refuses, WorkerInventoryRecord.Trust(true, true, false));
        Assert.Equal(WorkerRecordTrust.Trusted, WorkerInventoryRecord.Trust(true, true, true));
    }

    [Fact]
    public void OnlyAllThreeTogether_IsTrusted()
    {
        // Exhaustive, because each of the three refuses for its own reason and a
        // future edit that drops one would otherwise pass every other test here.
        foreach (bool hasRecord in new[] { false, true })
        {
            foreach (bool isLoaded in new[] { false, true })
            {
                foreach (bool persisted in new[] { false, true })
                {
                    WorkerRecordTrust trust = WorkerInventoryRecord.Trust(hasRecord, isLoaded, persisted);
                    Assert.Equal(
                        hasRecord && isLoaded && persisted
                            ? WorkerRecordTrust.Trusted
                            : WorkerRecordTrust.Refuses,
                        trust);
                    Assert.NotEqual(WorkerRecordTrust.Unspecified, trust);
                }
            }
        }
    }

    [Fact]
    public void TheUnspecifiedVerdictIsZeroAndIsNotTrust()
    {
        // A state nobody decided must not be pickable into, so the default value
        // has to be the refusing one.
        Assert.Equal(WorkerRecordTrust.Unspecified, default(WorkerRecordTrust));
        Assert.NotEqual(WorkerRecordTrust.Trusted, default(WorkerRecordTrust));
    }

    // What no test here establishes, stated rather than left to be assumed: that
    // a refused body ever becomes trusted again in a running game. `Trust` is
    // memoryless, which is the part that can be decided without the game, but
    // the only inventory change this mod makes is a pick, and the refusal is
    // what stops the next one - so nothing this mod does will clear the flag. A
    // zone load re-creates the record clean, from the last package that did get
    // written; the units in the change that failed are not in that package and
    // are lost. GUNNAR_COLLECTION.md §6c says so in those words.

    [Fact]
    public void TheFieldNames_AreForemansOwnSpellingAndMustNotDrift()
    {
        // Byte-compatible with Concerned Foreman's worker on purpose: same field
        // names, same package, same companion revision. A second spelling for the
        // same thing would be a second format to keep in step forever.
        Assert.Equal("tcc.worker.inventory", GunnarHaulingDefaults.WorkerInventoryField);
        Assert.Equal("tcc.worker.revision", GunnarHaulingDefaults.WorkerRevisionField);
    }

    [Fact]
    public void EveryDurableWorkerField_LivesUnderTheOneKeyPrefix()
    {
        foreach (string field in new[]
                 {
                     GunnarHaulingDefaults.WorkerKeyField,
                     GunnarHaulingDefaults.WorkerInventoryField,
                     GunnarHaulingDefaults.WorkerRevisionField,
                 })
        {
            Assert.StartsWith(GunnarHaulingDefaults.WorkerKeyPrefix, field, System.StringComparison.Ordinal);
        }
    }
}
