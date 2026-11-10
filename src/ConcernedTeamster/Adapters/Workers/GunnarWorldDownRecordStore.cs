using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Where the record of what Gunnar's collection account still held,
/// unvouched for, when its world went down is kept (#376, third slice): one
/// append-only file per world beside the death record, on this computer only.
///
/// The same shape as <see cref="GunnarDeathRecordStore"/>, for the same reason:
/// the BepInEx log is replaced on the next launch, and evidence a player only
/// looks for tomorrow has to outlive it. An existing file that cannot be read
/// is left exactly as it is and the new rows go to the log only.
///
/// <b>Nothing here touches the world save.</b></summary>
internal sealed class GunnarWorldDownRecordStore
{
    private const string Prefix = "teamster_worker_world_down_";
    private const string Extension = ".tsv";

    private readonly string _directory;

    internal GunnarWorldDownRecordStore(string? directory = null)
    {
        _directory = string.IsNullOrEmpty(directory)
            ? Adapters.TripRecordingService.SidecarDirectory
            : directory!;
    }

    internal string FileNameFor(long worldUid) =>
        Prefix + worldUid.ToString(CultureInfo.InvariantCulture) + Extension;

    /// <summary>Appends the rows for one world-down. False, with a notice that
    /// names no path, when they could not be kept.</summary>
    internal bool TryAppend(long? worldUid, string rows, out string notice)
    {
        if (worldUid == null || worldUid.Value == 0)
        {
            notice = "no world could be named, so the record is in the log only";
            return false;
        }

        string path = Path.Combine(_directory, FileNameFor(worldUid.Value));
        string? existing = SidecarFileStore.TryRead(path, out string? readError);
        if (readError != null)
        {
            notice = "the existing world-down record could not be read, so it was left alone and this one is in " +
                "the log only: " + Scrub(readError);
            return false;
        }

        if (!SidecarFileStore.TryWriteAtomic(path, WorldDownRows.Append(existing, rows), out string? writeError))
        {
            notice = "the world-down record could not be written, so this one is in the log only: " + Scrub(writeError);
            return false;
        }

        notice = "recorded in " + FileNameFor(worldUid.Value);
        return true;
    }

    private static string Scrub(string? text) =>
        Domain.Support.SupportBundleSanitizer.Sanitize(text);
}
