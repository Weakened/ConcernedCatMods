using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class PinStoreTests
{
    private static DateTime _now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
    private static PinStore NewStore() => new(() => _now);

    [Fact]
    public void Create_AssignsIdentityRevisionAndTimestamps()
    {
        var store = NewStore();
        AtlasPin pin = store.Create(p => p.Name = "Harbor");

        Assert.Equal(AtlasId.PinKind, pin.Id.Kind);
        Assert.False(pin.Id.IsEmpty);
        Assert.Equal(1, pin.Revision);
        Assert.Equal(_now, pin.CreatedUtc);
        Assert.Equal(_now, pin.ModifiedUtc);
        Assert.True(store.IsDirty);
    }

    [Fact]
    public void Mutate_PreservesIdentity_BumpsRevisionMonotonically()
    {
        var store = NewStore();
        AtlasPin pin = store.Create(p => p.Name = "Old");
        AtlasId id = pin.Id;

        _now = _now.AddMinutes(5);
        Assert.True(store.Mutate(id, p => p.Name = "New"));
        Assert.True(store.Mutate(id, p => p.Checked = true));

        Assert.True(store.TryGet(id, out AtlasPin same));
        Assert.Same(pin, same);
        Assert.Equal("New", same.Name);
        Assert.Equal(3, same.Revision);
        Assert.Equal(_now, same.ModifiedUtc);
    }

    [Fact]
    public void Delete_LeavesADurableTombstone_AndRestoreUndeletes()
    {
        var store = NewStore();
        AtlasPin pin = store.Create(p => p.Name = "Doomed");

        Assert.True(store.Delete(pin.Id));
        Assert.True(pin.Deleted);
        Assert.NotNull(pin.DeletedUtc);
        Assert.Empty(store.Living);
        Assert.Single(store.Tombstones);
        Assert.Equal(1, store.Count);

        Assert.True(store.Restore(pin.Id));
        Assert.False(pin.Deleted);
        Assert.Null(pin.DeletedUtc);
        Assert.Single(store.Living);
        Assert.Equal(3, pin.Revision);
    }

    [Fact]
    public void Restore_OnLivingPin_Fails()
    {
        var store = NewStore();
        AtlasPin pin = store.Create();
        Assert.False(store.Restore(pin.Id));
    }

    [Fact]
    public void Upsert_HigherRevisionWins_LowerAndEqualAreIgnored()
    {
        var store = NewStore();
        AtlasPin pin = store.Create(p => p.Name = "Local");
        store.Mutate(pin.Id, p => p.Name = "Local2");

        AtlasPin stale = pin.Clone();
        stale.Revision = 1;
        stale.Name = "Stale";
        Assert.False(store.Upsert(stale));
        Assert.Equal("Local2", pin.Name);

        AtlasPin equal = pin.Clone();
        equal.Name = "Equal";
        Assert.False(store.Upsert(equal));
        Assert.Equal("Local2", pin.Name);

        AtlasPin newer = pin.Clone();
        newer.Revision = 10;
        newer.Name = "Newer";
        Assert.True(store.Upsert(newer));
        Assert.Equal("Newer", pin.Name);
        Assert.Equal(10, pin.Revision);
    }

    [Fact]
    public void Upsert_DeletedTombstone_DoesNotResurrectViaStaleWriter()
    {
        var store = NewStore();
        AtlasPin pin = store.Create(p => p.Name = "Shared");
        AtlasPin staleCopy = pin.Clone();

        store.Delete(pin.Id);

        // A stale peer that never saw the deletion pushes its old living copy.
        Assert.False(store.Upsert(staleCopy));
        Assert.True(pin.Deleted);
    }

    [Fact]
    public void ChangeStream_PublishesEveryPersistedChange()
    {
        var store = NewStore();
        int events = 0;
        store.Changed += _ => events++;

        AtlasPin pin = store.Create();
        store.Mutate(pin.Id, p => p.Name = "x");
        store.Delete(pin.Id);

        Assert.Equal(3, events);
    }

    [Fact]
    public void PurgeTombstones_RemovesOnlyExpiredTombstones()
    {
        var store = NewStore();
        AtlasPin living = store.Create(p => p.Name = "Alive");
        AtlasPin oldDead = store.Create(p => p.Name = "OldDead");
        AtlasPin newDead = store.Create(p => p.Name = "NewDead");
        store.Delete(oldDead.Id);
        _now = _now.AddDays(40);
        store.Delete(newDead.Id);

        int purged = store.PurgeTombstones(TimeSpan.FromDays(30));

        Assert.Equal(1, purged);
        Assert.False(store.TryGet(oldDead.Id, out _));
        Assert.True(store.TryGet(newDead.Id, out _));
        Assert.True(store.TryGet(living.Id, out _));
    }

    [Fact]
    public void LoadedStore_StartsClean()
    {
        var source = NewStore();
        source.Create(p => p.Name = "A");
        var loaded = new PinStore(CloneAll(source), () => _now);

        Assert.False(loaded.IsDirty);
        Assert.Equal(1, loaded.Count);
    }

    private static List<AtlasPin> CloneAll(PinStore store)
    {
        var pins = new List<AtlasPin>();
        foreach (AtlasPin pin in store.All)
        {
            pins.Add(pin.Clone());
        }

        return pins;
    }
}

public class AtlasIdTests
{
    [Fact]
    public void Roundtrip_ParsesItsOwnToString()
    {
        AtlasId id = AtlasId.NewPin();
        Assert.True(AtlasId.TryParse(id.ToString(), out AtlasId parsed));
        Assert.Equal(id, parsed);
        Assert.StartsWith("cc:pin:", id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("cc:pin")]
    [InlineData("xx:pin:0123456789abcdef0123456789abcdef")]
    [InlineData("cc::0123456789abcdef0123456789abcdef")]
    [InlineData("cc:pin:not-a-guid")]
    public void Malformed_IsRejected(string text)
    {
        Assert.False(AtlasId.TryParse(text, out _));
    }
}

public class AtlasTextTests
{
    [Theory]
    [InlineData("plain")]
    [InlineData("tabs\tand\nnewlines\rand % percent")]
    [InlineData("commas, everywhere, %2C literal")]
    [InlineData("")]
    public void EscapeUnescape_Roundtrips(string value)
    {
        Assert.Equal(value, AtlasText.Unescape(AtlasText.Escape(value)));
    }

    [Fact]
    public void EscapedText_ContainsNoRowBreakingCharacters()
    {
        string escaped = AtlasText.Escape("a\tb\nc\rd,e");
        Assert.DoesNotContain('\t', escaped);
        Assert.DoesNotContain('\n', escaped);
        Assert.DoesNotContain('\r', escaped);
        Assert.DoesNotContain(',', escaped);
    }

    [Fact]
    public void Tags_RoundtripWithCommasInside()
    {
        var tags = new List<string> { "iron, tin", "base", "  trimmed  " };
        List<string> parsed = AtlasText.SplitTags(AtlasText.JoinTags(tags));
        Assert.Equal(new[] { "iron, tin", "base", "trimmed" }, parsed);
    }
}
