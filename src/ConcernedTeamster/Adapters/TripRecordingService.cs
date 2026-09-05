using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Owns trip persistence at runtime (CT-016): feeds the pure
/// recorder from the pump's pulled-cart snapshots, and on every trip
/// finalization does a read-merge-prune-write cycle against this world's
/// sidecar — atomic write, versioned header, world-UID check, backup
/// before a refused file would ever be replaced. Files live under
/// <c>BepInEx/config/ConcernedCatMods/ConcernedTeamster/</c> with a
/// "teamster" infix, so they can never collide with Cartographer's
/// sidecars (different folder AND different names). No Valheim save file
/// is ever touched.</summary>
internal sealed class TripRecordingService
{
    private readonly TripRecorder _recorder;
    private readonly TripRecorderOptions _options;
    private readonly ManualLogSource _log;
    private readonly string _pluginVersion;
    private bool _ioFailureLogged;
    private bool _refusalLogged;

    public TripRecordingService(TripRecorderOptions options, ManualLogSource log, string pluginVersion)
    {
        _options = options;
        _recorder = new TripRecorder(options);
        _log = log;
        _pluginVersion = pluginVersion;
    }

    public static string SidecarDirectory =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedTeamster");

    public static string SidecarPathFor(long worldUid) =>
        Path.Combine(SidecarDirectory, "teamster_trips_" +
            worldUid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt");

    public void FeedPulled(CartTelemetry telemetry)
    {
        _recorder.FeedPulled(telemetry);
        PersistFinishedTrips();
    }

    public void NotifyNotPulled(double nowSeconds)
    {
        _recorder.NotifyNotPulled(nowSeconds);
        PersistFinishedTrips();
    }

    /// <summary>World exit / shutdown: finalize the open trip and flush.
    /// Uses the world UID captured while the world was still up when the
    /// live query already fails.</summary>
    public void FlushAndReset(long lastKnownWorldUid)
    {
        IReadOnlyList<Trip> drained = _recorder.DrainOnReset();
        if (drained.Count > 0)
        {
            Persist(drained, lastKnownWorldUid);
        }
    }

    private void PersistFinishedTrips()
    {
        IReadOnlyList<Trip> finished = _recorder.DrainFinishedTrips();
        if (finished.Count == 0)
        {
            return;
        }

        if (!WorldContextAdapter.TryGetWorldUid(out long worldUid))
        {
            WarnIoOnce("world UID unavailable; finished trip dropped rather than misfiled");
            return;
        }

        Persist(finished, worldUid);
    }

    private void Persist(IReadOnlyList<Trip> newTrips, long worldUid)
    {
        if (worldUid == 0L)
        {
            WarnIoOnce("world UID unavailable at flush; finished trip dropped rather than misfiled");
            return;
        }

        string path = SidecarPathFor(worldUid);
        string? existingText = SidecarFileStore.TryRead(path, out string? readError);
        if (readError is not null)
        {
            WarnIoOnce("sidecar read failed: " + readError);
            return;
        }

        TripSidecar.ParseResult existing = TripSidecar.Parse(existingText, worldUid);
        if (existing.Refused)
        {
            // Foreign or future file: back it up once and start fresh —
            // never silently destroy data.
            if (!_refusalLogged)
            {
                _refusalLogged = true;
                _log.LogWarning(
                    "Trip sidecar at " + path + " was refused (" +
                    string.Join("; ", existing.Errors) + "); backing it up and starting fresh.");
            }

            if (!SidecarFileStore.TryBackup(path, "refused", out string? backupError))
            {
                WarnIoOnce("sidecar backup failed: " + backupError + "; trips held in memory");
                return;
            }
        }
        else if (existing.Errors.Count > 0 && !_ioFailureLogged)
        {
            _ioFailureLogged = true;
            _log.LogWarning(
                "Trip sidecar had " + existing.Errors.Count +
                " malformed line(s); valid trips were kept.");
        }

        // CT-017: restore persisted segment scores, or migrate a v1 file by
        // recomputing them once from its trips — after backing it up.
        Domain.RoadQuality.RoadQualityIndex segments = existing.Segments;
        if (existing.NeedsMigration)
        {
            if (!SidecarFileStore.TryBackup(path, "migrate-v1", out string? migrateBackupError))
            {
                WarnIoOnce("pre-migration backup failed: " + migrateBackupError + "; trips held in memory");
                return;
            }

            segments = Domain.RoadQuality.RoadQualityIndex.ComputeFromTrips(existing.Trips);
            _log.LogInfo(
                "Trip sidecar migrated from format v1: segment scores recomputed from " +
                existing.Trips.Count + " stored trip(s); original backed up.");
        }

        foreach (Trip trip in newTrips)
        {
            segments.AddTrip(trip);
        }

        var merged = new List<Trip>(existing.Trips);
        merged.AddRange(newTrips);
        IReadOnlyList<Trip> pruned = TripSidecar.Prune(merged, _options.MaxTripsRetained);

        string composed = TripSidecar.Compose(pruned, worldUid, _pluginVersion, segments);
        if (!SidecarFileStore.TryWriteAtomic(path, composed, out string? writeError))
        {
            WarnIoOnce("sidecar write failed: " + writeError + "; previous file left intact");
        }
    }

    private void WarnIoOnce(string message)
    {
        if (_ioFailureLogged)
        {
            return;
        }

        _ioFailureLogged = true;
        _log.LogWarning("Trip recording: " + message + ".");
    }
}
