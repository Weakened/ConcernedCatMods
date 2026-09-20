using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>The four things only the running game can do for a build order:
/// walk Thorstein, move material, put a piece in the world, and make him look
/// like he is working.
///
/// <b>Why they are declared here and not beside their adapters.</b> Everything
/// that decides <i>when</i> to walk, <i>how much</i> to fetch and <i>whether</i>
/// to place is in <see cref="ShelterBuildLoop"/>, which is proved with no game.
/// A port declared next to its game-bound implementation drifts towards the
/// implementation's convenience; a port declared next to its only caller says
/// what the caller actually needs, which is why each of these is four or five
/// members rather than a window onto the engine.
///
/// <b>None of them decides anything.</b> A port that could refuse for a reason
/// of its own would be a second policy, invisible to the tests that prove the
/// first one.</summary>
internal enum BuildWalkStatus
{
    /// <summary>Nobody said. Never treated as arrival.</summary>
    Unspecified = 0,

    /// <summary>No goal.</summary>
    Idle = 1,

    Walking = 2,

    /// <summary>Within the requested tolerance of the goal, <b>by distance</b>.
    /// </summary>
    Arrived = 3,

    /// <summary>The movement planner gave up; <see cref="IBuildWalk.Deferral"/>
    /// says why.</summary>
    Deferred = 4,
}

/// <summary>Walking Thorstein, for the build loop.
///
/// This is deliberately a narrower thing than the worker-motion seam it is
/// implemented over (<c>IWorkerMotion</c>): the loop never chooses a job id,
/// because only one job may hold the worker and the adapter is the thing that
/// holds it. A loop that could name the job id could walk a body another job
/// was holding.</summary>
internal interface IBuildWalk
{
    /// <summary>The body exists, is owned here and is not faulted.</summary>
    bool IsPresent { get; }

    SitePoint Position { get; }

    BuildWalkStatus Status { get; }

    /// <summary>Why the planner gave up, in the player's words. Empty when it
    /// has not.</summary>
    string Deferral { get; }

    /// <summary>Starts or replaces a walk. False when the body is not present or
    /// this loop does not hold him.</summary>
    bool WalkTo(SitePoint point, float arrivalTolerance);

    void Stop();
}

/// <summary>What one draw out of the permitted supply container produced.
///
/// <b>Drawn and Short are both measured, never inferred.</b> A draw that reports
/// what it asked for rather than what actually moved is how phantom material
/// gets into a plan: the loop would then walk to a wall carrying nothing and
/// blame the placement gate.</summary>
internal readonly struct BuildDraw
{
    private BuildDraw(MaterialTally? drawn, MaterialTally? shortBy, string refusal)
    {
        Drawn = drawn ?? new MaterialTally();
        Short = shortBy ?? new MaterialTally();
        Refusal = refusal ?? string.Empty;
    }

    /// <summary>What actually reached Thorstein's own inventory.</summary>
    internal MaterialTally Drawn { get; }

    /// <summary>What was asked for and is not there, per item.</summary>
    internal MaterialTally Short { get; }

    /// <summary>Why nothing could be drawn at all - no container marked, the
    /// chest locked, the record unwritable. Empty when the draw happened, even
    /// if it came up short.</summary>
    internal string Refusal { get; }

    internal bool IsRefused => Refusal.Length != 0;

    internal static BuildDraw Moved(MaterialTally? drawn, MaterialTally? shortBy) =>
        new BuildDraw(drawn, shortBy, refusal: string.Empty);

    internal static BuildDraw Refused(string why) =>
        new BuildDraw(drawn: null, shortBy: null, refusal: string.IsNullOrEmpty(why)
            ? "the supply container could not be used, and nothing said why"
            : why);

    public override string ToString() => IsRefused
        ? "refused (" + Refusal + ")"
        : "drew " + Drawn.Describe() + ", short " + Short.Describe();
}

