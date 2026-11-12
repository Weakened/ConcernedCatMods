using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>What one press did to the dig session (#551).</summary>
internal enum DigAction
{
    /// <summary>Nothing: no ground aimed at, or the press means nothing here.</summary>
    None = 0,

    /// <summary>A corner was set; the next stage began.</summary>
    Advanced = 1,

    /// <summary>One stage back (Secondary).</summary>
    SteppedBack = 2,

    /// <summary>The player asked to dig the rectangle and depth shown.</summary>
    Dig = 3,

    /// <summary>Secondary with nothing to step back from: the next Secondary within
    /// <see cref="DigSession.UndoConfirmSeconds"/> undoes the last dig.</summary>
    UndoArmed = 4,

    /// <summary>The second Secondary: undo the last dig.</summary>
    Undo = 5,
}

/// <summary>The shovel's dig flow (#551, owner playtest 2026-11-10: "the player defines the size, first the ground cover
/// and depth ... allow people to undo steps"), game-free so every press is tested:
/// <list type="number">
/// <item><b>First corner.</b> A 2 x 2 m footprint follows the aim. Attack sets that corner.</item>
/// <item><b>Second corner.</b> The rectangle stretches from the corner to the aim, 2 to 8 m a side. Attack sets it;
/// Secondary goes back.</item>
/// <item><b>Depth.</b> The wheel (or the controller's zoom buttons) sets 1 to 7 m. Attack digs; Secondary goes back.</item>
/// </list>
/// With nothing to go back to, Secondary arms undo, and a second Secondary within <see cref="UndoConfirmSeconds"/> undoes
/// the last dig. A careless double press can therefore undo only after the line has said what it will do. The
/// session only decides; it never reads the world or digs.</summary>
internal sealed class DigSession
{
    /// <summary>How long an armed undo waits for its confirming press (lead default).</summary>
    internal const float UndoConfirmSeconds = 3f;

    private float _undoArmedAt = float.NaN;

    internal DigStage Stage { get; private set; } = DigStage.FirstCorner;

    internal bool HasAim { get; private set; }

    internal int AimX { get; private set; }

    internal int AimZ { get; private set; }

    internal int AnchorX { get; private set; }

    internal int AnchorZ { get; private set; }

    internal int CornerX { get; private set; }

    internal int CornerZ { get; private set; }

    internal int Depth { get; private set; } = DigFootprint.DefaultDepth;

    /// <summary>True while an undo waits for its confirming press.</summary>
    internal bool UndoArmed(float now) => LadderRules.Finite(_undoArmedAt) && now - _undoArmedAt <= UndoConfirmSeconds && now >= _undoArmedAt;

    /// <summary>The rectangle shown now: a 2 x 2 m square at the aim, the rectangle to the aim, or the one set.</summary>
    internal DigFootprint Footprint => Stage switch
    {
        DigStage.FirstCorner => DigFootprint.FromCorners(AimX, AimZ, AimX + DigFootprint.MinimumSide, AimZ + DigFootprint.MinimumSide, Depth),
        DigStage.SecondCorner => DigFootprint.FromCorners(AnchorX, AnchorZ, AimX, AimZ, Depth),
        _ => DigFootprint.FromCorners(AnchorX, AnchorZ, CornerX, CornerZ, Depth),
    };

    /// <summary>Whether the shown rectangle follows the aim (it does until both corners are set).</summary>
    internal bool FollowsAim => Stage != DigStage.Depth;

    internal void Aim(int x, int z)
    {
        HasAim = true;
        AimX = x;
        AimZ = z;
    }

    internal void LoseAim() => HasAim = false;

    /// <summary>Attack: set the first corner, set the second, or ask to dig.</summary>
    internal DigAction Primary()
    {
        _undoArmedAt = float.NaN;
        switch (Stage)
        {
            case DigStage.FirstCorner:
                if (!HasAim) return DigAction.None;
                AnchorX = AimX;
                AnchorZ = AimZ;
                Stage = DigStage.SecondCorner;
                return DigAction.Advanced;
            case DigStage.SecondCorner:
                if (!HasAim) return DigAction.None;
                // The corner as clamped, so the rectangle set is the one shown.
                DigFootprint set = DigFootprint.FromCorners(AnchorX, AnchorZ, AimX, AimZ, Depth);
                CornerX = set.MinX == AnchorX ? set.MaxX : set.MinX;
                CornerZ = set.MinZ == AnchorZ ? set.MaxZ : set.MinZ;
                Stage = DigStage.Depth;
                return DigAction.Advanced;
            default:
                return DigAction.Dig;
        }
    }

    /// <summary>Secondary: one stage back, or arm or confirm undo.</summary>
    internal DigAction Secondary(float now)
    {
        if (Stage == DigStage.Depth) { Stage = DigStage.SecondCorner; _undoArmedAt = float.NaN; return DigAction.SteppedBack; }
        if (Stage == DigStage.SecondCorner) { Stage = DigStage.FirstCorner; _undoArmedAt = float.NaN; return DigAction.SteppedBack; }
        if (UndoArmed(now))
        {
            _undoArmedAt = float.NaN;
            return DigAction.Undo;
        }

        _undoArmedAt = LadderRules.Finite(now) ? now : float.NaN;
        return LadderRules.Finite(now) ? DigAction.UndoArmed : DigAction.None;
    }

    /// <summary>The wheel: <paramref name="steps"/> metres deeper (positive) or shallower, in the depth stage only.
    /// Returns whether the depth changed.</summary>
    internal bool ChangeDepth(int steps)
    {
        if (Stage != DigStage.Depth || steps == 0) return false;
        int depth = DigFootprint.ClampDepth(Depth + steps);
        if (depth == Depth) return false;
        Depth = depth;
        return true;
    }

    /// <summary>A dig was committed: start the next hole, keeping the depth.</summary>
    internal void Dug()
    {
        Stage = DigStage.FirstCorner;
        _undoArmedAt = float.NaN;
    }

    /// <summary>The shovel was put away or a screen opened: start over, keeping the depth.</summary>
    internal void Reset()
    {
        Stage = DigStage.FirstCorner;
        HasAim = false;
        _undoArmedAt = float.NaN;
    }
}
