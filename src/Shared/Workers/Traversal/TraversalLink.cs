using System;
using System.Globalization;

namespace TheConcernedCat.Workers.Traversal;

/// <summary>What sort of thing a link is. Zero is unspecified, so a link nobody
/// described is never mistaken for a usable one.</summary>
internal enum TraversalLinkKind
{
    Unspecified = 0,

    /// <summary>A ladder run: one piece, or several stacked end to end and
    /// climbed as one (`docs/mods/concerned-foreman/LADDERS.md` §5).</summary>
    Ladder = 1,
}

/// <summary>Which way along a link an actor is going.</summary>
internal enum TraversalDirection
{
    Unspecified = 0,

    /// <summary>From the bottom endpoint to the top one.</summary>
    Up = 1,

    /// <summary>From the top endpoint to the bottom one.</summary>
    Down = 2,
}

/// <summary>The numbers traversal obeys, in one place, with the reasons.
///
/// Deliberately <b>not</b> the ladder domain's <c>ClimbLimits</c>: that one
/// tunes a body on a ladder and belongs to the product that owns ladders; this
/// one tunes a <i>walker's decision</i> about a link and belongs to every
/// product that has workers. The two numbers they share (climb speed, the
/// shortest thing worth climbing) are passed in by the adapter that knows both,
/// rather than making one shared area depend on the other.</summary>
internal sealed class TraversalLimits
{
    /// <summary>How close to an endpoint counts as standing at it. Wider than a
    /// walk goal's arrival tolerance would suggest, because a walker arriving at
    /// the foot of a ladder stops wherever the last path node put it.</summary>
    public float ApproachRadiusMetres { get; set; } = 1.5f;

    /// <summary>Shorter than this and it is a step, not a link: a body walks up
    /// it, and taking a link would be slower than walking. Mirrors the ladder
    /// domain's minimum climbable height; the adapter passes the real one.
    /// </summary>
    public float MinHeightMetres { get; set; } = 0.9f;

    /// <summary>The tallest link a <b>worker</b> may commit to.
    ///
    /// <b>Provisional.</b> The honest position is that nobody has yet watched a
    /// worker climb anything, so this number is not tuned — it exists so that
    /// the refusal path is real code with a test behind it rather than a
    /// promise, and the NPC gate (G5) is what sets it. A player is not subject
    /// to it: a person climbing their own tower is their business.</summary>
    public float MaxWorkerHeightMetres { get; set; } = 12f;

    /// <summary>Metres per second of traversal. Mirrors the ladder domain's
    /// tuned climb speed.</summary>
    public float TraversalSpeedMetresPerSecond { get; set; } = 1.8f;

    /// <summary>Metres per second on the flat, for comparing a climb with a
    /// walk. Roughly vanilla's walking pace.</summary>
    public float WalkSpeedMetresPerSecond { get; set; } = 3f;

    /// <summary>The fixed price of getting on and off a link, in metres of
    /// walking. Without it a one-metre link would beat a two-metre detour and a
    /// walker would spend its day mounting and dismounting ladders.</summary>
    public float LinkOverheadMetres { get; set; } = 4f;

    /// <summary>How far a walker will look for a link, on the ground plane.
    /// Mirrors the settlement planner's sixty-four metre planning horizon: a
    /// link beyond it is no use, because the walk to it would be refused
    /// anyway. (Named rather than referenced: the settlement layer is a
    /// different shared area and products adopt the two independently.)
    /// </summary>
    public float MaxSearchMetres { get; set; } = 64f;

    /// <summary>What a metre of this link costs in metres of walking.</summary>
    public float CostPerMetre =>
        TraversalSpeedMetresPerSecond <= 0f
            ? float.PositiveInfinity
            : Math.Max(1f, WalkSpeedMetresPerSecond / TraversalSpeedMetresPerSecond);

