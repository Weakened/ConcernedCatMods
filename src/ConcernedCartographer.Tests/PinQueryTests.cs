using System.Diagnostics;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class PinQueryTests
{
    private static AtlasPin Pin(Action<AtlasPin> init)
    {
        var pin = new AtlasPin(AtlasId.NewPin());
        init(pin);
        return pin;
    }

    private static readonly AtlasPin Harbor = Pin(p =>
    {
        p.Name = "North Harbor";
        p.Category = "Travel";
        p.IconId = "cc:harbor";
        p.Notes = "longship dock";
        p.Tags.AddRange(new[] { "sea", "trade" });
        p.Status = AtlasPinStatus.Done;
        p.Scope = AtlasScope.Table;
        p.Source = AtlasPinSource.AdoptedVanilla;
        p.Checked = true;
        p.Position = new RoadPoint(100f, 30f, 200f);
    });

    private static readonly AtlasPin Mine = Pin(p =>
    {
        p.Name = "Copper mine";
        p.Category = "Resources";
        p.IconId = "cc:resource";
        p.Tags.Add("copper");
        p.Status = AtlasPinStatus.Todo;
        p.Position = new RoadPoint(-500f, 30f, -500f);
    });

    [Theory]
    [InlineData("harbor", true, false)]
    [InlineData("HARBOR", true, false)]
    [InlineData("longship", true, false)]
    [InlineData("copper", false, true)]
    [InlineData("trade", true, false)]
    [InlineData("", true, true)]
    [InlineData("harbor copper", false, false)]
    public void PlainWords_SearchNameNotesTagsCategory(string query, bool matchesHarbor, bool matchesMine)
    {
        PinQuery parsed = PinQuery.Parse(query);
        Assert.Equal(matchesHarbor, parsed.Matches(Harbor));
        Assert.Equal(matchesMine, parsed.Matches(Mine));
    }

    [Theory]
    [InlineData("name:harbor", true, false)]
    [InlineData("category:travel", true, false)]
    [InlineData("tag:sea", true, false)]
    [InlineData("icon:resource", false, true)]
    [InlineData("status:done", true, false)]
    [InlineData("status:todo", false, true)]
    [InlineData("scope:table", true, false)]
    [InlineData("source:adoptedvanilla", true, false)]
    [InlineData("is:checked", true, false)]
    [InlineData("is:unchecked", false, true)]
    [InlineData("category:travel is:checked", true, false)]
    [InlineData("category:travel status:todo", false, false)]
    public void Tokens_NarrowSpecificFields(string query, bool matchesHarbor, bool matchesMine)
    {
        PinQuery parsed = PinQuery.Parse(query);
        Assert.Equal(matchesHarbor, parsed.Matches(Harbor));
        Assert.Equal(matchesMine, parsed.Matches(Mine));
    }

    [Fact]
    public void Near_FiltersByDistance()
    {
        Assert.True(PinQuery.Parse("near:0,0,400").Matches(Harbor));
        Assert.False(PinQuery.Parse("near:0,0,100").Matches(Harbor));
        Assert.True(PinQuery.Parse("near:-500,-500,10").Matches(Mine));
    }

    [Theory]
    [InlineData("bogus:value")]
    [InlineData("near:notanumber")]
    [InlineData("near:1,2")]
    public void MalformedTokens_DegradeToPlainWords_NeverThrow(string query)
    {
        PinQuery parsed = PinQuery.Parse(query);
        // Degraded to a word that matches nothing here - but never an error,
        // and clearing the query restores everything.
        Assert.False(parsed.Matches(Harbor));
        Assert.True(PinQuery.Parse("").Matches(Harbor));
    }

    [Fact]
    public void SearchTiming_TenThousandPins_IsInteractive()
    {
        var pins = new List<AtlasPin>();
        for (int i = 0; i < 10_000; i++)
        {
            int n = i;
            pins.Add(Pin(p =>
            {
                p.Name = $"Pin number {n}";
                p.Notes = n % 3 == 0 ? "iron deposit nearby" : "plain field";
                p.Tags.Add(n % 2 == 0 ? "even" : "odd");
                p.Position = new RoadPoint(n % 100 * 50f, 30f, n / 100 * 50f);
            }));
        }

        var stopwatch = Stopwatch.StartNew();
        List<AtlasPin> ironHits = PinQuery.Parse("iron tag:even").Filter(pins);
        long ms = stopwatch.ElapsedMilliseconds;

        Assert.True(ironHits.Count > 1000);
        Assert.True(ms < 500, $"query took {ms} ms");
    }
}

public class SavedViewStoreTests
{
    [Fact]
    public void SaveApplyRoundtrip_RestoresExactState()
    {
        var store = new SavedViewStore();
        store.Save(new SavedView("Trade run", "category:travel is:unchecked", showDirt: true, showPaved: false, showPins: true, clusterEnabled: true));
        store.Save(new SavedView("all off", "", false, false, false, false));

        SavedViewStore reloaded = SavedViewStore.Parse(store.Serialize(), out int malformed);

        Assert.Equal(0, malformed);
        Assert.Equal(2, reloaded.Views.Count);
        Assert.True(reloaded.TryGet("trade RUN", out SavedView view));
        Assert.Equal("category:travel is:unchecked", view.Query);
        Assert.True(view.ShowDirt);
        Assert.False(view.ShowPaved);
        Assert.True(view.ClusterEnabled);
    }

