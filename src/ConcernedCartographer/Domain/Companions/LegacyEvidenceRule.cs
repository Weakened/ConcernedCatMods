using TheConcernedCat.Companions.Unlock;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>What was found on disk when the probe looked for signs that this
/// installation has been used before. Every field is a plain fact about files,
/// gathered by the adapter; no interpretation happens until
/// <see cref="LegacyEvidenceRule"/> sees them.</summary>
internal readonly struct LegacyEvidenceFacts
{
    public LegacyEvidenceFacts(
        bool thisWorldHasData,
        bool anyWorldHasData,
        bool profileWideDataExists,
        bool configuredBeforeThisRelease,
        bool probeFailed)
    {
        ThisWorldHasData = thisWorldHasData;
        AnyWorldHasData = anyWorldHasData;
        ProfileWideDataExists = profileWideDataExists;
        ConfiguredBeforeThisRelease = configuredBeforeThisRelease;
        ProbeFailed = probeFailed;
    }

    /// <summary>A roads/pins/routes/survey sidecar exists for the world being
    /// played right now.</summary>
    public bool ThisWorldHasData { get; }

    /// <summary>A world sidecar exists for <i>any</i> world. A player who has
    /// used the mod for a year and then starts a brand new world is still an
    /// existing player, and losing their toolbar on that new world would be
    /// the exact failure this whole policy exists to prevent.</summary>
    public bool AnyWorldHasData { get; }

    /// <summary>Profile-level data — saved views, survey rules, a translator
    /// override file. None of these is created by simply installing the mod;
    /// each one takes a deliberate action.</summary>
    public bool ProfileWideDataExists { get; }

    /// <summary>The data directory holds something this build did not write
    /// for itself. Weaker than a recognised data file — it says somebody was
    /// here, not what they did — so it only ever produces Ambiguous.
    ///
    /// A directory containing nothing but our own first-run bookkeeping does
    /// NOT set this. It used to, and the result was that a brand new player
    /// was classified as a returning one on their very first session.</summary>
    public bool ConfiguredBeforeThisRelease { get; }

    /// <summary>The probe itself could not complete. Not evidence about the
    /// player, and deliberately resolves towards granting.</summary>
    public bool ProbeFailed { get; }

    public static LegacyEvidenceFacts None => default;

    public static LegacyEvidenceFacts Failed =>
        new LegacyEvidenceFacts(false, false, false, false, probeFailed: true);
}

/// <summary>The files this mod writes for itself, without anybody asking.
///
/// This list exists because the first in-game run of the companion build
/// granted a brand new character on a brand new world access as an EXISTING
/// user. The cause was circular: the plugin writes its starter survey rules
/// during startup, the legacy probe then finds <c>survey-rules.tsv</c> and
/// reads it as a deliberate past action, and so every fresh installation looks
/// like a returning player. The gate #264 asks for could never engage for
/// anyone.
///
/// Nothing here counts as evidence of anything. A file only says a player used
/// this mod before if a player had to do something to create it.</summary>
internal static class CartographerFirstRunFiles
{
    /// <summary>Exact names this build creates on its own.</summary>
    private static readonly string[] Names =
    {
        "author-id.txt",
        "survey-rules.tsv",
        "cartographer-strings-template.tsv",
        "onboarding-shown.txt",

        // The build between (commit 6903a65) wrote these two into the product
        // directory before the markers moved into "state". Only this mod ever
        // creates them, so a stray one is not evidence that a player was here
        // — and leaving them out is what made #343's rename grant the unlock
        // to every fresh install. They are adopted and removed on the next
        // start; this is what the profile reads as in the meantime.
        "author-id.dat",
        "onboarding-shown.dat",
    };

    /// <summary>Suffixes this build creates on its own. Companion sidecars are
    /// here because they are written the moment the companion system opens,
    /// and because they are scoped to one character: another character's
    /// sidecar is not evidence about this one.</summary>
    private static readonly string[] Suffixes =
    {
        ".companions.tsv",
        ".companions.tsv.tmp",

        // The quarantine name. `CompanionSidecarStore` renames an unreadable
        // sidecar to `<name>.companions.tsv.corrupt` (or `.corrupt.1` … `.19`)
        // in this same directory, and the sidecar itself is written the moment
        // the companion system opens, with no player involvement. Left out,
        // a brand-new profile whose first sidecar write was torn is read as a
        // returning player on the next launch and never sees the #264
        // introduction — #343 again, through a different file.
        //
        // Matched by Contains rather than EndsWith below, because the numbered
        // variants put digits after it.
        ".companions.tsv.corrupt",

        // A half-finished copy left in the data folder by an interrupted
        // relocation out of the settings folder (#304). Only this build ever
        // writes one, and it lands in the probed directory, so leaving it off
        // this list is #343 through yet another file.
        ".relocating.tmp",
    };

    /// <summary>True when <paramref name="fileName"/> is something this build
    /// wrote for itself rather than something a player did.
    ///
    /// A name we cannot read is NOT one of ours. Every unknown in this file
    /// resolves towards "somebody was here", because saying that to a new
    /// player costs them a few minutes of story and saying the opposite to a
    /// returning one takes their toolbar away.</summary>
    public static bool IsSelfWritten(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        foreach (string name in Names)
        {
            if (string.Equals(fileName, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (string suffix in Suffixes)
        {
            // Contains, not EndsWith: the sidecar quarantine appends an attempt
            // number after its suffix (`.corrupt.7`), and EndsWith could not see
            // those at all.
            if (fileName!.IndexOf(suffix, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a data directory holds nothing but this build's own
    /// bookkeeping — which is what a first run looks like, and must not be
    /// mistaken for a history.</summary>
    public static bool IsOnlySelfWritten(System.Collections.Generic.IReadOnlyList<string>? fileNames)
    {
        if (fileNames == null)
        {
            // A listing we could not take is not evidence that there is
            // nothing there.
            return false;
        }

        foreach (string name in fileNames)
        {
            if (!IsSelfWritten(name))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Turns "what is on disk" into the shared layer's
/// <see cref="LegacyEvidence"/> answer.
///
/// The asymmetry is deliberate and is the whole reason this is a separate,
/// tested rule rather than three ifs in an adapter. Saying Present to a new
/// player costs nothing — they see the tools a few minutes earlier than the
/// story intended. Saying None to a player who has been mapping for a year
/// takes their toolbar away and looks like the mod broke. So anything that
/// even resembles prior use resolves upward, and a probe that fell over
/// resolves upward too.</summary>
internal static class LegacyEvidenceRule
{
    public static LegacyEvidence Evaluate(LegacyEvidenceFacts facts)
    {
        // Real, world-scoped data this player made. The strongest signal there
        // is, and the common case for everyone upgrading.
        if (facts.ThisWorldHasData || facts.AnyWorldHasData)
        {
            return LegacyEvidence.Present;
        }

        // Deliberate profile-level actions: saving a view, writing a survey
        // rule, installing a translation. Nobody does any of those by
        // accident.
        if (facts.ProfileWideDataExists)
        {
            return LegacyEvidence.Present;
        }

        // A probe that threw knows nothing about the player. Ambiguous grants
        // access, and the grant is written down immediately, so one unreadable
        // directory cannot come back later as a lockout.
        if (facts.ProbeFailed)
        {
            return LegacyEvidence.Ambiguous;
        }

        // A config file alone is weak: it appears on first launch, before the
        // player has done anything. Ambiguous is exactly right — it grants,
        // and it is honest about why.
        if (facts.ConfiguredBeforeThisRelease)
        {
            return LegacyEvidence.Ambiguous;
        }

        return LegacyEvidence.None;
    }
}
