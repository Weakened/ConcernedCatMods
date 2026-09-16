using System;
using System.IO;
using System.Linq;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests.Companions;

/// <summary>Which doors companions may use: closed to them unless their owner
/// says otherwise, remembered by where the door is, and never made more open by
/// a damaged file.</summary>
public sealed class DoorAccessTests
{
    private static readonly int WoodDoor = 1234567;

    private static DoorPlace Door(float x, float z, int prefab = 0, float y = 30f)
    {
        return new DoorPlace(new WorldPoint(x, y, z), prefab == 0 ? WoodDoor : prefab);
    }

    [Fact]
    public void ADoorIsClosedToCompanionsUntilItsOwnerOpensIt()
    {
        var book = new DoorAccessBook();
        Assert.False(book.IsAllowed(Door(1f, 2f), DoorAccessPolicy.OnlyAllowedDoors));

        Assert.True(book.Toggle(Door(1f, 2f)));
        Assert.True(book.IsAllowed(Door(1f, 2f), DoorAccessPolicy.OnlyAllowedDoors));

        Assert.False(book.Toggle(Door(1f, 2f)));
        Assert.False(book.IsAllowed(Door(1f, 2f), DoorAccessPolicy.OnlyAllowedDoors));
    }

    [Fact]
    public void TheAllDoorsPolicyOpensEveryDoorWithoutWritingAnything()
    {
        var book = new DoorAccessBook();
        Assert.True(book.IsAllowed(Door(9f, 9f), DoorAccessPolicy.AllDoors));
        Assert.Empty(book.Allowed);
        Assert.False(book.IsDirty);
    }

    [Fact]
    public void ADoorIsTheSameDoorWhereverItsNumberWentButNotNextDoor()
    {
        // The game renumbers every object on load, so the place is the name.
        var book = new DoorAccessBook();
        book.SetAllowed(Door(10f, 20f), true);

        Assert.True(book.IsAllowed(Door(10.2f, 20.1f), DoorAccessPolicy.OnlyAllowedDoors));

        // The door beside it in the same wall is its own door.
        Assert.False(book.IsAllowed(Door(12f, 20f), DoorAccessPolicy.OnlyAllowedDoors));

        // So is a different kind of door standing in the same place, and a door
        // on the floor above.
        Assert.False(book.IsAllowed(Door(10f, 20f, prefab: 42), DoorAccessPolicy.OnlyAllowedDoors));
        Assert.False(book.IsAllowed(Door(10f, 20f, y: 33f), DoorAccessPolicy.OnlyAllowedDoors));
    }

    [Fact]
    public void AllowingTwiceIsOneEntryAndOnlyARealChangeIsDirty()
    {
        var book = new DoorAccessBook();
        Assert.True(book.SetAllowed(Door(1f, 1f), true));
        book.MarkClean();

        Assert.False(book.SetAllowed(Door(1.1f, 1f), true));
        Assert.False(book.IsDirty);
        Assert.Single(book.Allowed);

        Assert.Equal(1, book.Clear());
        Assert.True(book.IsDirty);
        Assert.Equal(0, book.Clear());
    }

    [Fact]
    public void TheFileRoundTripsAndADamagedLineOnlyForgetsThatDoor()
    {
        var book = new DoorAccessBook();
        book.SetAllowed(Door(1.5f, -2.25f), true);
        book.SetAllowed(Door(-40.125f, 17f, prefab: 99), true);

        var lines = DoorAccessCodec.Serialize(book).ToList();
        Assert.Equal(DoorAccessCodec.Header, lines[0]);

        lines.Add("door\tnot-a-number\t1\t2\t3");
        lines.Add("door\t5\t1");
        lines.Add("someday\ta row a later version adds");

        DoorAccessBook read = DoorAccessCodec.Parse(lines, out int skipped);
        Assert.Equal(2, skipped);
        Assert.Equal(2, read.Allowed.Count);
        Assert.False(read.IsDirty);
        Assert.True(read.IsAllowed(Door(1.5f, -2.25f), DoorAccessPolicy.OnlyAllowedDoors));
        Assert.True(read.IsAllowed(Door(-40.125f, 17f, prefab: 99), DoorAccessPolicy.OnlyAllowedDoors));
    }

    [Fact]
    public void TheStoreKeepsOneFilePerWorldAndNeverOverwritesOneItCouldNotRead()
    {
        string root = Path.Combine(Path.GetTempPath(), "cc-doors-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DoorAccessStore(root);
            var world = new WorldId(0x1122334455667788);
            var other = new WorldId(7);

            DoorAccessStore.LoadReport empty = store.Load(world);
            Assert.Empty(empty.Book.Allowed);
            Assert.False(empty.ReadOnly);

            empty.Book.SetAllowed(Door(3f, 4f), true);
            Assert.Null(store.Save(world, empty.Book, empty.ReadOnly));
            Assert.False(empty.Book.IsDirty);

            Assert.Single(store.Load(world).Book.Allowed);
            Assert.Empty(store.Load(other).Book.Allowed);

            // Nothing changed, nothing written.
            Assert.Null(store.Save(world, store.Load(world).Book, readOnly: false));

            // A book loaded read-only is refused, and says so.
            var refused = new DoorAccessBook();
            refused.SetAllowed(Door(8f, 8f), true);
            Assert.NotNull(store.Save(world, refused, readOnly: true));
            Assert.Single(store.Load(world).Book.Allowed);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
