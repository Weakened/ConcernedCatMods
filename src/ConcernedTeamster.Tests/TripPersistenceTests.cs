using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace ConcernedTeamster.Tests;

/// <summary>CT-016: recorder state machine (start, spacing, cap-splitting,
/// end debounce, noise discard), sidecar round-trip with malformed-row
/// skipping, cross-world refusal, unknown-version refusal (migration
/// stub), pruning caps, and atomic-write crash behavior on a real
/// filesystem.</summary>
public class TripPersistenceTests
{
    private static CartTelemetry Pulled(
        double time, string id = "1:1", float x = 10f, float z = 20f,
        float grade = 3f, float speed = 1.5f)
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            id, baseMass: 20f, cargoWeight: 100f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: true, isPulledByLocalPlayer: true);
        return CartTelemetry.Create(
            snapshot, true, speed, 0f, true, grade, grade, GradeDirection.Level,
            TerrainSurfaceKind.Untouched, time, x, z);
    }

    private static TripRecorderOptions Options(int maxSamples = 600, int maxTrips = 50)
    {
        return TripRecorderOptions.CreateClamped(1f, maxSamples, maxTrips);
    }

    // -- recorder --------------------------------------------------------

    [Fact]
    public void Recorder_RecordsAtSpacing_AndFinalizesAfterDebounce()
    {
        var recorder = new TripRecorder(Options());
        for (int index = 0; index < 20; index++)
        {
            recorder.FeedPulled(Pulled(index * 0.5)); // 0.5 s cadence, 1 s spacing
        }

        Assert.True(recorder.IsRecording);
        Assert.Equal(10, recorder.CurrentSampleCount); // every other sample

        recorder.NotifyNotPulled(10.0);
        recorder.NotifyNotPulled(12.9);
        Assert.True(recorder.IsRecording); // still inside the 3 s debounce
        recorder.NotifyNotPulled(13.1);
        Assert.False(recorder.IsRecording);

        IReadOnlyList<Trip> finished = recorder.DrainFinishedTrips();
        Assert.Single(finished);
        Assert.Equal(10, finished[0].Samples.Count);
        Assert.Equal("1:1", finished[0].CartId);
    }

    [Fact]
    public void Recorder_ReattachWithinDebounce_ContinuesTheSameTrip()
    {
        var recorder = new TripRecorder(Options());
        recorder.FeedPulled(Pulled(0.0));
        recorder.FeedPulled(Pulled(1.0));
        recorder.NotifyNotPulled(1.5);
        recorder.FeedPulled(Pulled(2.5)); // re-grab inside 3 s
        recorder.FeedPulled(Pulled(3.5));
        recorder.FeedPulled(Pulled(4.5));

        recorder.NotifyNotPulled(5.0);
        recorder.NotifyNotPulled(8.1);

        IReadOnlyList<Trip> finished = recorder.DrainFinishedTrips();
        Assert.Single(finished);
        Assert.Equal(5, finished[0].Samples.Count);
    }

    [Fact]
    public void Recorder_TooShortTrips_AreDiscardedAsNoise()
    {
        var recorder = new TripRecorder(Options());
        recorder.FeedPulled(Pulled(0.0));
        recorder.FeedPulled(Pulled(1.0)); // 2 samples < MinSamplesToKeep
        recorder.NotifyNotPulled(2.0);
        recorder.NotifyNotPulled(5.1);

        Assert.Empty(recorder.DrainFinishedTrips());
    }

    [Fact]
    public void Recorder_CapSplitsIntoSegmentsWithoutLosingTheHaul()
    {
        var recorder = new TripRecorder(TripRecorderOptions.CreateClamped(1f, 50, 50));
        for (int index = 0; index < 120; index++)
        {
            recorder.FeedPulled(Pulled(index));
        }

        IReadOnlyList<Trip> finished = recorder.DrainFinishedTrips();
        Assert.Equal(2, finished.Count);           // two full 50-sample segments
        Assert.All(finished, trip => Assert.Equal(50, trip.Samples.Count));
        Assert.True(recorder.IsRecording);          // the third segment is live
        Assert.Equal(20, recorder.CurrentSampleCount);
    }

    [Fact]
    public void Recorder_CartSwitch_FinalizesThePreviousTrip()
    {
        var recorder = new TripRecorder(Options());
        for (int index = 0; index < 6; index++)
        {
            recorder.FeedPulled(Pulled(index, id: "1:1"));
        }

        recorder.FeedPulled(Pulled(6.0, id: "2:2"));

        IReadOnlyList<Trip> finished = recorder.DrainFinishedTrips();
        Assert.Single(finished);
        Assert.Equal("1:1", finished[0].CartId);
        Assert.True(recorder.IsRecording);
    }

    [Fact]
    public void Recorder_DrainOnReset_FinalizesTheOpenTrip()
    {
        var recorder = new TripRecorder(Options());
        for (int index = 0; index < 8; index++)
        {
            recorder.FeedPulled(Pulled(index));
        }

        IReadOnlyList<Trip> drained = recorder.DrainOnReset();
        Assert.Single(drained);
        Assert.Equal(8, drained[0].Samples.Count);
        Assert.False(recorder.IsRecording);
    }

    // -- codec -----------------------------------------------------------

    private static Trip MakeTrip(int samples, string cartId = "1:1", double startTime = 0.0)
    {
        var list = new TripSample[samples];
        for (int index = 0; index < samples; index++)
        {
            list[index] = new TripSample(
                startTime + index, 10f + index, 20f - index,
                index == 2 ? float.NaN : 4.5f, 1.25f, 120f);
        }

        return new Trip(0, cartId, list);
    }

    [Fact]
    public void Sidecar_RoundTripsTripsSamplesAndNaNMarkers()
    {
        string text = TripSidecar.Compose(
            new[] { MakeTrip(6), MakeTrip(5, "2:2", 100.0) }, worldUid: 42L, "0.4.0");

        TripSidecar.ParseResult parsed = TripSidecar.Parse(text, expectedWorldUid: 42L);

        Assert.False(parsed.Refused);
        Assert.Empty(parsed.Errors);
        Assert.Equal(2, parsed.Trips.Count);
        Assert.Equal(6, parsed.Trips[0].Samples.Count);
        Assert.Equal("2:2", parsed.Trips[1].CartId);
        Assert.True(float.IsNaN(parsed.Trips[0].Samples[2].GradePercent));
        Assert.Equal(11f, parsed.Trips[0].Samples[1].PositionX);
        Assert.Equal(120f, parsed.Trips[0].Samples[0].TotalMass);
    }

    [Fact]
    public void Sidecar_MalformedRows_AreSkippedAndReported()
    {
        string text = TripSidecar.Compose(new[] { MakeTrip(6) }, 42L, "0.4.0");
        text = text.Replace("s: 2.00", "s: garbage");
        text += "stray line\n";

        TripSidecar.ParseResult parsed = TripSidecar.Parse(text, 42L);

        Assert.False(parsed.Refused);
        Assert.Single(parsed.Trips);
        Assert.Equal(5, parsed.Trips[0].Samples.Count); // one row skipped
        Assert.Equal(2, parsed.Errors.Count);
    }

    [Fact]
    public void Sidecar_WrongWorldUid_IsRefusedUntouched()
    {
        string text = TripSidecar.Compose(new[] { MakeTrip(6) }, worldUid: 42L, "0.4.0");

        TripSidecar.ParseResult parsed = TripSidecar.Parse(text, expectedWorldUid: 7L);

        Assert.True(parsed.Refused);
        Assert.Empty(parsed.Trips);
        Assert.Contains(parsed.Errors, error => error.Contains("does not match this world"));
    }

    [Fact]
    public void Sidecar_UnknownFormatVersion_IsRefused_MigrationStub()
    {
        string text = TripSidecar.Compose(new[] { MakeTrip(6) }, 42L, "0.4.0")
            .Replace("format-version: 2", "format-version: 99");

        TripSidecar.ParseResult parsed = TripSidecar.Parse(text, 42L);

        Assert.True(parsed.Refused);
        Assert.Empty(parsed.Trips);
        Assert.Contains(parsed.Errors, error => error.Contains("unsupported format-version"));
    }

    [Fact]
    public void Sidecar_Prune_KeepsNewestAndRenumbersDensely()
    {
        var trips = new List<Trip>();
        for (int index = 0; index < 8; index++)
        {
            trips.Add(MakeTrip(5, cartId: "c" + index, startTime: index * 100.0));
        }

        IReadOnlyList<Trip> pruned = TripSidecar.Prune(trips, maxTrips: 3);

        Assert.Equal(3, pruned.Count);
        Assert.Equal("c5", pruned[0].CartId); // oldest five pruned
        Assert.Equal("c7", pruned[2].CartId);
        Assert.Equal(new[] { 1, 2, 3 }, new[] { pruned[0].Id, pruned[1].Id, pruned[2].Id });
    }

    // -- atomic file store -------------------------------------------------

    [Fact]
    public void FileStore_CrashBeforeSwap_LeavesThePreviousFileIntact()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ct016-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "teamster_trips_42.txt");
            Assert.True(SidecarFileStore.TryWriteAtomic(path, "original", out _));

            // Simulated kill-during-write: the temp file exists but the swap
            // never happened. The original must be untouched, and loading
            // ignores the temp.
            File.WriteAllText(path + ".tmp", "torn partial write");
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal("original", SidecarFileStore.TryRead(path, out string? readError));
            Assert.Null(readError);

            // The next successful write replaces atomically and cleans up.
            Assert.True(SidecarFileStore.TryWriteAtomic(path, "second", out _));
            Assert.Equal("second", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileStore_BackupBeforeMigration_CopiesTheFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ct016-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "teamster_trips_42.txt");
            File.WriteAllText(path, "old format");

            Assert.True(SidecarFileStore.TryBackup(path, "refused", out _));
            Assert.Equal("old format", File.ReadAllText(path + ".bak-refused"));
            Assert.Equal("old format", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Isolation_TwoWorlds_ByFilenameAndHeader()
    {
        // Filenames differ by UID, and even a byte-for-byte copy into the
        // other world's name is refused by the header check.
        string worldAText = TripSidecar.Compose(new[] { MakeTrip(6) }, 1111L, "0.4.0");
        TripSidecar.ParseResult asWorldB = TripSidecar.Parse(worldAText, 2222L);

        Assert.True(asWorldB.Refused);
        Assert.Empty(asWorldB.Trips);
    }
}
