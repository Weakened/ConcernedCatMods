using System;

namespace TheConcernedCat.Workers.Traversal;

/// <summary>Why an actor may not use a link. Never
/// <see cref="Unspecified"/> on a refusal: a refusal without a reason is
/// indistinguishable from a bug.</summary>
internal enum TraversalRefusal
{
    Unspecified = 0,

    /// <summary>Traversal is switched off.</summary>
    Disabled = 1,

    /// <summary>This actor may not use this kind of link: NPC climbing is off,
    /// or this worker was never granted it. The default answer for every worker
    /// today.</summary>
    NotCapable = 2,

    /// <summary>The worker rules are not satisfied: not opted in, not the host,
    /// dedicated, or other peers are connected. Workers only; the player is
    /// never subject to it.</summary>
    NoAuthority = 3,

    /// <summary>There is no such link any more. The ladder was destroyed,
    /// unloaded or rebuilt, and the id in hand refers to something that has
    /// gone.</summary>
    LinkGone = 4,

    /// <summary>The link is taller than this actor is allowed to commit to.
    /// </summary>
    TooTall = 5,

    /// <summary>The actor is at neither end of it.</summary>
    NotAtAnEndpoint = 6,

    /// <summary>Dead, attached to something, swimming, carrying something that
    /// takes both hands: not in a state to take a link.</summary>
    ActorBusy = 7,

    /// <summary>Already on a link.</summary>
    AlreadyTraversing = 8,
}

/// <summary>What the caller wants this step to do. Deliberately <b>not</b> an
/// input axis: a player's controller turns keys into one of these, and a
/// worker's driver turns its goal into one of these, so no worker code ever
/// reaches into player input handling (`LADDERS.md` decision L6).</summary>
internal enum TraversalIntent
{
    Unspecified = 0,

    /// <summary>Keep going towards the far end.</summary>
    Advance = 1,

    /// <summary>Stop where you are, still on the link.</summary>
    Hold = 2,

    /// <summary>Go back the way you came.</summary>
    Reverse = 3,
}

/// <summary>Where a traversal has got to.</summary>
internal enum TraversalPhase
{
    Unspecified = 0,

    Advancing = 1,

    Holding = 2,

    Reversing = 3,

    /// <summary>At the far end. The caller's next call is
    /// <see cref="ILadderTraversal.Exit"/> with
    /// <see cref="TraversalExitKind.Arrived"/>.</summary>
    ReachedFarEnd = 4,

    /// <summary>Back at the end it started from, having changed its mind.
    /// </summary>
    ReturnedToStart = 5,

    /// <summary>The link or the actor is gone from under the traversal. The
    /// caller must exit <b>now</b>, and the exit never places a body: the same
    /// rule the ladder domain's safety check applies to a player.</summary>
    Lost = 6,
}

/// <summary>How a traversal ended.</summary>
internal enum TraversalExitKind
{
    Unspecified = 0,

    /// <summary>Reached the far end and stepped off there.</summary>
    Arrived = 1,

    /// <summary>Came back and stepped off where it started.</summary>
    TurnedBack = 2,

    /// <summary>Let go on purpose part way along.</summary>
    LetGo = 3,

    /// <summary>Let go without choosing to: the link went, the actor died, the
    /// runtime stopped. Never places a body.</summary>
    Interrupted = 4,
}

/// <summary>Everything a traversal takes away from a body while it holds it, and
/// therefore everything it must give back. The worker-side mirror of the ladder
/// domain's restoration checklist; the adapter that owns both is where the two
/// are kept in step.</summary>
internal readonly struct TraversalRestoration
{
    public TraversalRestoration(bool restoreGravity, bool restoreMotor, bool restoreCollisions, bool clearPose)
    {
        RestoreGravity = restoreGravity;
        RestoreMotor = restoreMotor;
        RestoreCollisions = restoreCollisions;
        ClearPose = clearPose;
    }

    public bool RestoreGravity { get; }

    public bool RestoreMotor { get; }

    public bool RestoreCollisions { get; }

    public bool ClearPose { get; }

    public bool IsComplete => RestoreGravity && RestoreMotor && RestoreCollisions && ClearPose;

    /// <summary>The only value any exit ever carries. It is a constant rather
    /// than a decision on purpose: there is no exit path on which some of a body
    /// is handed back.</summary>
    public static TraversalRestoration Everything => new TraversalRestoration(true, true, true, true);
}

