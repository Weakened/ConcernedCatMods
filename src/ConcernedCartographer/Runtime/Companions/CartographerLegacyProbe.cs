using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.Companions.Unlock;
using TheConcernedCat.ConcernedCartographer.Companions;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Answers the shared layer's one product-specific question: has this
/// installation been used before?
///
/// Only the product knows what its own old data looks like, which is why this
/// lives here and not in the shared layer. It reads file <i>existence</i> only
/// — never contents — so a corrupt atlas is as much proof of prior use as a
/// healthy one, and a probe can never be slowed down or confused by the size
/// of somebody's road data.
///
/// The interpretation is not here either: this gathers facts and
/// <see cref="LegacyEvidenceRule"/> decides, so the decision is unit-tested
/// against every combination without a filesystem.</summary>
internal sealed class CartographerLegacyProbe
{
    /// <summary>World-scoped sidecar suffixes this product has ever written.
    /// Any one of them existing proves the mod did real work in some world.</summary>
    private static readonly string[] WorldSuffixes =
    {
        ".roads.tsv",
        ".pins.tsv",
        ".routes-atlas.tsv",
        ".survey-rejected.tsv",
        ".terrain-intent.tsv",
    };

    /// <summary>Profile-scoped files that only appear after a deliberate
    /// action: saving a view, installing a translation. The translator's
    /// override has a different name from the template this build writes, and
    /// somebody has to create it.</summary>
    private static readonly string[] ProfileFiles =
    {
        "views.tsv",
        "cartographer-strings.tsv",

        // Only exists because somebody ran `cc_atlas support`. It was reaching
        // the probe as an unrecognised name, which grants through the weak
        // "somebody was here" signal rather than saying what it is.
        //
        // Both names. The report is `.log` now, because a configuration editor
        // opens `.txt` and was offering it for editing (#304) - but a profile
        // that ran the command under an older build still has the `.txt`, and
        // that is still somebody having deliberately asked for a report.
        // Dropping the old name would take a returning player's evidence away.
        "support-report.log",
        "support-report.txt",
    };

    /// <summary>Files that count as prior use only if they were already on
    /// disk when this process started.
    ///
    /// <c>survey-rules.tsv</c> is both: the plugin writes a starter copy during
    /// startup, and a player can edit or import their own. Existence alone
    /// therefore proved only that the mod had been launched — including by the
    /// very session doing the asking — and a brand new character on a brand new
    /// world was granted access as an existing user because of it, observed in
    /// game.
    ///
    /// The timestamp separates the two without reading a byte of the file: a
    /// copy this session wrote is younger than the session. One a player has
    /// had since their last install is older, and still counts.</summary>
    private static readonly string[] PreexistingProfileFiles =
    {
        "survey-rules.tsv",
    };

    /// <summary>When this process began. Everything this build writes for
    /// itself is younger than this; anything older was already there.</summary>
    private static readonly DateTime ProcessStartUtc = ResolveProcessStartUtc();

    private static DateTime ResolveProcessStartUtc()
    {
        try
        {
            return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch
        {
            // Unknown start time must not turn an old file into a new one, so
            // it resolves to "everything predates us" - the generous answer.
            return DateTime.MaxValue;
        }
    }

    private readonly ManualLogSource _log;

    public CartographerLegacyProbe(ManualLogSource log)
    {
        _log = log;
    }

    public LegacyEvidence Evaluate(long worldUid)
    {
        LegacyEvidenceFacts facts = Gather(worldUid);
        return LegacyEvidenceRule.Evaluate(facts);
    }

    /// <summary>True when the file was on disk before this session started.
    /// A timestamp, not contents: this probe has never read a file's bytes and
    /// still does not.</summary>
    private static bool ExistedBeforeThisSession(string path)
    {
        try
        {
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) < ProcessStartUtc;
        }
        catch
        {
            // An unreadable timestamp is not evidence of a new player.
            return true;
        }
    }

    /// <summary>Every file name directly in the data directory. Names only —
    /// this probe has never read a file's contents and still does not.</summary>
    private static IReadOnlyList<string> NamesIn(string directory)
    {
        string[] paths = Directory.GetFiles(directory);
        var names = new string[paths.Length];
        for (int index = 0; index < paths.Length; index++)
        {
            names[index] = Path.GetFileName(paths[index]);
        }

        return names;
    }

    internal LegacyEvidenceFacts Gather(long worldUid)
    {
        try
        {
            string directory = CartographerPaths.Root;
            if (!Directory.Exists(directory))
            {
                // No directory at all is the one genuinely clean state: this
                // installation has never written anything anywhere.
                return LegacyEvidenceFacts.None;
            }

            string worldToken = worldUid.ToString(CultureInfo.InvariantCulture);
            bool thisWorld = false;
            bool anyWorld = false;

            foreach (string suffix in WorldSuffixes)
            {
                if (!thisWorld && File.Exists(Path.Combine(directory, worldToken + suffix)))
                {
                    thisWorld = true;
                }

                if (!anyWorld && Directory.GetFiles(directory, "*" + suffix).Length > 0)
                {
                    anyWorld = true;
                }

                if (thisWorld && anyWorld)
                {
                    break;
                }
            }

            bool profileWide = false;
            foreach (string file in ProfileFiles)
            {
                if (File.Exists(Path.Combine(directory, file)))
                {
                    profileWide = true;
                    break;
                }
            }

            if (!profileWide)
            {
                foreach (string file in PreexistingProfileFiles)
                {
                    if (ExistedBeforeThisSession(Path.Combine(directory, file)))
                    {
                        profileWide = true;
                        break;
                    }
                }
            }

            // A directory that exists but holds nothing we recognise still
            // means somebody was here. Weak, so it reads as the config-age
            // signal rather than as real data — and Ambiguous grants.
            //
            // Our own first-run bookkeeping does not count. The directory is
            // created by this build during startup and immediately filled with
            // an author id, a starter rules file and a strings template, so
            // "the directory is not empty" was true on every first run and
            // this signal fired for everybody.
            bool configured = !thisWorld && !anyWorld && !profileWide &&
                !CartographerFirstRunFiles.IsOnlySelfWritten(NamesIn(directory));

            return new LegacyEvidenceFacts(
                thisWorld, anyWorld, profileWide, configured, probeFailed: false);
        }
        catch (Exception exception)
        {
            // Unreadable is not the same as empty, and must never be treated
            // as proof of a new player.
            _log.LogWarning(
                "Could not check for existing Concerned Cartographer data, so your tools stay " +
                $"available: {SafeLogText.Describe(exception)}");
            return LegacyEvidenceFacts.Failed;
        }
    }
}