    [Fact]
    public void SavingSameName_Replaces_AndRemoveWorks()
    {
        var store = new SavedViewStore();
        store.Save(new SavedView("A", "one", true, true, true, false));
        store.Save(new SavedView("a", "two", false, false, false, false));

        Assert.Single(store.Views);
        Assert.True(store.TryGet("A", out SavedView view));
        Assert.Equal("two", view.Query);
        Assert.True(store.Remove("A"));
        Assert.Empty(store.Views);
    }

    [Fact]
    public void MalformedRows_AreSkipped()
    {
        var lines = new List<string>
        {
            SavedViewStore.Header,
            "Good\tquery\t1\t0\t1\t0\t1",
            "Bad\tquery\t1\t0\t1",
            "\tempty-name\t1\t0\t1\t0\t1",
        };

        SavedViewStore store = SavedViewStore.Parse(lines, out int malformed);

        Assert.Equal(2, malformed);
        Assert.Single(store.Views);
    }
}

public class PinClustererTests
{
    private static AtlasPin At(float x, float z, string icon = "vanilla:dot", string category = "")
    {
        var pin = new AtlasPin(AtlasId.NewPin()) { IconId = icon, Category = category };
        pin.Position = new RoadPoint(x, 30f, z);
        return pin;
    }

    [Fact]
    public void CrowdedCell_BecomesOneCluster_WithCentroidAndDominantIcon()
    {
        var pins = new List<AtlasPin>
        {
            At(10f, 10f, "cc:resource", "Resources"),
            At(20f, 10f, "cc:resource", "Resources"),
            At(30f, 10f, "vanilla:dot", "Resources"),
            At(900f, 900f),
        };

        PinClusterer.Result result = PinClusterer.Compute(pins, cellMeters: 100f, minClusterSize: 3);

        PinClusterer.Cluster cluster = Assert.Single(result.Clusters);
        Assert.Equal(3, cluster.Members.Count);
        Assert.Equal(20f, cluster.Center.X);
        Assert.Equal("cc:resource", cluster.DominantIconId);
        Assert.Equal("Resources", cluster.DominantCategory);
        Assert.Single(result.Singles);
    }

    [Fact]
    public void SmallGroups_StaySingles()
    {
        var pins = new List<AtlasPin> { At(0f, 0f), At(5f, 5f) };
        PinClusterer.Result result = PinClusterer.Compute(pins, 100f, minClusterSize: 3);
        Assert.Empty(result.Clusters);
        Assert.Equal(2, result.Singles.Count);
    }

    [Fact]
    public void AlwaysVisiblePins_AreNeverClustered()
    {
        var pins = new List<AtlasPin> { At(1f, 1f), At(2f, 2f), At(3f, 3f), At(4f, 4f) };
        var keep = new HashSet<Guid> { pins[0].Id.Value };

        PinClusterer.Result result = PinClusterer.Compute(pins, 100f, 3, keep);

        Assert.Contains(pins[0], result.Singles);
        PinClusterer.Cluster cluster = Assert.Single(result.Clusters);
        Assert.Equal(3, cluster.Members.Count);
    }

    [Fact]
    public void ClusteringNeverMutatesEntities()
    {
        AtlasPin pin = At(1f, 1f);
        long revision = pin.Revision;
        PinClusterer.Compute(new[] { pin, At(2f, 2f), At(3f, 3f) }, 100f);
        Assert.Equal(revision, pin.Revision);
        Assert.False(pin.Deleted);
    }

    [Fact]
    public void ZeroCellSize_DisablesClustering()
    {
        var pins = new List<AtlasPin> { At(1f, 1f), At(2f, 2f), At(3f, 3f) };
        PinClusterer.Result result = PinClusterer.Compute(pins, 0f);
        Assert.Empty(result.Clusters);
        Assert.Equal(3, result.Singles.Count);
    }

    [Fact]
    public void TenThousandPins_ClusterQuickly_AndDeterministically()
    {
        var pins = new List<AtlasPin>();
        var random = new Random(7);
        for (int i = 0; i < 10_000; i++)
        {
            pins.Add(At((float)(random.NextDouble() * 8000 - 4000), (float)(random.NextDouble() * 8000 - 4000)));
        }

        var stopwatch = Stopwatch.StartNew();
        PinClusterer.Result first = PinClusterer.Compute(pins, 256f);
        long ms = stopwatch.ElapsedMilliseconds;
        PinClusterer.Result second = PinClusterer.Compute(pins, 256f);

        Assert.True(ms < 1000, $"clustering took {ms} ms");
        Assert.Equal(first.Clusters.Count, second.Clusters.Count);
        Assert.Equal(first.Singles.Count, second.Singles.Count);
        int total = first.Singles.Count;
        foreach (PinClusterer.Cluster cluster in first.Clusters)
        {
            total += cluster.Members.Count;
        }

        Assert.Equal(10_000, total);
    }
}
