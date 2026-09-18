using System;
using System.Collections.Generic;

namespace TheConcernedCat.Ladders;

/// <summary>Ladder pieces stacked end to end, climbed as one run.
///
/// This is what makes the feature worth having: a player builds a tower and
/// stacks four ladder pieces up it, and the climb does not stop and restart
/// three times on the way up.</summary>
internal sealed class LadderRun
{
    private readonly List<LadderGeometry> _pieces;

    internal LadderRun(List<LadderGeometry> pieces)
    {
        _pieces = pieces;
        Bottom = pieces[0].Bottom;
        Top = pieces[pieces.Count - 1].Top;
    }

    public IReadOnlyList<LadderGeometry> Pieces => _pieces;

    public ClimbPoint Bottom { get; }

    public ClimbPoint Top { get; }

    public float Height => Math.Max(0f, Top.Y - Bottom.Y);

    /// <summary>The run's own geometry: the bottom of the lowest piece, the top
    /// of the highest, the standing side and width they all share, and the rung
    /// pitch of the piece a climber starts on.</summary>
    public LadderGeometry AsOne { get; private set; }

    /// <summary>Which piece holds the climber at this height. The runtime
    /// watches that piece for destruction and takes the rung pitch from it.</summary>
    public LadderGeometry PieceAt(float progress)
    {
        float height = Bottom.Y + Math.Min(Math.Max(progress, 0f), Height);
        for (int index = 0; index < _pieces.Count; index++)
        {
            LadderGeometry piece = _pieces[index];
            if (height <= piece.Top.Y + 0.01f)
            {
                return piece;
            }
        }

        return _pieces[_pieces.Count - 1];
    }

    /// <summary>Joins pieces that are genuinely one ladder: same standing side,
    /// same line, and touching end to end. Anything else stays its own run, so
    /// two ladders on opposite walls never become one climb.</summary>
    public static bool TryJoin(IReadOnlyList<LadderGeometry> pieces, out LadderRun run)
    {
        run = null!;
        if (pieces == null || pieces.Count == 0)
        {
            return false;
        }

        var ordered = new List<LadderGeometry>(pieces);
        ordered.Sort((left, right) => left.Bottom.Y.CompareTo(right.Bottom.Y));

        for (int index = 0; index < ordered.Count; index++)
        {
            if (!ordered[index].IsClimbable)
            {
                return false;
            }

            if (index == 0)
            {
                continue;
            }

            LadderGeometry below = ordered[index - 1];
            LadderGeometry above = ordered[index];

            // Facing the same way, within a few degrees: a ladder turned around
            // the corner is a different climb.
            if (below.StandingSide.Agreement(above.StandingSide) < 0.98f)
            {
                return false;
            }

            // On the same line: the next piece starts where this one ends,
            // sideways as well as in height.
            float gap = above.Bottom.Y - below.Top.Y;
            if (gap < -0.35f || gap > 0.35f)
            {
                return false;
            }

            if (below.Top.HorizontalDistanceTo(above.Bottom) > 0.35f)
            {
                return false;
            }
        }

        if (!LadderGeometry.TryMeasure(
                ordered[0].Bottom,
                ordered[ordered.Count - 1].Top,
                ordered[0].StandingSide,
                ordered[0].Width,
                ordered[0].RungPitch,
                out LadderGeometry whole))
        {
            return false;
        }

        run = new LadderRun(ordered) { AsOne = whole };
        return true;
    }

    /// <summary>One piece, on its own, is a run too. The common case, and it
    /// must not need the joining rules to spell it.</summary>
    public static LadderRun Single(in LadderGeometry piece)
    {
        if (!piece.IsClimbable)
        {
            throw new ArgumentOutOfRangeException(nameof(piece), "That piece is not climbable.");
        }

        return new LadderRun(new List<LadderGeometry> { piece }) { AsOne = piece };
    }
}
