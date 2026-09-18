using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>The caller's clock, in seconds (<c>Time.time</c> in the adapter).
/// </summary>
internal interface IHaulClock
{
    float Now { get; }
}

/// <summary>The work-authority answer, computed from facts gathered at the
/// moment of asking (DECISIONS.md D3). Asked before every mutation: attach,
/// every motor command, every lease assignment.</summary>
internal interface IHaulAuthority
{
    WorkAuthorityVerdict Evaluate();
}

/// <summary>Gunnar's body, as the executor may use it. Every motion goes
/// through the vanilla motor (<c>BaseAI.MoveTo</c> / <c>MoveTowards</c> →
/// <c>Character.SetMoveDir</c> → <c>UpdateWalking</c>); nothing here writes a
/// position, a velocity or a force.</summary>
internal interface IPullerBody
{
    PullerBodyFacts Read();

    /// <summary>Walks toward a point with the body's own pathfinding, at walking
    /// pace. Arrival is decided by the caller from distance, never from the
    /// motor's "stopped" answer.</summary>
    void WalkTo(WorkPoint target, float arrivalRadiusMetres);

    /// <summary>Steers straight toward a point along a segment the route planner
    /// vouched for, at walking pace.</summary>
    void SteerToward(WorkPoint target);

    /// <summary>Turns toward a horizontal direction without moving.</summary>
    void Face(float directionX, float directionZ);

    /// <summary>Stops commanding motion. Always allowed: it only releases
    /// control.</summary>
    void Stop();

    /// <summary>Re-applies the D5 calibration to the body's own base mass.
    /// False when it cannot (no measurement of the local player).</summary>
    bool Calibrate();

    /// <summary>Retires the body. The executor calls it only after the joint
    /// has been released and the identity is resting.</summary>
    void Retire();
}

/// <summary>The attach seam over the cart (DECISIONS.md D4, CONTRACTS.md §2.5):
/// the cart's own attach and detach, called directly, never its interaction,
/// ownership request or owner change.</summary>
internal interface ICartHitchSeam
{
    /// <summary>The startup capability probe verified every member the seam
    /// uses. When false, hauling is unavailable and nothing else changes.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>What the probe found missing, for the one log line.</summary>
    string UnavailableDetail { get; }

    CartObservation Observe(CartKey cart);

    ParkingGround ReadGround(CartKey cart);

    /// <summary>Calls the cart's attach toward Gunnar's body and verifies, in
    /// the same call, that the joint's connected body is his, that the cart
    /// reports itself attached and that its attach flag is set. Any failure
    /// detaches again and answers <see cref="HitchRefusal.VerifyFailed"/>.
    /// </summary>
    AttachResult AttachAndVerify(CartKey cart);

    /// <summary>Releases the joint through the cart's own detach, only when the
    /// joint is Gunnar's or no joint exists; never another puller's.</summary>
    ReleaseResult ReleaseJoint(CartKey cart);
}

/// <summary>Where the executor reports. Main thread; never per frame.</summary>
internal interface IHaulExecutionLog
{
    void Info(string message);

    void Warning(string message);

    /// <summary>Something the design says cannot happen, such as an illegal
    /// phase transition. Always reported.</summary>
    void Bug(string message);
}
