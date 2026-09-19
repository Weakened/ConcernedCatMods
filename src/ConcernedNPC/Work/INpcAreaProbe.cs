using System;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>Why a sampled point will not do. Flags, because a point is often
/// several kinds of wrong at once and a player asking "why not there" deserves
/// all of them rather than whichever was checked first.</summary>
[Flags]
public enum AreaRejection
{
    None = 0,

    /// <summary>Under or in water deeper than an NPC will stand in.</summary>
    Water = 1 << 0,

    /// <summary>Nothing solid to stand on within the search band.</summary>
    Unsupported = 1 << 1,

    /// <summary>The ground is there and too steep to stand on.</summary>
    TooSteep = 1 << 2,

    /// <summary>Something is already in the way - a piece, a creature, another
    /// NPC.</summary>
    Occupied = 1 << 3,

    /// <summary>Standing here would hurt: fire, lava, a burning effect area.
    /// </summary>
    Hazard = 1 << 4,

    /// <summary>The point is outside the work area it was sampled for. Kept as
    /// a rejection rather than filtered out silently, so a scan that keeps
    /// proposing points outside its own area is visible instead of merely
    /// slow.</summary>
    OutsideArea = 1 << 5,
}

/// <summary>What one sampled point turned out to be.</summary>
public enum AreaSampleVerdict
{
    /// <summary>The probe could not answer. Never a yes.</summary>
    Unreadable = 0,

    /// <summary>An NPC can stand here. <see cref="AreaSample.Ground"/> is where.
    /// </summary>
    Standable = 1,

    /// <summary>The ground here is not loaded. <b>Defer, do not reject.</b> A
    /// scan that counts unloaded points as unsuitable reports an area empty
    /// because the player walked away from it, and the NPC then declares a job
    /// finished it never began. Asked again once the zone streams in, the same
    /// point may be perfectly good.</summary>
    NotLoaded = 2,

    /// <summary>Loaded, readable, and not somewhere an NPC may stand.
    /// <see cref="AreaSample.Rejection"/> says why, in as many ways as
    /// apply.</summary>
    Rejected = 3,
}

/// <summary>One point, probed.</summary>
public readonly struct AreaSample
{
    public AreaSample(AreaSampleVerdict verdict, NpcPoint ground, AreaRejection rejection)
    {
        Verdict = verdict;
        Ground = ground;
        Rejection = rejection;
    }

    /// <summary>What the point turned out to be.</summary>
    public AreaSampleVerdict Verdict { get; }

    /// <summary>Where the ground actually is beneath or above the point asked
    /// about - the probe searches a band, so the answer is rarely the question.
    /// Only meaningful for <see cref="AreaSampleVerdict.Standable"/>.</summary>
    public NpcPoint Ground { get; }

    /// <summary>Every reason this point was refused, or
    /// <see cref="AreaRejection.None"/>.</summary>
    public AreaRejection Rejection { get; }

    /// <summary>The one question a caller asks before walking somewhere.</summary>
    public bool IsStandable => Verdict == AreaSampleVerdict.Standable;
}

/// <summary>Asking the world about one point: is there ground, is it loaded, may
/// an NPC stand on it.
///
/// <b>What it guarantees.</b> That this library never touches the game to find
/// out. Every implementation is a role's adapter or a test's fake, and the
/// planners above it are therefore provable without the game installed - which
/// matters here more than usual, because the products reference licensed
/// assemblies no test runner has.
///
/// <b>What it guarantees about honesty.</b> That "I could not tell" and "no" are
/// different answers (<see cref="AreaSampleVerdict.Unreadable"/> and
/// <see cref="AreaSampleVerdict.NotLoaded"/> against
/// <see cref="AreaSampleVerdict.Rejected"/>), and that an implementation which
/// cannot see the ground says so rather than guessing either way. Guessing yes
/// walks an NPC into water; guessing no makes an area look empty.
///
/// <b>Why per point and not per area.</b> Because that is the only shape the
/// world can honestly answer. Nothing in the shipped products sweeps an area for
/// standable ground; they ask "is this zone loaded" for a whole area and "may I
/// stand here" for one point at a time. The one search that does exist - the
/// companion placement ring - works by proposing a short, deterministic,
/// best-first list of candidates and letting a probe refuse each one. That is
/// the idiom every planner in this library follows: propose in order, let the
/// world say no, fall through.
///
/// <b>Cost.</b> A probe may be expensive - a raycast, a navmesh query, a
/// physics overlap - so callers sample a bounded number of candidates per tick
/// and remember refusals. Nothing here loops until it finds one.</summary>
public interface INpcAreaProbe
{
    /// <summary>Probes one point. Never throws for a world reason: a probe that
    /// cannot answer returns <see cref="AreaSampleVerdict.Unreadable"/>, because
    /// an exception out of a sampling loop is a fault the driver above it does
    /// not catch.</summary>
    AreaSample Probe(NpcPoint point);
}
