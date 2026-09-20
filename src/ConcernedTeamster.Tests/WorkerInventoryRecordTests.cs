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
