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
    /// action: saving a view, installing a translation.
    ///
    /// <c>survey-rules.tsv</c> used to be here and is deliberately gone. The
    /// plugin writes a starter copy of it during startup, so finding it proved
    /// only that this mod had been launched — including by the very session
    /// doing the asking. A brand new character on a brand new world was granted
    /// access as an existing user because of it, observed in game before this
    /// was fixed. The translator's override file stays: it has a different name
    /// from the template this build writes, and somebody has to create it.</summary>
    private static readonly string[] ProfileFiles =
    {
        "views.tsv",
        "cartographer-strings.tsv",
    };

    private readonly ManualLogSource _log;

    public CartographerLegacyProbe(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>The directory every Cartographer sidecar has always lived in.
    /// Also where companion sidecars go, so one folder move takes a player's
    /// whole Cartographer history with it.</summary>
    public static string DataDirectory => Path.Combine(
        Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer");

    public LegacyEvidence Evaluate(long worldUid)
    {
        LegacyEvidenceFacts facts = Gather(worldUid);
        return LegacyEvidenceRule.Evaluate(facts);
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
            string directory = DataDirectory;
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
