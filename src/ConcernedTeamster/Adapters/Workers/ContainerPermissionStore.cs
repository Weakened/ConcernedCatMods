using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Where a player's container permissions are kept: one small file per
/// world, beside Teamster's other sidecars, on this computer only (#374).
///
/// <b>It fails safe in the one direction that matters.</b> A file that cannot be
/// read loads as "no containers enabled" and is then <b>never written over</b>,
/// so the worst a damaged or locked file can do is keep Gunnar out of a chest -
/// it can never open one nobody opened to him, and it can never destroy a file
/// somebody might still recover by hand. That is the door-permission store's
/// rule, for the same reason, and it is why <see cref="IsReadOnly"/> exists
/// rather than the loader simply returning an empty book.
///
/// <b>Nothing here touches the world save.</b> The file lives under the BepInEx
/// config path, like every other Teamster sidecar. What one player lets their own
/// NPCs do is not a fact about the world, and writing it into one would push it
/// at everybody who loads that world.</summary>
internal sealed class ContainerPermissionStore
{
    private const string Prefix = "teamster_containers_";
    private const string Extension = ".tsv";

    private readonly string _directory;

    internal ContainerPermissionStore(string? directory = null)
    {
        _directory = string.IsNullOrEmpty(directory)
            ? TripRecordingService.SidecarDirectory
            : directory!;
    }

    /// <summary>True when the file exists and could not be read. Saving is
    /// refused while this holds.</summary>
    internal bool IsReadOnly { get; private set; }

    /// <summary>What went wrong, scrubbed, or null. For a console reply and the
    /// log - never for deciding anything.</summary>
    internal string? Notice { get; private set; }

    /// <summary>How many records the last load dropped as unreadable. Worth
    /// telling a player: their permissions did not all come back, and finding
    /// that out at a chest is worse than being told.</summary>
    internal int Dropped { get; private set; }

    internal string PathFor(long worldUid) =>
        Path.Combine(
            _directory,
            Prefix + worldUid.ToString(CultureInfo.InvariantCulture) + Extension);

    /// <summary>Reads one world's permissions. An absent file is a fresh world,
    /// which is the ordinary case and never an error.</summary>
    internal NpcContainerDesk Load(long worldUid)
    {
        IsReadOnly = false;
        Notice = null;
        Dropped = 0;

        string path = PathFor(worldUid);
        string? content = SidecarFileStore.TryRead(path, out string? error);

        if (error != null)
        {
            // The file is there and unreadable. Refuse to save over it, and hand
            // back a desk with nothing enabled: off is always the safe answer.
            IsReadOnly = true;
            Notice = "Container permissions could not be read, so none are in force this session, " +
                "and the file will not be written over: " + Scrub(error);
            return new NpcContainerDesk();
        }

        if (content == null)
        {
            return new NpcContainerDesk();
        }

        List<NpcContainerDecision> rows = ContainerPermissionRows.Deserialize(content, out int dropped);
        NpcContainerDesk desk = NpcContainerDesk.Restore(rows, out int droppedOnRestore);
        Dropped = dropped + droppedOnRestore;

        if (Dropped > 0)
        {
            Notice = Dropped.ToString(CultureInfo.InvariantCulture) +
                " container permission(s) could not be read back and are off; mark those chests again.";
        }

        return desk;
    }

    /// <summary>Writes one world's permissions, atomically. False with a notice
    /// on any failure, and the previous file is left exactly as it was.
    ///
    /// Refused outright while <see cref="IsReadOnly"/> holds: a session that
    /// could not read the file has no idea what is in it, and writing what it
    /// happens to hold would throw away permissions the player still has.
    /// </summary>
    internal bool Save(long worldUid, NpcContainerDesk desk)
    {
        if (IsReadOnly)
        {
            Notice = "Container permissions are not being saved this session, because the existing " +
                "file could not be read and overwriting it would discard what is in it.";
            return false;
        }

        string path = PathFor(worldUid);
        string content = ContainerPermissionRows.Serialize(desk.Decisions);
        if (!SidecarFileStore.TryWriteAtomic(path, content, out string? error))
        {
            Notice = "Container permissions could not be saved: " + Scrub(error);
            return false;
        }

        desk.MarkClean();
        Notice = null;
        return true;
    }

    /// <summary>#411 is the issue that routes this product's console and log text
    /// through the scrubber wholesale. Until then this one call site does it by
    /// hand, because a path in a failure notice is a path in a console reply, and
    /// this notice's whole job is to be shown to somebody.</summary>
    private static string Scrub(string? text) =>
        Domain.Support.SupportBundleSanitizer.Sanitize(text);
}