/// <summary>What the actor is doing when it asks, with no engine type in sight
/// and nothing about how it was asked.</summary>
internal readonly struct TraversalRequest
{
    public TraversalRequest(
        TraversalActor actor,
        string? linkId,
        WorkPoint standing,
        bool featureEnabled,
        WorkAuthorityVerdict authority,
        bool actorReady,
        bool alreadyTraversing,
        TraversalDirection direction = TraversalDirection.Unspecified)
    {
        Actor = actor;
        LinkId = linkId;
        Standing = standing;
        FeatureEnabled = featureEnabled;
        Authority = authority;
        ActorReady = actorReady;
        AlreadyTraversing = alreadyTraversing;
        Direction = direction;
    }

    public TraversalActor Actor { get; }

    /// <summary>Which link. Resolved against the published network at the moment
    /// of asking, so an id for something that has gone is caught here rather
    /// than half way up it.</summary>
    public string? LinkId { get; }

    /// <summary>Where the actor's feet are.</summary>
    public WorkPoint Standing { get; }

    /// <summary>The feature switch: <c>Ladders/Enabled</c> for a player.</summary>
    public bool FeatureEnabled { get; }

    /// <summary>The worker-authority verdict this peer computed just now.
    /// Consulted <b>only for a worker</b>: a player climbing their own ladder on
    /// someone else's server is not a worker mutation, and gating it on being
    /// the host would break the feature for every client.</summary>
    public WorkAuthorityVerdict Authority { get; }

    /// <summary>Alive, on its feet, not attached to anything, not swimming.
    /// False is the safe default.</summary>
    public bool ActorReady { get; }

    public bool AlreadyTraversing { get; }

    /// <summary><see cref="TraversalDirection.Unspecified"/> means "work it out
    /// from where I am standing", which is what both a walking player and an
    /// arriving worker want.</summary>
    public TraversalDirection Direction { get; }

    /// <summary>A worker in the ordinary case: authorised, ready, not already on
    /// something.</summary>
    public static TraversalRequest ForWorker(
        WorkerKey worker,
        string linkId,
        WorkPoint standing,
        TraversalCapability capability = TraversalCapability.Ladders,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted) =>
        new TraversalRequest(
            TraversalActor.OfWorker(worker, capability),
            linkId,
            standing,
            featureEnabled: true,
            authority,
            actorReady: true,
            alreadyTraversing: false);

    /// <summary>The player in the ordinary case.</summary>
    public static TraversalRequest ForPlayer(string linkId, WorkPoint standing, bool featureEnabled = true) =>
        new TraversalRequest(
            TraversalActor.LocalPlayer(),
            linkId,
            standing,
            featureEnabled,
            WorkAuthorityVerdict.Unspecified,
            actorReady: true,
            alreadyTraversing: false);
}

