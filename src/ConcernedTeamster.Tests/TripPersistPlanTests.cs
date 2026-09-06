using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace ConcernedTeamster.Tests;

/// <summary>CT-039 / DEF-teamster-v0.4-001: the backup-before-rewrite
/// guarantee, proven directly against the decision function rather than
/// relying on review of TripRecordingService.Persist's control flow (which
/// cannot be exercised by an automated test — it is Adapters-layer and
/// BepInEx-bound). If a future change ever stops mandating a backup for a
/// refused, malformed, or migrating file, one of these tests fails.</summary>
public class TripPersistPlanTests
{
    private static TripSidecar.ParseResult Clean(int tripCount = 1)
    {
        var trips = new List<TheConcernedCat.ConcernedTeamster.Domain.Trips.Trip>();
        for (int index = 0; index < tripCount; index++)
        {
            trips.Add(new TheConcernedCat.ConcernedTeamster.Domain.Trips.Trip(
                index, "1:1", new[] { new TripSample(0, 0f, 0f, 1f, 1f, 100f) }));
        }

        return new TripSidecar.ParseResult(
            refused: false,
            needsMigration: false,
            trips: trips,
            segments: new RoadQualityIndex(),
            errors: Array.Empty<string>());
    }

    [Fact]
    public void Decide_RefusedFile_MandatesABackup()
    {
        TripSidecar.ParseResult refused = new(
            refused: true,
            needsMigration: false,
            trips: Array.Empty<TheConcernedCat.ConcernedTeamster.Domain.Trips.Trip>(),
            segments: new RoadQualityIndex(),
            errors: new[] { "world-uid does not match this world" });

        TripPersistPlan.Plan plan = TripPersistPlan.Decide(refused);

        Assert.Equal("refused", plan.BackupReason);
        Assert.NotNull(plan.LogWarning);
    }

    [Fact]
    public void Decide_MalformedRows_MandatesABackup()
    {
        // CT-039: this case previously took NO backup even though the next
        // write permanently discards whatever the malformed lines held —
        // the exact silent-loss gap this leaf closes.
        TripSidecar.ParseResult malformed = new(
            refused: false,
            needsMigration: false,
            trips: new List<TheConcernedCat.ConcernedTeamster.Domain.Trips.Trip>
            {
                new(0, "1:1", new[] { new TripSample(0, 0f, 0f, 1f, 1f, 100f) }),
            },
            segments: new RoadQualityIndex(),
            errors: new[] { "line 4: malformed sample; skipped" });

        TripPersistPlan.Plan plan = TripPersistPlan.Decide(malformed);

        Assert.Equal("malformed", plan.BackupReason);
        Assert.NotNull(plan.LogWarning);
    }

    [Fact]
    public void Decide_NeedsMigration_MandatesABackup()
    {
        TripSidecar.ParseResult migrating = new(
            refused: false,
            needsMigration: true,
            trips: new List<TheConcernedCat.ConcernedTeamster.Domain.Trips.Trip>
            {
                new(0, "1:1", new[] { new TripSample(0, 0f, 0f, 1f, 1f, 100f) }),
            },
            segments: new RoadQualityIndex(),
            errors: Array.Empty<string>());

        TripPersistPlan.Plan plan = TripPersistPlan.Decide(migrating);

        Assert.Equal("migrate-v1", plan.BackupReason);
        Assert.NotNull(plan.LogInfo);
    }

    [Fact]
    public void Decide_CleanFile_MandatesNoBackup()
    {
        TripPersistPlan.Plan plan = TripPersistPlan.Decide(Clean());

        Assert.Null(plan.BackupReason);
        Assert.Null(plan.LogWarning);
        Assert.Null(plan.LogInfo);
    }

    [Fact]
    public void Decide_RefusedTakesPrecedenceOverMigrationFlag()
    {
        // A refused parse always reports NeedsMigration false in practice
        // (TripSidecar refuses before it would ever set that flag), but
        // the plan must still resolve unambiguously to exactly one backup
        // reason if that ever changed — never two, never neither.
        TripSidecar.ParseResult refusedAndFlagged = new(
            refused: true,
            needsMigration: true,
            trips: Array.Empty<TheConcernedCat.ConcernedTeamster.Domain.Trips.Trip>(),
            segments: new RoadQualityIndex(),
            errors: new[] { "unsupported format-version" });

        TripPersistPlan.Plan plan = TripPersistPlan.Decide(refusedAndFlagged);

        Assert.Equal("refused", plan.BackupReason);
    }

