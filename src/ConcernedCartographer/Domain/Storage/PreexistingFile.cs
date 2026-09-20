using System;
using System.IO;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>The one question "was this file already here before we started?",
/// in the domain so the answer can be proved against a real file rather than
/// only reasoned about.
///
/// <b>Why it is not simply inlined in the probe.</b>
/// <c>CartographerLegacyProbe</c> lives in the runtime layer and cannot be
/// compiled into the test assembly — it needs BepInEx — so the predicate that
/// decides whether a returning player is recognised had no test that touched a
/// filesystem at all. #366 was exactly a disagreement between this predicate
/// and a writer somewhere else in startup, and a disagreement between two pieces
/// of code is only catchable by a test that runs both.
///
/// <b>The rule, and its direction.</b> A file whose last write predates the
/// session was there before us and is the player's history. A file written
/// during this session might be ours. Everything ambiguous — an unreadable
/// timestamp, a filesystem that will not answer — resolves to <c>true</c>,
/// because saying "new player" to a veteran takes their toolbar away and saying
/// "returning player" to a new one costs a few minutes of story. Those two
/// mistakes are not the same size.
///
/// <b>What this implies for every writer.</b> Any code that rewrites a file on
/// this list for its own reasons — a migration, a format upgrade — moves the
/// file's last-write time and therefore changes the answer. A writer that does
/// so must put the timestamp back (see
/// <see cref="SurveyRuleFile"/>), because the player did not touch the file and
/// the timestamp is being read as "when did the player last touch this".</summary>
internal static class PreexistingFile
{
    /// <summary>True when <paramref name="path"/> was on disk before
    /// <paramref name="sessionStartUtc"/>. A timestamp, never contents: the
    /// fresh-install probe has never read a byte of a player's data and this
    /// does not start.</summary>
    public static bool ExistedBefore(string path, DateTime sessionStartUtc)
    {
        try
        {
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) < sessionStartUtc;
        }
        catch
        {
            // An unreadable timestamp is not evidence of a new player.
            return true;
        }
    }
}
