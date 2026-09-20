using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>Every name the backup folder and the sidecar files are composed
/// from, in one place and always under the invariant culture.
///
/// <b>The defect this replaces (#367).</b> The writer built the backup folder
/// name with string interpolation — <c>$"{worldUid}-{stamp}-{label}"</c>, which
/// formats under <c>CultureInfo.CurrentCulture</c> — while the lister globbed
/// for <c>worldUid.ToString(CultureInfo.InvariantCulture) + "-*"</c>. Two
/// formattings of the same number, and the same file in this product formatted
/// the uid invariantly in four other places, so the odd one out was the one that
/// names the folder. Where the two disagree a backup is written under a name the
/// lister cannot find: <c>cc_atlas backups</c> shows nothing, <c>restore</c>
/// cannot offer it, and the player's backup is still on disk and unreachable.
/// Nothing warns, because both halves succeed.
///
/// <b>What makes them disagree.</b> Not digit grouping — a plain
/// <c>long.ToString()</c> has no group separators in any culture — but the
/// negative sign: <c>NumberFormatInfo.NegativeSign</c> is culture data, and
/// several cultures use U+2212 MINUS SIGN rather than ASCII hyphen-minus.
/// A negative world uid then lands in a folder whose name begins with a
/// character the invariant glob never matches. <b>Not verified in game:</b>
/// whether Valheim ever hands out a negative world uid is a claim about
/// Valheim's own uid generator and is not asserted here. It does not need to
/// be — two call sites formatting one identifier two ways is the defect, and
/// this removes the disagreement rather than betting on the range.
///
/// <b>The rule.</b> Anything that becomes part of a file or folder NAME is
/// composed here, by concatenation, never by interpolation. Human-facing log
/// lines and console replies are not names and stay in the current culture,
/// where a player's own number formatting is the correct one.</summary>
internal static class AtlasBackupNaming
{
    /// <summary>The world uid as it appears in every name on disk.</summary>
    public static string WorldToken(long worldUid) =>
        worldUid.ToString(CultureInfo.InvariantCulture);

    /// <summary>The sortable UTC stamp in a backup folder name. Invariant for a
    /// second reason beyond the sign: <c>yyyy</c> under a culture whose default
    /// calendar is not Gregorian yields a different era's year entirely, and the
    /// lister sorts these as plain text.</summary>
    public static string Stamp(DateTime utc) =>
        utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    /// <summary>One backup folder's name: uid, stamp, label.</summary>
    public static string FolderName(long worldUid, DateTime utc, string label) =>
        WorldToken(worldUid) + "-" + Stamp(utc) + "-" + label;

    /// <summary>The glob that finds this world's backup folders. Every name
    /// <see cref="FolderName"/> produces must match it — that is the property
    /// the tests assert, under a culture chosen to break the old code.</summary>
    public static string SearchPattern(long worldUid) =>
        WorldToken(worldUid) + "-*";

    /// <summary>One per-world sidecar's file name.</summary>
    public static string SidecarName(long worldUid, string suffix) =>
        WorldToken(worldUid) + suffix;

    /// <summary>One per-world sidecar's replay journal.</summary>
    public static string JournalName(long worldUid, string suffix) =>
        SidecarName(worldUid, suffix) + ".journal";
}