/// <summary>A build order's material: the one container the player permitted,
/// Thorstein's own persisted inventory, and the two movements between them.
///
/// <b>Four operations, and the conservation argument is exactly these four.</b>
/// Material enters through <see cref="Draw"/>, sits in <see cref="Carried"/>,
/// leaves through <see cref="Spend"/> when a piece has actually gone up, or goes
/// back through <see cref="PutBack"/>. There is no fifth way, which is what lets
/// a test add up the container, what he carries and what is standing and demand
/// the same number after any sequence of ticks, interruptions and reloads.
///
/// <b>Nothing here creates or destroys material.</b> Every implementation moves
/// between two real inventories and reports the measured delta. An
/// implementation that reported its intention instead would make the invariant
/// above a statement about arithmetic rather than about a player's chest.</summary>
internal interface IBuildMaterials
{
    /// <summary>Where the permitted supply container is, so he can walk to it.
    /// </summary>
    /// <returns>False with a named refusal when there is no permitted container
    /// in this world load. <b>Never the nearest chest</b>: the player named one.
    /// </returns>
    bool TrySupply(out SitePoint at, out string refusal);

    /// <summary>What Thorstein is holding for this order right now, read from
    /// his own inventory rather than remembered.</summary>
    MaterialTally Carried { get; }

    /// <summary>Moves up to <paramref name="wanted"/> out of the permitted
    /// container and into his own inventory, once.</summary>
    BuildDraw Draw(MaterialTally wanted);

    /// <summary>Takes one piece's real cost out of what he carries, at the
    /// moment that piece has gone up.</summary>
    /// <param name="spent">What actually left his inventory.</param>
    /// <param name="failure">Why it could not be taken. Empty on success.
    /// </param>
    bool Spend(PieceRecipe recipe, out MaterialTally spent, out string failure);

    /// <summary>Puts everything he still carries for this order back into the
    /// container it came out of.</summary>
    /// <returns>What actually went back. What did not is still in his inventory
    /// and <paramref name="failure"/> says why, because "it is gone" and "it is
    /// on him" are the two answers a player has to be able to tell apart.
    /// </returns>
    MaterialTally PutBack(out string failure);
}

/// <summary>The visible work: Thorstein's own body, looking like it is building.
///
/// <b>Presentation, and nothing but.</b> Whether a piece goes up is decided by
/// the placement gate; this only makes the moment legible. A failure here is
/// swallowed by the adapter, because a cottage that goes up without the arm
/// movement is a disappointment and a build that stops because of an animator is
/// a defect.</summary>
internal interface IBuildPose
{
    /// <summary>Starts or stops the working pose on the worker's own body.
    /// Idempotent: the loop calls it every round it is working.</summary>
    void Working(bool on);
}

/// <summary>How one attempt to place one piece ended, in the loop's own words.
///
/// <b>The three answers are not interchangeable and the loop treats each
/// differently.</b> A refusal is the system working and nothing was spent; a
/// failure is the system not working and nothing was spent either, but retrying
/// it is pointless in a way a refusal's is not; and a placement means the world
/// has a new piece in it and the material for it must now leave his
/// inventory.</summary>
internal enum PiecePlaced
{
    /// <summary>Nothing was attempted. Never a success.</summary>
    Unspecified = 0,

    /// <summary>The piece is standing, or at least the one call that creates it
    /// was made. Whether it is really there is answered by the next look at the
    /// site, which is the only thing that ever answers it.</summary>
    Placed = 1,

    /// <summary>A gate refused it. Nothing was spent.</summary>
    Refused = 2,

    /// <summary>Every gate allowed it and the placement itself did not happen.
    /// Nothing was spent.</summary>
    Failed = 3,
}

/// <summary>The one thing that puts a vanilla piece in the world, as the loop
/// sees it.
///
/// <b>The gate is inside the implementation, on purpose.</b> "Check, then place,
/// and never place without checking" is not a sequence the loop is trusted to
/// get right: the product's <c>WorldPiecePlacer</c> runs
/// <see cref="PlacementGate.May"/> and only then the installer, and this port is
/// the shape of that single call. A port that placed without gating, or a loop
/// that gated for itself, would make the ordering a convention again.</summary>
internal interface IPiecePlacer
{
    /// <summary>Gates and then places one piece.</summary>
    /// <param name="piece">What, where, and what it really costs.</param>
    /// <param name="carried">What Thorstein is holding. The cost is checked
    /// against this and never against a chest near the site.</param>
    /// <param name="authorised">Whether a player confirmed the order.</param>
    /// <param name="reason">The named refusal or failure, for the player. Empty
    /// when the piece went up.</param>
    PiecePlaced Place(CostedPiece piece, MaterialTally carried, bool authorised, out string reason);
}
