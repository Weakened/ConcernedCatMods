using System;
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
    /// action: saving a view, writing a survey rule, installing a
    /// translation.</summary>
    private static readonly string[] ProfileFiles =
    {
        "views.tsv",
        "survey-rules.tsv",
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
            // means somebody ran this mod before. Weak, so it reads as the
            // config-age signal rather than as real data — and Ambiguous
            // grants.
            bool configured = !thisWorld && !anyWorld && !profileWide;

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
