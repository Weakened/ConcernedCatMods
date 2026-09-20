using System;
using System.IO;
using TheConcernedCat.ConcernedCartographer.Atlas;

namespace TheConcernedCat.ConcernedCartographer.Storage;

/// <summary>Startup IO for <c>survey-rules.tsv</c>: write the starter set on a
/// first run, upgrade an untouched older starter file in place, and hand back
/// the parsed rules.
///
/// <b>Why this is not in the persistence layer.</b> It used to be, and #366 is
/// what that cost. The upgrade rewrites a file the fresh-install probe reads the
/// <i>timestamp</i> of, so the two are one behaviour — and the persistence layer
/// needs BepInEx, so no test could ever run the rewrite and the probe's question
/// together. Both halves now live in the domain and the test assembly runs both
/// against a real file.
///
/// <b>#366, and why the last merge made it worse.</b>
/// <c>CartographerLegacyProbe</c> decides "has this player been here before" for
/// <c>survey-rules.tsv</c> by asking whether the file predates the session
/// (<see cref="PreexistingFile"/>), because this build writes a starter copy
/// during startup and existence alone therefore proved nothing. Rewriting the
/// file during startup resets its last-write time, so the probe then sees a file
/// younger than the session and reports a veteran as a brand-new player. Before
/// issue #385 the rewrite only fired for pre-v1.0.3 installs; #385 added the
/// v1.0.3-v1.2.2 snapshot, which is essentially the whole installed base.
///
/// <b>The fix: the upgrade puts the timestamp back.</b> The last-write time is
/// read as "when did the player last touch this document", and a migration this
/// build performs on its own is not the player touching it — so preserving it
/// makes the timestamp more truthful about the question it is actually asked,
/// not less. It also makes the answer independent of construction order: it no
/// longer matters whether the probe runs before or after the rewrite, which is
/// the property <see cref="Outcome"/> exists to report and the tests pin in both
/// orders.
///
/// <b>Failing in the safe direction.</b> If the timestamp cannot be put back the
/// outcome says so rather than staying quiet, so the log names the one case where
/// a returning player could still be misread — a false negative is not the same
/// as ambiguity, and the program's "ambiguous evidence grants" rule does not
/// cover it. Nothing here ever moves the timestamp of a file the player edited:
/// an edited file matches no starter snapshot, so it is neither read further nor
/// written.</summary>
internal static class SurveyRuleFile
{
    /// <summary>What the coarsest filesystem this could plausibly land on
    /// (FAT/exFAT, two-second last-write granularity) may round a restored
    /// timestamp by. Anything further forward than this is a real failure to
    /// restore, not rounding.
    ///
    /// Public so the tests assert the contract this class actually offers
    /// rather than a tighter one of their own. Asserting exact equality would
    /// pass on NTFS and fail environmentally on a FAT-class volume — a test
    /// that is red for a reason the product is right about is worse than no
    /// test, because it trains people to ignore it.</summary>
    public static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    /// <summary>What happened to the file on disk. The caller logs; this decides
    /// nothing about wording and knows nothing about log levels.</summary>
    internal enum Outcome
    {
        /// <summary>The file was already there and was left exactly as it was:
        /// the player's own document, or a file already on the current starter
        /// set. Not written, not restamped.</summary>
        Kept,

        /// <summary>No file existed, so the current starter set was written.
        /// This file is genuinely new and SHOULD look new to the probe.</summary>
        Created,

        /// <summary>An untouched older starter file was rewritten with the
        /// current starter set and its original last-write time was put back, so
        /// a returning player still reads as returning.</summary>
        Upgraded,

        /// <summary>The rewrite happened but the original last-write time could
        /// not be restored. The rules are correct; the probe may now read this
        /// profile as a fresh install. The one outcome worth warning about.
        /// </summary>
        UpgradedButTimestampMoved,
    }

    /// <summary>Brings the file to the current starter set where that is safe,
    /// then parses it.</summary>
    /// <param name="path">The rules file.</param>
    /// <param name="rules">The parsed rules. Always set.</param>
    /// <param name="malformedRows">Rows the parser skipped.</param>
    public static Outcome LoadOrCreate(string path, out SurveyRuleSet rules, out int malformedRows)
    {
        Outcome outcome = Prepare(path);
        rules = SurveyRuleSet.Parse(File.ReadAllLines(path), out malformedRows);
        return outcome;
    }

    private static Outcome Prepare(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, SurveyRuleSet.Default().Serialize());
            return Outcome.Created;
        }

        // An edited file never reaches the write below: it matches no shipped
        // snapshot, so it is neither rewritten nor restamped.
        if (!SurveyStarterUpgrade.ShouldUpgrade(File.ReadAllLines(path)))
        {
            return Outcome.Kept;
        }

        // Read BEFORE the write, obviously, but also read at all: this is the
        // value the probe is about to be asked about.
        DateTime playerLastTouched = File.GetLastWriteTimeUtc(path);
        File.WriteAllLines(path, SurveyRuleSet.Default().Serialize());

        return RestoreLastWriteTime(path, playerLastTouched)
            ? Outcome.Upgraded
            : Outcome.UpgradedButTimestampMoved;
    }

    /// <summary>Puts the last-write time back, and confirms it at the file
    /// rather than assuming the call took. A filter driver, a network-backed
    /// config folder or a read-only attribute can accept the write and refuse
    /// the metadata, and the caller is about to tell a player whether they are
    /// a returning user on the strength of it.</summary>
    private static bool RestoreLastWriteTime(string path, DateTime original)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, original);
            return File.GetLastWriteTimeUtc(path) <= original + TimestampTolerance;
        }
        catch (Exception)
        {
            // Reported through the outcome, never swallowed: the caller's own
            // log line is the only place this can be seen.
            return false;
        }
    }
}