    public TraversalLimits Validate()
    {
        Require(ApproachRadiusMetres > 0f && ApproachRadiusMetres <= 8f, nameof(ApproachRadiusMetres));
        Require(MinHeightMetres > 0f && MinHeightMetres <= 4f, nameof(MinHeightMetres));
        Require(
            MaxWorkerHeightMetres > MinHeightMetres && MaxWorkerHeightMetres <= 128f,
            nameof(MaxWorkerHeightMetres));
        Require(
            TraversalSpeedMetresPerSecond > 0f && TraversalSpeedMetresPerSecond <= 10f,
            nameof(TraversalSpeedMetresPerSecond));
        Require(WalkSpeedMetresPerSecond > 0f && WalkSpeedMetresPerSecond <= 20f, nameof(WalkSpeedMetresPerSecond));
        Require(LinkOverheadMetres >= 0f && LinkOverheadMetres <= 64f, nameof(LinkOverheadMetres));
        Require(MaxSearchMetres > 0f && MaxSearchMetres <= 512f, nameof(MaxSearchMetres));
        return this;
    }

    public static TraversalLimits Default => new TraversalLimits();

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, "A traversal limit is outside its designed range.");
        }
    }
}

/// <summary>One published way of getting between two heights that walking cannot
/// reach: a ladder run's two ends, and everything a walker needs to decide
/// whether the link is worth taking.
///
/// <b>The endpoints are standing points, not the ladder.</b> <see cref="Bottom"/>
/// is where a body stands on the ground at the foot, and <see cref="Top"/> is
/// where it stands on the floor at the head. That is deliberately the walker's
/// vocabulary: a path that ends at <see cref="Bottom"/> continues from
/// <see cref="Top"/>, and nothing in the planner has to know what a rung is. The
/// geometry of the thing between them stays in the product that owns it.
///
/// <b>Id.</b> Stable for as long as the link is published, and no longer. It is
/// the adapter's to mint; it must not be anything the game renumbers across a
/// world load (a ZDO id is renumbered every time, which is why worker identity
/// never uses one either).</summary>
internal readonly struct TraversalLink : IEquatable<TraversalLink>
{
    private TraversalLink(
        string id,
        TraversalLinkKind kind,
        WorkPoint bottom,
        WorkPoint top,
        TraversalCapability requires)
    {
        Id = id;
        Kind = kind;
        Bottom = bottom;
        Top = top;
        Requires = requires;
    }

    public string Id { get; }

    public TraversalLinkKind Kind { get; }

    /// <summary>Where a body stands at the foot, ready to go up.</summary>
    public WorkPoint Bottom { get; }

    /// <summary>Where a body stands at the head, having come over the top.
    /// </summary>
    public WorkPoint Top { get; }

    /// <summary>What an actor must be able to do to use this link.</summary>
    public TraversalCapability Requires { get; }

    /// <summary>How much height the link gains. The reason to take it.</summary>
    public float Height => Top.Y - Bottom.Y;

    public bool IsEmpty => string.IsNullOrEmpty(Id) || Kind == TraversalLinkKind.Unspecified;

    /// <summary>Builds a link, or refuses. It refuses rather than throwing
    /// because its inputs are measurements of a world that may have moved,
    /// collapsed or never been what the adapter thought.</summary>
    public static bool TryCreate(
        string? id,
        TraversalLinkKind kind,
        WorkPoint bottom,
        WorkPoint top,
        TraversalCapability requires,
        TraversalLimits limits,
        out TraversalLink link)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        link = default;
        if (string.IsNullOrEmpty(id) || kind == TraversalLinkKind.Unspecified)
        {
            return false;
        }

        if (requires == TraversalCapability.None)
        {
            // A link nothing is needed for is a link nothing is gated on, and
            // that is a walk. Refusing keeps the capability check meaningful.
            return false;
        }

        if (!bottom.IsFinite || !top.IsFinite)
        {
            return false;
        }

        float height = top.Y - bottom.Y;
        if (float.IsNaN(height) || height < limits.MinHeightMetres)
        {
            return false;
        }

        link = new TraversalLink(id!, kind, bottom, top, requires);
        return true;
    }

    /// <summary>The endpoint an actor going <paramref name="direction"/> starts
    /// from.</summary>
    public WorkPoint EntryFor(TraversalDirection direction) =>
        direction == TraversalDirection.Down ? Top : Bottom;

    /// <summary>The endpoint it arrives at. A path that ends at
    /// <see cref="EntryFor"/> continues from here.</summary>
    public WorkPoint ExitFor(TraversalDirection direction) =>
        direction == TraversalDirection.Down ? Bottom : Top;

    /// <summary>Which way this link would take a body standing here, or
    /// <see cref="TraversalDirection.Unspecified"/> when it is at neither end.
    /// Height is part of the question: a body on the floor above is at the top
    /// even when it is directly over the foot of the ladder.</summary>
    public TraversalDirection DirectionFrom(WorkPoint standing, TraversalLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (!standing.IsFinite)
        {
            return TraversalDirection.Unspecified;
        }

        float radius = limits.ApproachRadiusMetres;
        bool atBottom = standing.HorizontalDistanceTo(Bottom) <= radius &&
            standing.VerticalDistanceTo(Bottom) <= radius;
        bool atTop = standing.HorizontalDistanceTo(Top) <= radius &&
            standing.VerticalDistanceTo(Top) <= radius;

        if (atBottom && atTop)
        {
            // Only a degenerate link can be both, and TryCreate refuses those.
            // Answering Unspecified rather than guessing keeps a broken
            // measurement from sending a body somewhere arbitrary.
            return TraversalDirection.Unspecified;
        }

        if (atBottom)
        {
            return TraversalDirection.Up;
        }

        return atTop ? TraversalDirection.Down : TraversalDirection.Unspecified;
    }

    /// <summary>How long the traversal takes, in seconds.</summary>
    public float SecondsToTraverse(TraversalLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        float speed = limits.TraversalSpeedMetresPerSecond;
        return speed <= 0f ? float.PositiveInfinity : Math.Max(0f, Height) / speed;
    }

    /// <summary>What taking this link costs, expressed in metres of walking, so
    /// a planner can compare it against the detour it replaces without knowing
    /// anything about ladders. Includes the fixed cost of getting on and off.
    /// </summary>
    public float EquivalentWalkMetres(TraversalLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        return limits.LinkOverheadMetres + (Math.Max(0f, Height) * limits.CostPerMetre);
    }

    /// <summary>A straight-line estimate of where a body is part way along, for
    /// a caller that has nothing better. <b>It is an estimate</b>: the real path
    /// of a climbing body hangs off the rungs and is the climb controller's to
    /// compute, not this layer's.</summary>
    public WorkPoint EstimatedPositionAt(TraversalDirection direction, float fraction)
    {
        float clamped = float.IsNaN(fraction) ? 0f : Math.Min(Math.Max(fraction, 0f), 1f);
        WorkPoint from = EntryFor(direction);
        WorkPoint to = ExitFor(direction);
        return new WorkPoint(
            from.X + ((to.X - from.X) * clamped),
            from.Y + ((to.Y - from.Y) * clamped),
            from.Z + ((to.Z - from.Z) * clamped));
    }

    public bool Equals(TraversalLink other) =>
        string.Equals(Id, other.Id, StringComparison.Ordinal) &&
        Kind == other.Kind &&
        Bottom.Equals(other.Bottom) &&
        Top.Equals(other.Top) &&
        Requires == other.Requires;

    public override bool Equals(object? obj) => obj is TraversalLink other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Id == null ? 0 : StringComparer.Ordinal.GetHashCode(Id);
            hash = (hash * 31) + (int)Kind;
            hash = (hash * 31) + Bottom.GetHashCode();
            hash = (hash * 31) + Top.GetHashCode();
            return (hash * 31) + (int)Requires;
        }
    }

    public override string ToString() =>
        IsEmpty
            ? "<no link>"
            : Kind + " '" + Id + "' " + Bottom + " -> " + Top + " (" +
                Height.ToString("0.##", CultureInfo.InvariantCulture) + " m)";
}
