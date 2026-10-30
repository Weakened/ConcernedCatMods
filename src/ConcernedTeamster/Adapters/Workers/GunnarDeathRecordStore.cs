using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Where the record of what Gunnar was carrying when he died is kept:
/// one append-only file per world, beside Teamster's other sidecars, on this
/// computer only (`DECISIONS.md` D16).
///
/// <b>Why a file and not the log.</b> D16 asks that a drop that failed be
/// "recorded and left as durable reconciliation evidence". The BepInEx log is
/// replaced on the next launch, so a death a player only notices tomorrow would
/// have left no evidence by then. The log still gets the same rows, at warning
/// level, so a player reading it today sees them too.
///
/// <b>It never destroys what it cannot read.</b> An existing file that cannot be
/// read is left exactly as it is and the new rows go to the log only - the same
/// rule <see cref="ContainerPermissionStore"/> keeps, for the same reason: a file
/// somebody may still recover by hand is worth more than this one write.
///
/// <b>Nothing here touches the world save.</b></summary>
internal sealed class GunnarDeathRecordStore
{
    private const string Prefix = "teamster_worker_deaths_";
    private const string Extension = ".tsv";

    private readonly string _directory;

    internal GunnarDeathRecordStore(string? directory = null)
    {
        _directory = string.IsNullOrEmpty(directory)
            ? Adapters.TripRecordingService.SidecarDirectory
            : directory!;
    }

    internal string FileNameFor(long worldUid) =>
        Prefix + worldUid.ToString(CultureInfo.InvariantCulture) + Extension;

    /// <summary>Appends the rows for one death. False, with a notice that names
    /// no path, when they could not be kept.</summary>
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
            notice = "the existing death record could not be read, so it was left alone and this one is in " +
                "the log only: " + Scrub(readError);
            return false;
        }

        if (!SidecarFileStore.TryWriteAtomic(path, WorkerDeathRows.Append(existing, rows), out string? writeError))
        {
            notice = "the death record could not be written, so this one is in the log only: " + Scrub(writeError);
            return false;
        }

        notice = "recorded in " + FileNameFor(worldUid.Value);
        return true;
    }

    private static string Scrub(string? text) =>
        Domain.Support.SupportBundleSanitizer.Sanitize(text);
}
