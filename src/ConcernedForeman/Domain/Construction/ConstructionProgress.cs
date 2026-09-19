using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>What is actually standing where a piece was planned.
///
/// <b>Four answers, because two would lie.</b> "Built" and "not built" cannot
/// express a zone that has not streamed in, and an NPC that reads an unloaded
/// chunk as empty ground will happily place a second floor on top of the first.
/// <see cref="Unknown"/> is never a yes and never a no.</summary>
internal enum PieceSighting
{
    /// <summary>Nobody could tell - unloaded ground, an unreadable object, a
    /// question the adapter could not answer. Left for another round.</summary>
    Unknown = 0,

    /// <summary>Nothing is there. This piece is still to build.</summary>
    Missing = 1,

    /// <summary>The right piece is already there. Somebody built it - Thorstein
    /// earlier, or the player - and either way it is done.</summary>
    Standing = 2,

    /// <summary>Something else is in the way: another piece, a boulder, the
    /// player's own wall. Not buildable and not built, and it is the one case a
    /// player has to be told about, because it is the one they can fix.
    /// </summary>
    Blocked = 3,
}

/// <summary>Whether the piece a plan wants is already there.
///
/// <b>This is the role's completion condition</b>, and it is the only thing in
/// this product that decides what "done" means for a wall. It is asked before
/// every stop, not once per round: a round is long enough for the player to
/// build that wall themselves, and for the ground under it to unload.</summary>
internal interface IPieceSight
{
    /// <summary>What is standing at this placement.</summary>
    PieceSighting Look(in PiecePlacement placement);
}

/// <summary>How far along a shelter is, read from the world rather than
/// remembered.
///
/// <b>Nothing about progress is written to disk, deliberately.</b> The pieces
/// are real vanilla pieces in the world save, so the world already knows which
/// of them are standing; a second record of it in a Foreman file could only ever
/// be a copy that goes stale, and it would be a new durable format in a
/// programme whose acceptance criterion is that no durable format changes.
/// Reading it back means a reload resumes for free and a player who builds three
/// walls by hand is simply three walls further on.
///
/// <b>Phase order is a constraint, not a preference.</b> A roof panel with no
/// walls under it is refused by the game, so a piece whose earlier phases are
/// not finished is <i>deferred</i> - offered again next round - rather than
/// walked to and refused. That is what <see cref="MayBuildNow"/> answers, and it
/// is why a round that provisions the whole cottage at once still lays the floor
/// first: the material comes in one trip, the building still goes in
/// order.</summary>
internal sealed class ConstructionProgress
{
    private readonly ShelterPlan _plan;
    private readonly Dictionary<string, PieceSighting> _sightings =
        new Dictionary<string, PieceSighting>(StringComparer.Ordinal);

    private ConstructionProgress(ShelterPlan plan)
    {
        _plan = plan;
    }

    /// <summary>The plan this is progress against.</summary>
    internal ShelterPlan Plan => _plan;

    /// <summary>Whether every piece of the shelter is standing.</summary>
    internal bool IsComplete
    {
        get
        {
            foreach (CostedPiece piece in _plan.Pieces)
            {
                if (SightingOf(piece.Key) != PieceSighting.Standing)
                {
                    return false;
                }
            }

            return _plan.Pieces.Count > 0;
        }
    }

