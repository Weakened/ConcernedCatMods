using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>Looks at what is on disk and reports it. Nothing here interprets
/// anything: <see cref="LegacyEvidenceRule"/> decides, and does it without a
/// filesystem.
///
/// <b>It takes a list of directories, and that is the point (#304).</b> This
/// product's files used to live in one place, inside the settings folder. They
/// now live in two — the settings folder keeps the handful of files a player
/// edits, and everything else moved out — and a profile that has not started
/// the new build yet, or whose relocation could not finish, still has its whole
/// history in the old one. Reading only the new directory would report a player
/// who has been mapping for a year as a new one, which takes their toolbar
/// away, because <c>LegacyEvidence.None</c> does not unlock. So every
/// directory this product has ever written to is read, and any of them is
/// enough.
///
/// It reads file <i>existence</i> and timestamps only — never contents — so a
/// corrupt atlas is as much proof of prior use as a healthy one, and a scan can
/// never be slowed down or confused by the size of somebody's road data.</summary>
internal static class CartographerEvidenceScan
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

    /// <summary>What is on disk, across every directory this product writes to.
    /// </summary>
    /// <param name="directories">Where to look. Newest layout first is the
    /// convention, though the answer is the same either way: every signal is a
    /// disjunction.</param>
    /// <param name="worldUid">The world being played right now.</param>
    /// <param name="processStartUtc">When this process began. Everything this
    /// build writes for itself is younger; anything older was already there.
    /// </param>
    /// <remarks>Throws whatever the filesystem throws. The caller turns that
    /// into <see cref="LegacyEvidenceFacts.Failed"/>, which grants — unreadable
    /// is not the same as empty and must never be treated as proof of a new
    /// player.</remarks>
    public static LegacyEvidenceFacts Gather(
        IReadOnlyList<string> directories, long worldUid, DateTime processStartUtc)
    {
        string worldToken = worldUid.ToString(CultureInfo.InvariantCulture);
        bool anyDirectory = false;
        bool thisWorld = false;
        bool anyWorld = false;
        bool profileWide = false;
        bool unrecognised = false;

        foreach (string directory in directories ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            // A directory that exists at all is the difference between "this
            // installation has never written anything anywhere" and everything
            // else.
            anyDirectory = true;

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

            if (!profileWide)
            {
                foreach (string file in ProfileFiles)
                {
                    if (File.Exists(Path.Combine(directory, file)))
                    {
                        profileWide = true;
                        break;
                    }
                }
            }

            if (!profileWide)
            {
                foreach (string file in PreexistingProfileFiles)
                {
                    if (ExistedBefore(Path.Combine(directory, file), processStartUtc))
                    {
                        profileWide = true;
                        break;
                    }
                }
            }

            if (!unrecognised &&
                !CartographerFirstRunFiles.IsOnlySelfWritten(NamesIn(directory)))
            {
                unrecognised = true;
            }
        }

        if (!anyDirectory)
        {
            // No directory anywhere is the one genuinely clean state.
            return LegacyEvidenceFacts.None;
        }

        // A directory that exists but holds nothing we recognise still means
        // somebody was here. Weak, so it reads as the config-age signal rather
        // than as real data — and Ambiguous grants.
        //
        // Our own first-run bookkeeping does not count. The directory is
        // created by this build during startup and immediately filled with a
        // starter rules file and a strings template, so "the directory is not
        // empty" was true on every first run and this signal fired for
        // everybody.
        bool configured = !thisWorld && !anyWorld && !profileWide && unrecognised;

        return new LegacyEvidenceFacts(
            thisWorld, anyWorld, profileWide, configured, probeFailed: false);
    }

    /// <summary>True when the file was on disk before this session started.
    /// A timestamp, not contents: this scan has never read a file's bytes and
    /// still does not.</summary>
    private static bool ExistedBefore(string path, DateTime processStartUtc)
    {
        try
        {
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) < processStartUtc;
        }
        catch
        {
            // An unreadable timestamp is not evidence of a new player.
            return true;
        }
    }

    /// <summary>Every file name directly in a directory. Names only, and
    /// <c>GetFiles</c> does not descend — which is what keeps the markers under
    /// <c>state</c> out of this listing by construction.</summary>
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
}
