namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Which of his two bodies Thorstein should have right now. One at a
/// time, or none.</summary>
internal enum ThorsteinBodyKind
{
    /// <summary>Neither. Nothing of him is shown, and no lease is held.</summary>
    None = 0,

    /// <summary>The local presentation figure at home: the owner's
    /// Mullet-and-Mustache Thorstein, extracted dark from the game's own
    /// character, never saved and never networked.</summary>
    Presentation = 1,

    /// <summary>The saved worker body (<c>CF_SettlementWorker</c>), which
    /// carries his issued tools and anything he has gathered in its own network
    /// object.</summary>
    Worker = 2,
}

/// <summary>Everything the decision needs, read by the runtime once per pass.
/// Game-free, so that the whole decision table is tested without the game.
/// </summary>
internal readonly struct ThorsteinBodyFacts
{
    internal ThorsteinBodyFacts(
        bool registered,
        bool worldLoaded,
        bool isHost,
        bool settlementEnabled,
        bool presentationEnabled,
        bool recruited,
        int savedWorkerBodies,
        bool resting,
        bool homeKnown,
        bool playerNearHome,
        bool introduced = false)
    {
        Registered = registered;
        WorldLoaded = worldLoaded;
        IsHost = isHost;
        SettlementEnabled = settlementEnabled;
        PresentationEnabled = presentationEnabled;
        Recruited = recruited;
        SavedWorkerBodies = savedWorkerBodies;
        Resting = resting;
        HomeKnown = homeKnown;
        PlayerNearHome = playerNearHome;
        Introduced = introduced;
    }

    /// <summary>#295 S3: this character has examined the carpenter's square and
    /// he is waiting for an axe and a hammer, so he is at home before he is on
    /// the roster. A finished story without employment (a dismissed worker) is
    /// not this: it shows nobody.</summary>
    internal bool Introduced { get; }

    /// <summary>Concerned NPC accepted his role. Without it no lease can be
    /// held, so nothing of him may be built.</summary>
    internal bool Registered { get; }

    internal bool WorldLoaded { get; }

    /// <summary>This process is the server and not a dedicated one. Only there
    /// is the census of saved bodies the whole world rather than whatever is
    /// near.</summary>
    internal bool IsHost { get; }

    /// <summary><c>Settlement/SettlementRuntimeEnabled</c>. Thorstein belongs to
    /// the settlement half, which is opt-in.</summary>
    internal bool SettlementEnabled { get; }

    /// <summary><c>Thorstein/ShowAtHome</c>.</summary>
    internal bool PresentationEnabled { get; }

    /// <summary>The settlement roster employs him. Unreadable counts as no:
    /// a figure of somebody nobody recruited is a stranger in the player's
    /// house.</summary>
    internal bool Recruited { get; }

    /// <summary>How many saved worker bodies carry his identity in this world,
    /// loaded or not. Negative when it is not known: nobody counted yet, or
    /// this process cannot see the whole world.</summary>
    internal int SavedWorkerBodies { get; }

    /// <summary>No job holds him (<c>MayRetireBody</c>).</summary>
    internal bool Resting { get; }

    /// <summary>A respawn anchor (a current claimed bed, else the world start)
    /// is known.</summary>
    internal bool HomeKnown { get; }

    /// <summary>The local player is close enough to home that it is loaded
    /// ground worth showing him in.</summary>
    internal bool PlayerNearHome { get; }
}

/// <summary>The answer and its reason, for <c>cf_worker status</c>.</summary>
internal readonly struct ThorsteinBodyDecision
{
    internal ThorsteinBodyDecision(ThorsteinBodyKind kind, string reason)
    {
        Kind = kind;
        Reason = reason ?? string.Empty;
    }

    internal ThorsteinBodyKind Kind { get; }

    internal string Reason { get; }

    public override string ToString() => Kind + " (" + Reason + ")";
}

/// <summary>Which body Thorstein should have (#295 step 1).
///
/// <b>The rule this exists to keep: the presentation body and the worker body
/// never coexist</b> (CLAUDE.md). His worker body is persistent. It is spawned
/// once, saved with the world, and never removed while it carries anything,
/// because his issued axe and hammer and anything he has gathered live inside
/// it (SETTLEMENT_AUTHORITY.md §5a). So <b>a saved worker body anywhere in the
/// world is his body</b>, loaded or not, idle or not, and while one exists the
/// figure is never shown. That is checked first and outranks everything else.
///
/// The figure appears only when he has no saved body at all: recruited, resting,
/// and with a home to stand at. In this build that is the time between
/// <c>cf_settle recruit</c> and <c>cf_worker spawn</c>, and again after a
/// <c>cf_worker despawn</c>, which itself refuses while he carries anything.
/// When an order wants a worker body, the hand-over is release-then-claim
/// (<see cref="ThorsteinBodies.TryTakeWorker"/>).
///
/// Every unknown fails towards showing nothing. An uncounted world, a client
/// that cannot see the whole world, an unreadable roster or a job in flight
/// each mean no figure, never a guess that might be a second Thorstein.
///
/// The arbiter in Concerned NPC is the structural backstop for the same rule.
/// This decides what to ask for; the arbiter refuses the ask if it would make
/// two.</summary>
internal static class ThorsteinBodyPlan
{
    internal static ThorsteinBodyDecision Decide(in ThorsteinBodyFacts facts)
    {
        if (!facts.Registered)
        {
            return new ThorsteinBodyDecision(
                ThorsteinBodyKind.None, "Concerned NPC did not accept his role, so no body may be held");
        }

        if (!facts.WorldLoaded)
        {
            return new ThorsteinBodyDecision(ThorsteinBodyKind.None, "no world is loaded");
        }

        if (facts.SavedWorkerBodies > 0)
        {
            // First, and unconditionally. A saved body is his body wherever it
            // is standing; showing a figure as well would be two of him.
            return new ThorsteinBodyDecision(
                ThorsteinBodyKind.Worker,
                facts.SavedWorkerBodies == 1
                    ? "his worker body is in this world"
                    : "this world holds more than one worker body with his identity");
        }

        if (!facts.IsHost)
        {
            return new ThorsteinBodyDecision(
                ThorsteinBodyKind.None,
                "only the host can see every saved body, so a figure is never shown elsewhere");
        }

        if (facts.SavedWorkerBodies < 0)
        {
            return new ThorsteinBodyDecision(
                ThorsteinBodyKind.None, "his saved bodies have not been counted in this world yet");
        }

        if (!facts.SettlementEnabled)
        {
            return new ThorsteinBodyDecision(ThorsteinBodyKind.None, "the settlement runtime is off");
        }

        if (!facts.PresentationEnabled)
        {
            return new ThorsteinBodyDecision(ThorsteinBodyKind.None, "Thorstein/ShowAtHome is off");
        }

        if (!facts.Recruited && !facts.Introduced)
        {
            return new ThorsteinBodyDecision(
                ThorsteinBodyKind.None,
                "he has not introduced himself yet (examine the carpenter's square) and is not on this " +
                "settlement's roster");
        }

        if (!facts.Resting)
        {
            return new ThorsteinBodyDecision(
                ThorsteinBodyKind.None, "an order holds him, so he is not at home");
        }

        if (!facts.HomeKnown)
        {
            return new ThorsteinBodyDecision(
                ThorsteinBodyKind.None, "neither a claimed bed nor the world start could be found");
        }

        if (!facts.PlayerNearHome)
        {
            return new ThorsteinBodyDecision(ThorsteinBodyKind.None, "you are not near home");
        }

        return new ThorsteinBodyDecision(ThorsteinBodyKind.Presentation, "at home, resting");
    }
}
