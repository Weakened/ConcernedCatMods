using System;

namespace TheConcernedCat.Ladders;

/// <summary>One ladder piece, measured: where it starts, where it ends, which
/// way a climber faces on it, how wide it is and how far apart its rungs are.
///
/// The measurements come from the game (a piece's bounds and orientation) and
/// nothing here knows that. Feeding this type from the engine is the runtime's
/// job; deciding anything about a climb is this layer's.</summary>
internal readonly struct LadderGeometry
{
    private LadderGeometry(
        ClimbPoint bottom,
        ClimbPoint top,
        ClimbHeading standingSide,
        float width,
        float rungPitch)
    {
        Bottom = bottom;
        Top = top;
        StandingSide = standingSide;
        Width = width;
        RungPitch = rungPitch;
    }

    /// <summary>The foot of the ladder, on its centreline, at the height a
    /// climber's feet rest when standing on the ground at it.</summary>
    public ClimbPoint Bottom { get; }

    /// <summary>The head of the ladder, on its centreline. The climber's feet
    /// reach it; the platform edge is usually a little above.</summary>
    public ClimbPoint Top { get; }

    /// <summary>The horizontal direction that leads away from the rungs, into
    /// the open air where the climber's body hangs. A climber stands on this
    /// side and looks the opposite way, into the ladder.</summary>
    public ClimbHeading StandingSide { get; }

    /// <summary>How wide the rungs are. It sets how far off-centre a climber
    /// may start, so that approaching "roughly at the ladder" is enough.</summary>
    public float Width { get; }

    /// <summary>The distance between rungs, used to place hands and feet and to
    /// time the contact sound. Never zero: a ladder with unknown rungs gets the
    /// conventional spacing rather than a division by zero.</summary>
    public float RungPitch { get; }

    public float Height => Math.Max(0f, Top.Y - Bottom.Y);

    /// <summary>The direction a climber looks while on the ladder: into it.</summary>
    public ClimbHeading FacingWhileClimbing => StandingSide.Opposite();

    public bool IsClimbable => Height >= MinimumClimbableHeight && StandingSide.IsKnown && Width > 0f;

    /// <summary>Shorter than this and it is a step, not a ladder: a character
    /// walks up it, and mounting would be worse than walking.</summary>
    public const float MinimumClimbableHeight = 0.9f;

    /// <summary>What Valheim's own wooden ladder uses, near enough: the fallback
    /// when a piece's rungs cannot be measured.</summary>
    public const float ConventionalRungPitch = 0.35f;

    public static bool TryMeasure(
        ClimbPoint bottom,
        ClimbPoint top,
        ClimbHeading standingSide,
        float width,
        float rungPitch,
        out LadderGeometry geometry)
    {
        geometry = default;
        if (!bottom.IsFinite || !top.IsFinite || !standingSide.IsKnown)
        {
            return false;
        }

        if (float.IsNaN(width) || width <= 0f || width > 4f)
        {
            return false;
        }

        if (float.IsNaN(rungPitch) || rungPitch <= 0f || rungPitch > 1f)
        {
            rungPitch = ConventionalRungPitch;
        }

        if (top.Y - bottom.Y < MinimumClimbableHeight)
        {
            return false;
        }

        // A leaning ladder is still a ladder, but its centreline is the line
        // between the two ends, so a lean is carried rather than flattened.
        geometry = new LadderGeometry(bottom, top, standingSide, width, rungPitch);
        return true;
    }

    /// <summary>The point on the centreline at <paramref name="progress"/>
    /// metres of climb, clamped to the ladder.</summary>
    public ClimbPoint PointAt(float progress)
    {
        float span = Height;
        float clamped = span <= 0f ? 0f : Math.Min(Math.Max(progress, 0f), span);
        float fraction = span <= 0f ? 0f : clamped / span;
        return new ClimbPoint(
            Bottom.X + ((Top.X - Bottom.X) * fraction),
            Bottom.Y + ((Top.Y - Bottom.Y) * fraction),
            Bottom.Z + ((Top.Z - Bottom.Z) * fraction));
    }

    /// <summary>How far up the ladder a point is, measured as height above the
    /// foot and clamped to the ladder. Height decides it, because a climber's
    /// body hangs off the centreline by design.</summary>
    public float ProgressOf(ClimbPoint point)
    {
        float progress = point.Y - Bottom.Y;
        return Math.Min(Math.Max(progress, 0f), Height);
    }

    /// <summary>How far off the ladder's line a point stands, sideways. The
    /// distance along the standing side is not part of it: that is how far the
    /// climber is from the rungs, which <see cref="ReachFrom"/> answers.</summary>
    public float SidewaysOffsetOf(ClimbPoint point)
    {
        ClimbPoint onLine = PointAt(ProgressOf(point));
        float dx = point.X - onLine.X;
        float dz = point.Z - onLine.Z;
        float alongStandingSide = (dx * StandingSide.X) + (dz * StandingSide.Z);
        float sidewaysX = dx - (StandingSide.X * alongStandingSide);
        float sidewaysZ = dz - (StandingSide.Z * alongStandingSide);
        return (float)Math.Sqrt((sidewaysX * sidewaysX) + (sidewaysZ * sidewaysZ));
    }

    /// <summary>How far in front of the rungs a point is. Positive is out in
    /// the open air where a climber belongs; negative is behind the ladder,
    /// inside the wall it is fixed to.</summary>
    public float ReachFrom(ClimbPoint point)
    {
        ClimbPoint onLine = PointAt(ProgressOf(point));
        float dx = point.X - onLine.X;
        float dz = point.Z - onLine.Z;
        return (dx * StandingSide.X) + (dz * StandingSide.Z);
    }

    /// <summary>Where a climber's body sits at a given progress: on the
    /// centreline, held <paramref name="bodyOffset"/> out into the open air so
    /// the character is not inside the rungs.</summary>
    public ClimbPoint ClimbPositionAt(float progress, float bodyOffset) =>
        PointAt(progress).Offset(StandingSide, bodyOffset);

    /// <summary>The rung index at a progress, for hands, feet and the contact
    /// sound. Counting from the foot of the ladder.</summary>
    public int RungAt(float progress)
    {
        float pitch = RungPitch <= 0f ? ConventionalRungPitch : RungPitch;
        return (int)Math.Floor(Math.Min(Math.Max(progress, 0f), Height) / pitch);
    }

    public override string ToString() =>
        "ladder " + Bottom + " -> " + Top + " (" + Height.ToString("0.##") + " m, standing side " + StandingSide + ")";
}