    /// <summary>Whether every piece was actually looked at. A progress report
    /// with an unknown in it may never be read as "finished" or as "nothing
    /// here".</summary>
    internal bool IsConclusive
    {
        get
        {
            foreach (CostedPiece piece in _plan.Pieces)
            {
                if (SightingOf(piece.Key) == PieceSighting.Unknown)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The phase being built: the earliest one with a piece not yet
    /// standing. <see cref="BuildPhase.Unspecified"/> when the shelter is
    /// finished.</summary>
    internal BuildPhase CurrentPhase
    {
        get
        {
            foreach (BuildPhase phase in BuildPhases.InOrder)
            {
                if (!IsPhaseStanding(phase))
                {
                    return phase;
                }
            }

            return BuildPhase.Unspecified;
        }
    }

    /// <summary>Every piece that is not standing, in build order. Blocked and
    /// unknown pieces are in here: neither is done.</summary>
    internal IReadOnlyList<CostedPiece> Remaining
    {
        get
        {
            var left = new List<CostedPiece>();
            foreach (CostedPiece piece in _plan.Pieces)
            {
                if (SightingOf(piece.Key) != PieceSighting.Standing)
                {
                    left.Add(piece);
                }
            }

            return left;
        }
    }

    /// <summary>The pieces something is in the way of, in build order.</summary>
    internal IReadOnlyList<CostedPiece> Blocked
    {
        get
        {
            var blocked = new List<CostedPiece>();
            foreach (CostedPiece piece in _plan.Pieces)
            {
                if (SightingOf(piece.Key) == PieceSighting.Blocked)
                {
                    blocked.Add(piece);
                }
            }

            return blocked;
        }
    }

    /// <summary>What the pieces that are not standing still cost, altogether.
    /// <b>The manifest the round is planned against</b> - never the whole
    /// shelter's, or a cottage half built by the player would be provisioned
    /// twice over.</summary>
    internal MaterialTally RemainingTotal
    {
        get
        {
            var tally = new MaterialTally();
            foreach (CostedPiece piece in Remaining)
            {
                tally.Add(piece.Recipe);
            }

            return tally;
        }
    }

    /// <summary>Looks at every planned placement once.</summary>
    internal static ConstructionProgress Read(ShelterPlan plan, IPieceSight? sight)
    {
        var progress = new ConstructionProgress(plan);
        if (!plan.IsPlanned)
        {
            return progress;
        }

        foreach (CostedPiece piece in plan.Pieces)
        {
            PiecePlacement placement = piece.Placement;
            PieceSighting sighting;
            try
            {
                sighting = sight == null ? PieceSighting.Unknown : sight.Look(in placement);
            }
            catch (Exception)
            {
                // A completion condition that throws must not end the build. It
                // has told us nothing, which is exactly what Unknown means, and
                // the piece comes back next round.
                sighting = PieceSighting.Unknown;
            }

            progress._sightings[piece.Key] = sighting;
        }

        return progress;
    }

    /// <summary>What was seen at one piece. <see cref="PieceSighting.Unknown"/>
    /// for a key nobody looked at, which is the safe answer in both
    /// directions.</summary>
    internal PieceSighting SightingOf(string? key) =>
        key != null && _sightings.TryGetValue(key, out PieceSighting sighting)
            ? sighting
            : PieceSighting.Unknown;

    /// <summary>Whether every piece of a phase is standing. An unknown counts as
    /// not standing, so an unfinished look never unlocks the next phase.
    /// </summary>
    internal bool IsPhaseStanding(BuildPhase phase)
    {
        foreach (CostedPiece piece in _plan.Pieces)
        {
            if (piece.Phase == phase && SightingOf(piece.Key) != PieceSighting.Standing)
            {
                return false;
            }
        }

        // A phase with no pieces in it is trivially standing, which is what lets
        // a later blueprint drop a phase without unlocking nothing after it.
        return true;
    }

    /// <summary>Whether this piece may be built right now: it is not already
    /// there, nothing is in the way of it, and every earlier phase is finished.
    /// </summary>
    internal bool MayBuildNow(CostedPiece piece)
    {
        PieceSighting sighting = SightingOf(piece.Key);
        if (sighting != PieceSighting.Missing)
        {
            return false;
        }

        foreach (BuildPhase phase in BuildPhases.InOrder)
        {
            if (phase == piece.Phase)
            {
                return true;
            }

            if (!IsPhaseStanding(phase))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The same question as <see cref="MayBuildNow"/>, as the word the
    /// route execution wants: build it, skip it because it is done, leave it for
    /// another round, or say something is in the way.</summary>
    internal PieceDisposition DispositionOf(CostedPiece piece)
    {
        switch (SightingOf(piece.Key))
        {
            case PieceSighting.Standing:
                return PieceDisposition.AlreadyBuilt;
            case PieceSighting.Blocked:
                return PieceDisposition.Blocked;
            case PieceSighting.Missing:
                return MayBuildNow(piece) ? PieceDisposition.Build : PieceDisposition.NotYet;
            default:
                return PieceDisposition.NotYet;
        }
    }

    public override string ToString() => _plan.IsPlanned
        ? Remaining.Count + " of " + _plan.Pieces.Count + " left, on " +
          BuildPhases.Describe(CurrentPhase)
        : "no plan";
}

/// <summary>What to do about one piece, right now.
///
/// These four map onto the shared runtime's own stop statuses at the seam, and
/// they are named for the role's meaning rather than the runtime's so that the
/// mapping is one visible table instead of role logic hidden inside an
/// enum.</summary>
internal enum PieceDisposition
{
    /// <summary>Not now, and not never: an earlier phase is unfinished or the
    /// ground could not be read. Offer it again next round.</summary>
    NotYet = 0,

    /// <summary>Go and build it.</summary>
    Build = 1,

    /// <summary>It is already there. Skip it and keep the material.</summary>
    AlreadyBuilt = 2,

    /// <summary>Something is in the way. Skip it, keep the material, and tell
    /// the player, because they are the only one who can move it.</summary>
    Blocked = 3,
}
