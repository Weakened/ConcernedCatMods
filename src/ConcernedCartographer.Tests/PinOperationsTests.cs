using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class PinOperationsTests
{
    private static DateTime _now = new(2026, 8, 27, 6, 0, 0, DateTimeKind.Utc);

    private static (PinStore Store, PinOperations Ops) NewFixture()
    {
        var store = new PinStore(() => _now);
        return (store, new PinOperations(store));
    }

    private static AtlasPin Pin(PinStore store, string name, float x = 0f, float z = 0f, string icon = "vanilla:dot")
    {
        return store.Create(pin =>
        {
            pin.Name = name;
            pin.IconId = icon;
            pin.Position = new RoadPoint(x, 30f, z);
        });
    }

    [Fact]
    public void Move_PreservesIdentity_AndUndoRestoresPosition()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin pin = Pin(store, "Dock", 10f, 10f);
        long revisionBefore = pin.Revision;

        Assert.True(ops.Move(pin.Id, new RoadPoint(50f, 30f, 60f)));
        Assert.Equal(50f, pin.Position.X);

        Assert.True(ops.Undo(out _));
        Assert.Equal(10f, pin.Position.X);
        Assert.True(pin.Revision > revisionBefore);

        Assert.True(ops.Redo(out _));
        Assert.Equal(50f, pin.Position.X);
    }

    [Fact]
    public void UndoRedo_NeverRollRevisionsBackward()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin pin = Pin(store, "Rev");
        long last = pin.Revision;

        ops.Move(pin.Id, new RoadPoint(1f, 30f, 1f));
        Assert.True(pin.Revision > last);
        last = pin.Revision;

        ops.Undo(out _);
        Assert.True(pin.Revision > last);
        last = pin.Revision;

        ops.Redo(out _);
        Assert.True(pin.Revision > last);
    }

    [Fact]
    public void Duplicate_CreatesNewIdentity_WithOffsetAndManagedSource()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin original = Pin(store, "Camp", 5f, 5f, "vanilla:fire");
        original.Tags.Add("north");
        store.Mutate(original.Id, p => p.Source = AtlasPinSource.AdoptedVanilla);

        AtlasPin? copy = ops.Duplicate(original.Id);

        Assert.NotNull(copy);
        Assert.NotEqual(original.Id, copy!.Id);
        Assert.Equal("Camp (copy)", copy.Name);
        Assert.Equal("vanilla:fire", copy.IconId);
        Assert.Equal(new[] { "north" }, copy.Tags);
        Assert.Equal(AtlasPinSource.Managed, copy.Source);
        Assert.Equal(1, copy.Revision);
        Assert.True(copy.Position.X > original.Position.X);

        Assert.True(ops.Undo(out _));
        Assert.True(store.TryGet(copy.Id, out AtlasPin undone));
        Assert.True(undone.Deleted);
    }

    [Fact]
    public void DeleteAndRestore_AreUndoable()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin pin = Pin(store, "Fragile");

        Assert.True(ops.Delete(pin.Id));
        Assert.True(pin.Deleted);
        Assert.Contains(pin, ops.RecentlyDeleted());

        Assert.True(ops.Undo(out _));
        Assert.False(pin.Deleted);

        ops.Delete(pin.Id);
        Assert.True(ops.RestoreDeleted(pin.Id));
        Assert.False(pin.Deleted);
    }

    [Fact]
    public void BatchEdit_AppliesToAllSelected_AndUndoesAsOneStep()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin a = Pin(store, "A");
        AtlasPin b = Pin(store, "B");
        AtlasPin c = Pin(store, "C");
        store.Delete(c.Id);

        int edited = ops.BatchEdit(new[] { a.Id, b.Id, c.Id }, pin => pin.Category = "Farm");

        Assert.Equal(2, edited);
        Assert.Equal("Farm", a.Category);
        Assert.Equal("Farm", b.Category);
        Assert.Equal("", c.Category);

        Assert.True(ops.Undo(out _));
        Assert.Equal("", a.Category);
        Assert.Equal("", b.Category);
    }

    [Fact]
    public void FindDuplicateGroups_MatchesByProximityAndIconOrName()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin oldest = Pin(store, "Copper", 0f, 0f, "cc:resource");
        _now = _now.AddMinutes(1);
        Pin(store, "copper ", 4f, 0f, "vanilla:dot");
        _now = _now.AddMinutes(1);
        Pin(store, "Other", 6f, 0f, "cc:resource");
        _now = _now.AddMinutes(1);
        Pin(store, "Far", 500f, 0f, "cc:resource");

        List<List<AtlasPin>> groups = ops.FindDuplicateGroups(15f);

        List<AtlasPin> group = Assert.Single(groups);
        Assert.Equal(3, group.Count);
        Assert.Same(oldest, group[0]);
    }

    [Fact]
    public void Merge_UnionsTagsConcatsNotesKeepsEarliestCreated_AndTombstonesDuplicates()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin primary = Pin(store, "Mine");
        primary.Tags.Add("iron");
        store.Mutate(primary.Id, p => p.Notes = "main shaft");

        _now = _now.AddHours(-2);
        AtlasPin older = Pin(store, "Mine dup");
        older.Tags.Add("iron");
        older.Tags.Add("tin");
        store.Mutate(older.Id, p => p.Notes = "old survey");
        _now = _now.AddHours(2);

        Assert.True(ops.Merge(primary.Id, new[] { older.Id }));

        Assert.False(primary.Deleted);
        Assert.True(older.Deleted);
        Assert.Contains("iron", primary.Tags);
        Assert.Contains("tin", primary.Tags);
        Assert.Contains("main shaft", primary.Notes);
        Assert.Contains("old survey", primary.Notes);
        Assert.Contains("[merged " + older.Id, primary.Notes);
        Assert.Equal(older.CreatedUtc, primary.CreatedUtc);

        Assert.True(ops.Undo(out _));
        Assert.False(older.Deleted);
        Assert.DoesNotContain("tin", primary.Tags);
        Assert.Equal("main shaft", primary.Notes);
    }

    [Fact]
    public void NewOperation_ClearsRedo()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin pin = Pin(store, "R");
        ops.Move(pin.Id, new RoadPoint(1f, 30f, 0f));
        ops.Undo(out _);
        Assert.Equal(1, ops.RedoCount);

        ops.Move(pin.Id, new RoadPoint(2f, 30f, 0f));
        Assert.Equal(0, ops.RedoCount);
    }

    [Fact]
    public void UndoDepth_IsBounded()
    {
        (PinStore store, PinOperations ops) = NewFixture();
        AtlasPin pin = Pin(store, "Deep");
        for (int i = 0; i < 30; i++)
        {
            ops.Move(pin.Id, new RoadPoint(i, 30f, 0f));
        }

        Assert.Equal(20, ops.UndoCount);
    }

    [Fact]
    public void JournalReplayAfterUndo_ConvergesOnTheVisibleState()
    {
        // The full pipeline property: serialize every change row like the
        // journal does, replay them, and the replayed store must match the
        // undone (visible) state, not the undone-away edit.
        (PinStore store, PinOperations ops) = NewFixture();
        var journal = new List<string> { PinCodec.Header };
        store.Changed += pin => journal.Add(PinCodec.SerializeRow(pin));

        AtlasPin pin = Pin(store, "Converge");
        ops.Move(pin.Id, new RoadPoint(99f, 30f, 99f));
        ops.Undo(out _);

        PinCodec.ParseResult replay = PinCodec.Parse(journal);
        AtlasPin replayed = Assert.Single(replay.Pins);
        Assert.Equal(0f, replayed.Position.X);
        Assert.Equal(pin.Revision, replayed.Revision);
    }
}
