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

    /// <summary>The mod's config file records a version older than the one
    /// that introduced companions. Weaker than a data file — a config is
    /// written on first launch — so it only ever produces Ambiguous.</summary>
    public bool ConfiguredBeforeThisRelease { get; }

    /// <summary>The probe itself could not complete. Not evidence about the
    /// player, and deliberately resolves towards granting.</summary>
    public bool ProbeFailed { get; }

    public static LegacyEvidenceFacts None => default;

    public static LegacyEvidenceFacts Failed =>
        new LegacyEvidenceFacts(false, false, false, false, probeFailed: true);
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
