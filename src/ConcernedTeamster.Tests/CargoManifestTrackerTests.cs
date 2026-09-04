using TheConcernedCat.ConcernedTeamster.Domain.Cargo;

namespace ConcernedTeamster.Tests;

/// <summary>CT-006: the tracker bounds refresh cost — call-count assertions
/// prove the reader runs at most once per spacing window no matter how often
/// consumers ask, and reset/null-reader paths clear the cache.</summary>
public class CargoManifestTrackerTests
{
    [Fact]
    public void GetOrRefresh_WithinSpacing_DoesNotCallReaderAgain()
    {
        var tracker = new CargoManifestTracker(1.0);
        int reads = 0;
        CargoManifest? Read(double now)
        {
            reads++;
            return CargoManifest.CreateEmpty(now);
        }

        tracker.GetOrRefresh(10.0, Read);
        for (double now = 10.05; now < 10.95; now += 0.05)
        {
            tracker.GetOrRefresh(now, Read);
        }

        Assert.Equal(1, reads);
    }

    [Fact]
    public void GetOrRefresh_AfterSpacing_RefreshesOnce()
    {
        var tracker = new CargoManifestTracker(1.0);
        int reads = 0;
        CargoManifest? Read(double now)
        {
            reads++;
            return CargoManifest.CreateEmpty(now);
        }

        tracker.GetOrRefresh(10.0, Read);
        CargoManifest? refreshed = tracker.GetOrRefresh(11.0, Read);

        Assert.Equal(2, reads);
        Assert.NotNull(refreshed);
        Assert.Equal(11.0, refreshed!.CaptureTimeSeconds);
    }

    [Fact]
    public void GetOrRefresh_NullReaderResult_ClearsTheCache()
    {
        var tracker = new CargoManifestTracker(1.0);

        tracker.GetOrRefresh(10.0, now => CargoManifest.CreateEmpty(now));
        Assert.NotNull(tracker.Current);

        tracker.GetOrRefresh(11.0, _ => null);
        Assert.Null(tracker.Current);
        // And consumers between refreshes see the cleared state, not stale
        // cargo from a cart that no longer exists.
        Assert.Null(tracker.GetOrRefresh(11.5, now => CargoManifest.CreateEmpty(now)));
    }

    [Fact]
    public void Reset_ForcesTheNextRequestToRefresh()
    {
        var tracker = new CargoManifestTracker(1.0);
        int reads = 0;
        CargoManifest? Read(double now)
        {
            reads++;
            return CargoManifest.CreateEmpty(now);
        }

        tracker.GetOrRefresh(10.0, Read);
        tracker.Reset();
        Assert.Null(tracker.Current);

        tracker.GetOrRefresh(10.1, Read);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Constructor_ClampsSpacingToTheFloor()
    {
        var tracker = new CargoManifestTracker(0.01);
        int reads = 0;
        CargoManifest? Read(double now)
        {
            reads++;
            return CargoManifest.CreateEmpty(now);
        }

        tracker.GetOrRefresh(10.0, Read);
        tracker.GetOrRefresh(10.5, Read);   // under the 1 s floor: no call

        Assert.Equal(1, reads);
    }
}