/// <summary>The answer to "may this actor use this link, and which way".</summary>
internal readonly struct TraversalVerdict
{
    private TraversalVerdict(
        bool allowed,
        TraversalRefusal refusal,
        TraversalLink link,
        TraversalDirection direction)
    {
        Allowed = allowed;
        Refusal = refusal;
        Link = link;
        Direction = direction;
    }

    public bool Allowed { get; }

    public TraversalRefusal Refusal { get; }

    /// <summary>The link as it stands right now. Meaningless on a refusal.
    /// </summary>
    public TraversalLink Link { get; }

    public TraversalDirection Direction { get; }

    /// <summary>Where the actor steps off, if it goes all the way.</summary>
    public WorkPoint Destination => Link.ExitFor(Direction);

    public static TraversalVerdict Allow(in TraversalLink link, TraversalDirection direction)
    {
        if (direction == TraversalDirection.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "An allowed traversal has a direction.");
        }

        return new TraversalVerdict(true, TraversalRefusal.Unspecified, link, direction);
    }

    public static TraversalVerdict Refuse(TraversalRefusal refusal)
    {
        if (refusal == TraversalRefusal.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal), "A refusal needs a reason.");
        }

        return new TraversalVerdict(false, refusal, default, TraversalDirection.Unspecified);
    }

    /// <summary>One sentence, for a log. Worker refusals are never shown to a
    /// player as if they were about the player.</summary>
    public string Explain()
    {
        switch (Refusal)
        {
            case TraversalRefusal.Disabled:
                return "Traversal is switched off.";
            case TraversalRefusal.NotCapable:
                return "That one does not climb.";
            case TraversalRefusal.NoAuthority:
                return "Workers may not act here.";
            case TraversalRefusal.LinkGone:
                return "That way up is not there any more.";
            case TraversalRefusal.TooTall:
                return "That climb is longer than a worker may commit to.";
            case TraversalRefusal.NotAtAnEndpoint:
                return "Not standing at either end of it.";
            case TraversalRefusal.ActorBusy:
                return "Not while it is doing that.";
            case TraversalRefusal.AlreadyTraversing:
                return "Already on one.";
            default:
                return Allowed ? "Allowed." : "Refused.";
        }
    }
}

/// <summary>A traversal in flight. Opaque on purpose: the caller hands it back
/// and nothing else.
///
/// It carries the link's <see cref="Generation"/> so that a link retired and
/// republished under the same id — a ladder rebuilt where the old one stood —
/// invalidates a traversal that started on the old one, instead of silently
/// carrying a body up something else.</summary>
internal readonly struct TraversalHandle : IEquatable<TraversalHandle>
{
    internal TraversalHandle(string linkId, int generation, TraversalActor actor, TraversalDirection direction)
    {
        LinkId = linkId;
        Generation = generation;
        Actor = actor;
        Direction = direction;
    }

    public string? LinkId { get; }

    public int Generation { get; }

    public TraversalActor Actor { get; }

    public TraversalDirection Direction { get; }

    public bool IsValid =>
        !string.IsNullOrEmpty(LinkId) && Generation > 0 && Direction != TraversalDirection.Unspecified;

    public bool Equals(TraversalHandle other) =>
        string.Equals(LinkId, other.LinkId, StringComparison.Ordinal) &&
        Generation == other.Generation &&
        Direction == other.Direction &&
        string.Equals(Actor.Identity, other.Actor.Identity, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TraversalHandle other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = LinkId == null ? 0 : StringComparer.Ordinal.GetHashCode(LinkId);
            hash = (hash * 31) + Generation;
            hash = (hash * 31) + (int)Direction;
            return (hash * 31) + StringComparer.Ordinal.GetHashCode(Actor.Identity);
        }
    }

    public override string ToString() =>
        IsValid ? Actor + " on '" + LinkId + "'#" + Generation + " going " + Direction : "<no traversal>";
}

/// <summary>Started, or refused with a reason.</summary>
internal readonly struct TraversalEntry
{
    private TraversalEntry(bool started, TraversalVerdict verdict, TraversalHandle handle)
    {
        Started = started;
        Verdict = verdict;
        Handle = handle;
    }

    public bool Started { get; }

    public TraversalVerdict Verdict { get; }

    /// <summary>Meaningless unless <see cref="Started"/>.</summary>
    public TraversalHandle Handle { get; }

    public static TraversalEntry Begin(in TraversalVerdict verdict, in TraversalHandle handle) =>
        new TraversalEntry(true, verdict, handle);

    public static TraversalEntry Refused(in TraversalVerdict verdict) =>
        new TraversalEntry(false, verdict, default);
}

