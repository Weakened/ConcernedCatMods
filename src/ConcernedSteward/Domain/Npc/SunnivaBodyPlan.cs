namespace TheConcernedCat.ConcernedSteward.Domain.Npc;

/// <summary>Everything the decision needs, gathered by the runtime each pass.
/// Game-free, so every branch is a unit test.</summary>
internal readonly struct SunnivaBodyFacts
{
    internal SunnivaBodyFacts(
        bool registered,
        bool worldLoaded,
        bool isHost,
        bool runtimeEnabled,
        bool showAtHome,
        bool settled,
        int savedWorkerBodies,
        bool resting,
        bool homeKnown,
        bool playerNearHome)
    {
        Registered = registered;
        WorldLoaded = worldLoaded;
        IsHost = isHost;
        RuntimeEnabled = runtimeEnabled;
        ShowAtHome = showAtHome;
        Settled = settled;
        SavedWorkerBodies = savedWorkerBodies;
        Resting = resting;
        HomeKnown = homeKnown;
        PlayerNearHome = playerNearHome;
    }

    /// <summary>Her role is registered with Concerned NPC.</summary>
    public bool Registered { get; }

    public bool WorldLoaded { get; }

    /// <summary>Single player or the host of a game: only there is the count of
    /// saved bodies the whole world's.</summary>
    public bool IsHost { get; }

    public bool RuntimeEnabled { get; }

    public bool ShowAtHome { get; }

    /// <summary>She is taken on and has moved in (her quest is Settled).</summary>
    public bool Settled { get; }

    /// <summary>Saved <c>CS_Steward</c> bodies with her identity anywhere in the
    /// world, loaded or not. Negative when it could not be counted.</summary>
    public int SavedWorkerBodies { get; }

    /// <summary>No round is open, no plan is unsettled, nothing is unreceipted
    /// and no fuel is unaccounted for.</summary>
    public bool Resting { get; }

    public bool HomeKnown { get; }

    public bool PlayerNearHome { get; }
}

/// <summary>Whether Sunniva is shown at home as a figure, and why.</summary>
internal readonly struct SunnivaBodyDecision
{
    internal SunnivaBodyDecision(bool showFigure, string reason)
    {
        ShowFigure = showFigure;
        Reason = reason ?? string.Empty;
    }

    public bool ShowFigure { get; }

    public string Reason { get; }

    public override string ToString() => (ShowFigure ? "the figure at home" : "no figure") + " (" + Reason + ")";
}

/// <summary>When Sunniva rests as a player-style figure at her home post
/// instead of having no body at all (#382, owner decision 2026-10-27; the seam
/// is Concerned NPC's <c>NpcBodyContract.ForWorkerWithPresentation</c>).
///
/// <b>A saved worker body outranks everything, and is checked first.</b> While
/// one with her identity exists anywhere in the world - loaded or not - she is
/// that body and the figure is never asked for. An unidentified <c>CS_Steward</c>
/// body counts as unknown. Her worker body leaves the world by dying or, with
/// #475, through the guarded retire verb (S1); either way the figure appears only
/// once nothing is open.
///
/// <b>Anything unknown shows nothing.</b> A count that failed, a world not up, a
/// client that is not the host, or a home that cannot be found all answer "no
/// figure"; showing nothing is always safe, and showing her beside her own body
/// is the one outcome this exists to prevent.</summary>
internal static class SunnivaBodyPlan
{
    internal static SunnivaBodyDecision Decide(in SunnivaBodyFacts facts)
    {
        if (!facts.Registered)
        {
            return No("her role is not registered with Concerned NPC");
        }

        if (!facts.WorldLoaded)
        {
            return No("no world is loaded");
        }

        if (facts.SavedWorkerBodies > 0)
        {
            return No("her worker body is in the world");
        }

        if (!facts.IsHost)
        {
            return No("only the host can count her bodies");
        }

        if (facts.SavedWorkerBodies < 0)
        {
            return No("her bodies could not be counted");
        }

        if (!facts.RuntimeEnabled)
        {
            return No("Steward/StewardRuntimeEnabled is off");
        }

        if (!facts.ShowAtHome)
        {
            return No("Appearance/ShowAtHome is off");
        }

        if (!facts.Settled)
        {
            return No("she has not moved in yet");
        }

        if (!facts.Resting)
        {
            return No("a round or its record is still open");
        }

        if (!facts.HomeKnown)
        {
            return No("her home post is not known (mark the settlement and the supply chest)");
        }

        if (!facts.PlayerNearHome)
        {
            return No("nobody is near her home post");
        }

        return new SunnivaBodyDecision(true, "she is resting at home");
    }

    private static SunnivaBodyDecision No(string reason) => new SunnivaBodyDecision(false, reason);
}