    // -- CT-039 review finding: retried trips must never be silently
    // dropped when a persist attempt fails ------------------------------

    private static Trip MakeTrip(string cartId) =>
        new(0, cartId, new[] { new TripSample(0, 0f, 0f, 1f, 1f, 100f) });

    // -- CT-039 review finding round 2: a pending retry queue must never
    // survive a world change and get merged into a different world's
    // sidecar -------------------------------------------------------------

    [Fact]
    public void ShouldDiscardPendingRetry_NoPendingTrips_NeverDiscards()
    {
        // Nothing to discard regardless of UID mismatch when the queue is
        // already empty — this must not be misread as "always discard on
        // any UID difference."
        Assert.False(TripPersistPlan.ShouldDiscardPendingRetry(
            pendingCount: 0, pendingWorldUid: 111L, currentWorldUid: 222L));
    }

    [Fact]
    public void ShouldDiscardPendingRetry_SameWorld_NeverDiscards()
    {
        Assert.False(TripPersistPlan.ShouldDiscardPendingRetry(
            pendingCount: 3, pendingWorldUid: 111L, currentWorldUid: 111L));
    }

    [Fact]
    public void ShouldDiscardPendingRetry_DifferentWorldWithPendingTrips_Discards()
    {
        // The exact scenario the finding was about: a failed persist left
        // trips pending under one world, and the player has since loaded
        // a different one — merging now would misattribute them.
        Assert.True(TripPersistPlan.ShouldDiscardPendingRetry(
            pendingCount: 2, pendingWorldUid: 111L, currentWorldUid: 222L));
    }

    [Fact]
    public void CombineForRetry_NoPending_ReturnsNewTripsUnchanged()
    {
        var newTrips = new[] { MakeTrip("a"), MakeTrip("b") };

        List<Trip> combined = TripPersistPlan.CombineForRetry(Array.Empty<Trip>(), newTrips);

        Assert.Equal(new[] { "a", "b" }, combined.Select(trip => trip.CartId));
    }

    [Fact]
    public void CombineForRetry_PendingTripsComeBeforeNewOnes()
    {
        // A prior cycle's un-persisted trips must not lose their place in
        // line to a cycle's fresh ones — oldest-first ordering matters for
        // TripSidecar.Prune's own newest-wins pruning downstream.
        var pending = new[] { MakeTrip("old-1"), MakeTrip("old-2") };
        var newTrips = new[] { MakeTrip("new-1") };

        List<Trip> combined = TripPersistPlan.CombineForRetry(pending, newTrips);

        Assert.Equal(new[] { "old-1", "old-2", "new-1" }, combined.Select(trip => trip.CartId));
    }

    [Fact]
    public void BoundForRetry_UnderCap_KeepsEverything()
    {
        var trips = new List<Trip> { MakeTrip("a"), MakeTrip("b") };

        List<Trip> bounded = TripPersistPlan.BoundForRetry(trips, maxRetained: 5);

        Assert.Equal(2, bounded.Count);
    }

    [Fact]
    public void BoundForRetry_OverCap_DropsOldestFirst()
    {
        // A persistently broken disk must not grow the retry queue
        // unboundedly, and the trips actually worth keeping are the most
        // recent ones (matching TripSidecar.Prune's own convention).
        var trips = new List<Trip> { MakeTrip("oldest"), MakeTrip("middle"), MakeTrip("newest") };

        List<Trip> bounded = TripPersistPlan.BoundForRetry(trips, maxRetained: 2);

        Assert.Equal(new[] { "middle", "newest" }, bounded.Select(trip => trip.CartId));
    }

    [Fact]
    public void CombineThenBoundForRetry_AFailedRetryStillEndsUpBounded()
    {
        // The realistic sequence: a prior failure left 2 pending trips, a
        // persistently broken disk means this cycle's attempt fails too —
        // the caller re-bounds the combined set, which must never exceed
        // the cap even after repeated failures.
        var pending = new[] { MakeTrip("p1"), MakeTrip("p2") };
        var newTrips = new[] { MakeTrip("n1"), MakeTrip("n2") };

        List<Trip> combined = TripPersistPlan.CombineForRetry(pending, newTrips);
        List<Trip> bounded = TripPersistPlan.BoundForRetry(combined, maxRetained: 3);

        Assert.Equal(new[] { "p2", "n1", "n2" }, bounded.Select(trip => trip.CartId));
    }
}