/// <summary>One step's worth of traversal.</summary>
internal readonly struct TraversalProgress
{
    public TraversalProgress(TraversalPhase phase, float fraction, float metresFromEntry, WorkPoint estimatedPosition)
    {
        Phase = phase;
        Fraction = fraction;
        MetresFromEntry = metresFromEntry;
        EstimatedPosition = estimatedPosition;
    }

    public TraversalPhase Phase { get; }

    /// <summary>0 at the end it started from, 1 at the far end.</summary>
    public float Fraction { get; }

    public float MetresFromEntry { get; }

    /// <summary>A straight-line estimate. The body's real position is the
    /// product's to compute; this is for a caller that has nothing better and
    /// for a test that needs a number.</summary>
    public WorkPoint EstimatedPosition { get; }

    /// <summary>True when the caller must call <see cref="ILadderTraversal.Exit"/>
    /// on this step rather than the next one.</summary>
    public bool MustExit =>
        Phase == TraversalPhase.Lost ||
        Phase == TraversalPhase.ReachedFarEnd ||
        Phase == TraversalPhase.ReturnedToStart;

    public static TraversalProgress Lost => new TraversalProgress(TraversalPhase.Lost, 0f, 0f, default);
}

/// <summary>Where the actor ends up, and what has to be given back.</summary>
internal readonly struct TraversalRelease
{
    private TraversalRelease(TraversalExitKind kind, WorkPoint landing, bool placeActor)
    {
        Kind = kind;
        Landing = landing;
        PlaceActor = placeActor;
    }

    public TraversalExitKind Kind { get; }

    /// <summary>Meaningful only when <see cref="PlaceActor"/> is true.</summary>
    public WorkPoint Landing { get; }

    /// <summary>Whether the caller moves the body at all. A let-go or an
    /// interruption never does: moving a body that was just killed, thrown or
    /// unloaded is how mods put people inside terrain.</summary>
    public bool PlaceActor { get; }

    /// <summary>Always complete, on every path.</summary>
    public TraversalRestoration Restoration => TraversalRestoration.Everything;

    public static TraversalRelease Step(TraversalExitKind kind, WorkPoint landing)
    {
        if (kind != TraversalExitKind.Arrived && kind != TraversalExitKind.TurnedBack)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Only a deliberate step-off places a body.");
        }

        return new TraversalRelease(kind, landing, placeActor: true);
    }

    public static TraversalRelease Release(TraversalExitKind kind)
    {
        if (kind != TraversalExitKind.LetGo && kind != TraversalExitKind.Interrupted)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "A step-off has a landing.");
        }

        return new TraversalRelease(kind, default, placeActor: false);
    }
}

/// <summary>The seam between deciding to use a link and actually being carried
/// along one.
///
/// <b>Why this exists</b> (`docs/mods/concerned-foreman/LADDERS.md` decision L6):
/// the player's climb controller is its first implementation and a worker is its
/// second, so both are carried by the same four calls and neither knows anything
/// about the other. In particular <b>no method here takes an input</b> — the
/// nearest thing is <see cref="TraversalIntent"/>, which a player's controller
/// computes from keys and a worker's driver computes from its goal. A worker's
/// decision code therefore never touches player input handling, which is the
/// whole point of the seam.
///
/// <b>The name says ladder</b> because the spec names it and because a ladder is
/// the only kind of link that exists. Nothing in the contract is about rungs:
/// <see cref="TraversalLinkKind"/> is where a rope or a hatch would be added.
///
/// <b>The one rule.</b> Every path out of a traversal restores the body in the
/// same step, and only the two deliberate step-offs move it. An implementation
/// that can leave an actor without gravity, without a motor or stuck in a pose
/// is a defect whatever else it achieves.</summary>
internal interface ILadderTraversal
{
    /// <summary>May this actor use this link, and which way. Changes nothing.
    /// </summary>
    TraversalVerdict CanTraverse(in TraversalRequest request);

    /// <summary>Take hold of the link. Asks <see cref="CanTraverse"/> again
    /// itself: the answer may have changed since the caller looked, and an
    /// entry that trusts a stale verdict is an entry onto a ladder that has
    /// gone.</summary>
    TraversalEntry Enter(in TraversalRequest request);

    /// <summary>One step along. <paramref name="deltaSeconds"/> is the caller's
    /// own elapsed time; a step with no time in it moves nothing.</summary>
    TraversalProgress Traverse(in TraversalHandle handle, TraversalIntent intent, float deltaSeconds);

    /// <summary>Let go, for any reason at all, and say what the caller must put
    /// back. Calling it twice on the same handle is safe: the second call
    /// releases nothing and still answers.</summary>
    TraversalRelease Exit(in TraversalHandle handle, TraversalExitKind kind);
}
