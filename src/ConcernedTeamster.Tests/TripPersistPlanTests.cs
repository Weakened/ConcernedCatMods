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
}
