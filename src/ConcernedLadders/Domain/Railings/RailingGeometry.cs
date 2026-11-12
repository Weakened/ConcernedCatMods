using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain.Families;

namespace TheConcernedCat.ConcernedLadders.Domain.Railings;

/// <summary>The shape of a railing piece (#556). Measured from the installed game's stair pieces (Valheim 1.0.21, their
/// snap points, read by <c>cl-ladders-devtools/doors/stairs.py</c>): every straight stair - <c>wood_stair</c>,
/// <c>stone_stair</c>, <c>ashwood_stair</c>, the black marble and grausten stairs, <c>dvergrprops_wood_stair</c> - rises
/// 1 m over 2 m (26.57°), <c>wood_stepladder</c> rises 2 m over 2 m (45°), and the Dvergr spiral stairs
/// (<c>piece_dvergr_spiralstair</c>, <c>_right</c>) rise 1 m over a quarter turn with their outer edge 1.75 m from their
/// centre. The owner asked for "33 and 45 degrees"; the railings follow the stairs the game has.</summary>
internal enum RailingAngle
{
    /// <summary>A level railing for floors, landings and the top of a stairwell: 2 m long.</summary>
    Level = 0,

    /// <summary>The straight stairs: 1 m up over 2 m.</summary>
    Stair = 1,

    /// <summary>The step ladder: 2 m up over 2 m.</summary>
    Steep = 2,

    /// <summary>The outer edge of <c>piece_dvergr_spiralstair</c>: a quarter turn to the left, 1 m up.</summary>
    SpiralLeft = 3,

    /// <summary>The outer edge of <c>piece_dvergr_spiralstair_right</c>: the left one mirrored.</summary>
    SpiralRight = 4,

    /// <summary>A newel post on its own, for corners and the ends of a run.</summary>
    Newel = 5,
}

/// <summary>A railing's geometry contract (#556), in the piece's own space: x across, y up, z along the railing from its
/// low end. The base line runs from the bottom snap (the origin) to the top snap, so a railing whose bottom snap meets a
/// stair's bottom side snap, turned to run up the stair, has its top snap on the stair's top side snap. The posts, the
/// handrail and the bottom rail all follow that line, so every height above the stair is the same along it. Lead
/// defaults; in game, OWNER GO-AROUND PENDING.</summary>
internal static class RailingGeometry
{
    /// <summary>Every straight railing is 2 m along the ground (the floor grid and both stair runs).</summary>
    internal const float Run = 2f;

    /// <summary>The Dvergr spiral stair's outer edge, from its centre (its outer snap points).</summary>
    internal const float SpiralRadius = 1.75f;

    /// <summary>The spiral stair's rise over its quarter turn.</summary>
    internal const float SpiralRise = 1f;

    internal const float HandrailHeight = 0.95f;

    internal const float BottomRailHeight = 0.1f;

    internal const float PostHeight = 1.05f;

    /// <summary>A newel post on its own stands a little taller than the posts at a railing's ends.</summary>
    internal const float NewelHeight = 1.2f;

    internal const float ColliderThickness = 0.12f;

    /// <summary>A spiral railing's handrail, bottom rail and collider follow its curve in this many straight pieces.</summary>
    internal const int SpiralSegments = 12;

    internal static bool IsSpiral(RailingAngle angle) => angle == RailingAngle.SpiralLeft || angle == RailingAngle.SpiralRight;

    internal static bool IsStraight(RailingAngle angle) => angle == RailingAngle.Level || angle == RailingAngle.Stair || angle == RailingAngle.Steep;

    internal static float Rise(RailingAngle angle) => angle switch
    {
        RailingAngle.Level => 0f,
        RailingAngle.Stair => 1f,
        RailingAngle.Steep => 2f,
        RailingAngle.SpiralLeft or RailingAngle.SpiralRight => SpiralRise,
        RailingAngle.Newel => 0f,
        _ => throw new ArgumentOutOfRangeException(nameof(angle)),
    };

    internal static float Slope(RailingAngle angle) => IsStraight(angle) ? Rise(angle) / Run : 0f;

    internal static float Degrees(RailingAngle angle) => IsStraight(angle) ? (float)(Math.Atan2(Rise(angle), Run) * 180.0 / Math.PI) : 0f;

    /// <summary>A straight railing's base line length along the slope.</summary>
    internal static float Length(RailingAngle angle) => (float)Math.Sqrt(Run * Run + Rise(angle) * Rise(angle));

    /// <summary>A straight railing's base line height at <paramref name="z"/>.</summary>
    internal static float BaseAt(RailingAngle angle, float z) => Slope(angle) * z;

    /// <summary>The base line at <paramref name="t"/> from its bottom (0) to its top (1). A spiral turns a quarter round
    /// the stair's centre, which lies <see cref="SpiralRadius"/> behind the bottom snap (-z): to the left (-x) for the left
    /// stair, to the right for the right one.</summary>
    internal static Point3 Base(RailingAngle angle, float t)
    {
        if (IsStraight(angle)) return new Point3(0f, Rise(angle) * t, Run * t);
        if (!IsSpiral(angle)) return new Point3(0f, 0f, 0f);
        double theta = Math.PI / 2 * (1 + t);
        float side = angle == RailingAngle.SpiralLeft ? 1f : -1f;
        return new Point3(side * (float)(SpiralRadius * Math.Cos(theta)), SpiralRise * t, (float)(SpiralRadius * Math.Sin(theta)) - SpiralRadius);
    }

    /// <summary>The base line's length: the slope for a straight railing, the helix for a spiral.</summary>
    internal static float PathLength(RailingAngle angle)
    {
        if (IsStraight(angle)) return Length(angle);
        if (!IsSpiral(angle)) return 0f;
        float arc = (float)(Math.PI / 2 * SpiralRadius);
        return (float)Math.Sqrt(arc * arc + SpiralRise * SpiralRise);
    }

    /// <summary>The base line's length in plan (what the balusters are spaced along).</summary>
    internal static float PlanLength(RailingAngle angle) =>
        IsStraight(angle) ? Run : IsSpiral(angle) ? (float)(Math.PI / 2 * SpiralRadius) : 0f;

    internal static Point3 BottomSnap => new Point3(0f, 0f, 0f);

    internal static Point3 TopSnap(RailingAngle angle) => Base(angle, 1f);

    /// <summary>The stair a railing is made for, by its measured side (or outer) snap points: bottom and top.</summary>
    internal static (Point3 Bottom, Point3 Top) StairSideSnaps(RailingAngle angle) => angle switch
    {
        RailingAngle.Stair => (new Point3(1f, 0f, 1f), new Point3(1f, 1f, -1f)),
        RailingAngle.Steep => (new Point3(0.5f, 0f, 1f), new Point3(0.5f, 2f, -1f)),
        RailingAngle.SpiralLeft => (new Point3(0f, 0f, 1.75f), new Point3(-1.75f, 1f, 0f)),
        RailingAngle.SpiralRight => (new Point3(0f, 0f, 1.75f), new Point3(1.75f, 1f, 0f)),
        _ => (new Point3(1f, 0f, 1f), new Point3(1f, 0f, -1f)),
    };

    /// <summary>The colliders: thin boxes from just above the base line to the handrail, one along a straight railing,
    /// one per chord of a spiral, one round a newel. Each in its own frame: turned by <see cref="RailingCollider.Yaw"/>
    /// about y, then <see cref="RailingCollider.Pitch"/> about x (Unity's <c>Quaternion.Euler(pitch, yaw, 0)</c>).</summary>
    internal static IReadOnlyList<RailingCollider> Colliders(RailingAngle angle)
    {
        const float bottom = 0.05f;
        if (angle == RailingAngle.Newel)
            return new[] { new RailingCollider(new Point3(0f, NewelHeight / 2f, 0f), new Point3(0.16f, NewelHeight, 0.16f), 0f, 0f, new Point3(0f, 0f, 0f)) };
        int segments = IsSpiral(angle) ? SpiralSegments : 1;
        var colliders = new List<RailingCollider>(segments);
        for (int index = 0; index < segments; index++)
        {
            Point3 a = Base(angle, index / (float)segments), b = Base(angle, (index + 1) / (float)segments);
            Point3 chord = b - a;
            float plan = (float)Math.Sqrt(chord.X * chord.X + chord.Z * chord.Z);
            float length = chord.Length;
            float yaw = (float)(Math.Atan2(chord.X, chord.Z) * 180.0 / Math.PI);
            float pitch = -(float)(Math.Atan2(chord.Y, plan) * 180.0 / Math.PI);
            float cos = plan / length;
            // In the chord's frame z runs along it and y is square to it: the box spans the parallelogram between the
            // base line and the handrail, its height measured square to the slope.
            colliders.Add(new RailingCollider(new Point3(0f, (bottom + HandrailHeight) * 0.5f * cos, length * 0.5f),
                new Point3(ColliderThickness, (HandrailHeight - bottom) * cos, length), yaw, pitch, a));
        }

        return colliders;
    }
}

/// <summary>One collider box: <see cref="Centre"/> and <see cref="Size"/> in a frame at <see cref="Origin"/> turned by
/// <see cref="Yaw"/> then <see cref="Pitch"/> degrees.</summary>
internal readonly struct RailingCollider
{
    internal RailingCollider(Point3 centre, Point3 size, float yaw, float pitch, Point3 origin)
    {
        Centre = centre;
        Size = size;
        Yaw = yaw;
        Pitch = pitch;
        Origin = origin;
    }

    internal Point3 Centre { get; }

    internal Point3 Size { get; }

    internal float Yaw { get; }

    internal float Pitch { get; }

    internal Point3 Origin { get; }
}

/// <summary>A point in a railing's or a part's space.</summary>
internal readonly struct Point3
{
    internal Point3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal float X { get; }

    internal float Y { get; }

    internal float Z { get; }

    public static Point3 operator +(Point3 a, Point3 b) => new Point3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Point3 operator -(Point3 a, Point3 b) => new Point3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Point3 operator *(Point3 a, float s) => new Point3(a.X * s, a.Y * s, a.Z * s);

    internal float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

    internal Point3 Normalized()
    {
        float length = Length;
        return length > 0f ? new Point3(X / length, Y / length, Z / length) : this;
    }

    internal static Point3 Cross(Point3 a, Point3 b) => new Point3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    internal static float Dot(Point3 a, Point3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

/// <summary>What kind of part a railing piece is made of, for the tests and the record.</summary>
internal enum RailingPartKind
{
    Post = 0,
    PostCap = 1,
    Handrail = 2,
    BottomRail = 3,
    Baluster = 4,
    Knuckle = 5,
    Scroll = 6,
    Finial = 7,
}

/// <summary>One part of a railing: a straight bar from <see cref="From"/> to <see cref="To"/> of square cross-section
/// <see cref="Width"/>, twisted by <see cref="TwistDegrees"/> over its length (0 for a plain bar), or a small solid
/// (<see cref="RailingPartKind.Knuckle"/>, <see cref="RailingPartKind.Finial"/>) of <see cref="Width"/> centred at
/// <see cref="From"/>.</summary>
internal readonly struct RailingPart
{
    internal RailingPart(RailingPartKind kind, Point3 from, Point3 to, float width, float twistDegrees = 0f)
    {
        Kind = kind;
        From = from;
        To = to;
        Width = width;
        TwistDegrees = twistDegrees;
    }

    internal RailingPartKind Kind { get; }

    internal Point3 From { get; }

    internal Point3 To { get; }

    internal float Width { get; }

    internal float TwistDegrees { get; }
}

/// <summary>What each style of railing is made of (#556; owner references 2026-11-10): plain - square posts with a cap, a
/// handrail and square balusters (wood and the vanilla woods); decorated - twisted balusters with a knuckle half way up and
/// capped posts (copper); detailed - thinner twisted bars, a pair of scrolls between every few bars on straight railings,
/// a bottom rail and finials on the posts (iron, flametal). Game-free so every style's parts are tested.</summary>
internal static class RailingDesign
{
    /// <summary>The widest gap between balusters (centre to centre), along the ground.</summary>
    internal const float BalusterPitch = 0.16f;

    internal static float PostWidth(RailingStyle style) => style == RailingStyle.Plain ? 0.11f : style == RailingStyle.Decorated ? 0.08f : 0.07f;

    internal static float HandrailWidth(RailingStyle style) => style == RailingStyle.Plain ? 0.08f : 0.06f;

    internal static float BalusterWidth(RailingStyle style) => style == RailingStyle.Plain ? 0.045f : style == RailingStyle.Decorated ? 0.03f : 0.022f;

    /// <summary>Twist of one baluster over its length: none plain, a turn and a half decorated, three turns detailed.</summary>
    internal static float Twist(RailingStyle style) => style == RailingStyle.Plain ? 0f : style == RailingStyle.Decorated ? 540f : 1080f;

    /// <summary>Detailed straight railings put a pair of scrolls in place of every this many balusters.</summary>
    internal const int IronScrollEvery = 4;

    /// <summary>Every part of a railing piece, in the piece's space.</summary>
    internal static IReadOnlyList<RailingPart> Parts(RailingStyle style, RailingAngle angle)
    {
        var parts = new List<RailingPart>();
        float post = PostWidth(style), rail = HandrailWidth(style), bar = BalusterWidth(style);
        if (angle == RailingAngle.Newel)
        {
            AddPost(parts, style, new Point3(0f, 0f, 0f), RailingGeometry.NewelHeight, post * 1.3f);
            return parts;
        }

        AddPost(parts, style, RailingGeometry.Base(angle, 0f), RailingGeometry.PostHeight, post);
        AddPost(parts, style, RailingGeometry.Base(angle, 1f), RailingGeometry.PostHeight, post);

        // The handrail and the bottom rail between the posts' inner faces, following the base line.
        float plan = RailingGeometry.PlanLength(angle);
        float inset = post * 0.5f / plan;
        int segments = RailingGeometry.IsSpiral(angle) ? RailingGeometry.SpiralSegments : 1;
        float railCentre = RailingGeometry.HandrailHeight - rail * 0.5f;
        bool bottomRail = style != RailingStyle.Plain || angle == RailingAngle.Level;
        for (int index = 0; index < segments; index++)
        {
            float t0 = inset + (1f - 2f * inset) * index / segments, t1 = inset + (1f - 2f * inset) * (index + 1) / segments;
            parts.Add(new RailingPart(RailingPartKind.Handrail, Above(angle, t0, railCentre), Above(angle, t1, railCentre), rail));
            if (bottomRail)
                parts.Add(new RailingPart(RailingPartKind.BottomRail, Above(angle, t0, RailingGeometry.BottomRailHeight), Above(angle, t1, RailingGeometry.BottomRailHeight), bar * 1.4f));
        }

        // The balusters: evenly spaced along the ground between the posts, vertical, up to the handrail.
        float clear = plan * (1f - 2f * inset);
        int count = Math.Max(1, (int)Math.Ceiling(clear / BalusterPitch) - 1);
        float bottomAbove = bottomRail ? RailingGeometry.BottomRailHeight : 0f;
        float topAbove = RailingGeometry.HandrailHeight - rail;
        float pitch = clear / (count + 1);
        for (int index = 1; index <= count; index++)
        {
            float t = inset + (1f - 2f * inset) * index / (count + 1);
            Point3 low = Above(angle, t, bottomAbove), high = Above(angle, t, topAbove);
            if (style == RailingStyle.Detailed && RailingGeometry.IsStraight(angle) && index % IronScrollEvery == 0)
            {
                AddScrollPair(parts, low, high, pitch, bar);
                continue;
            }

            parts.Add(new RailingPart(RailingPartKind.Baluster, low, high, bar, Twist(style)));
            if (style == RailingStyle.Decorated)
            {
                Point3 middle = (low + high) * 0.5f;
                parts.Add(new RailingPart(RailingPartKind.Knuckle, middle, middle, bar * 2.6f));
            }
        }

        return parts;
    }

    private static Point3 Above(RailingAngle angle, float t, float height) => RailingGeometry.Base(angle, t) + new Point3(0f, height, 0f);

    private static void AddPost(List<RailingPart> parts, RailingStyle style, Point3 foot, float height, float width)
    {
        Point3 head = foot + new Point3(0f, height, 0f);
        parts.Add(new RailingPart(RailingPartKind.Post, foot, head, width));
        if (style == RailingStyle.Detailed)
        {
            Point3 tip = head + new Point3(0f, width * 0.9f, 0f);
            parts.Add(new RailingPart(RailingPartKind.Finial, tip, tip, width * 1.4f));
        }
        else
        {
            parts.Add(new RailingPart(RailingPartKind.PostCap, head, head + new Point3(0f, width * 0.35f, 0f), width * 1.35f));
        }
    }

    /// <summary>The detailed style's decoration: a straight bar with two C-scrolls back to back on it, each a quarter of the
    /// gap wide, facing along the railing (in the railing's own y-z plane).</summary>
    private static void AddScrollPair(List<RailingPart> parts, Point3 low, Point3 high, float pitch, float bar)
    {
        parts.Add(new RailingPart(RailingPartKind.Baluster, low, high, bar, 0f));
        Point3 middle = (low + high) * 0.5f;
        float height = (high - low).Length;
        float radius = Math.Min(pitch * 0.42f, height * 0.18f);
        foreach (int side in new[] { -1, 1 })
        {
            foreach (int level in new[] { -1, 1 })
            {
                var centre = middle + new Point3(0f, level * radius * 1.05f, side * radius);
                const int segments = 10;
                Point3 previous = Spiral(centre, radius, side, level, 0f);
                for (int s = 1; s <= segments; s++)
                {
                    Point3 next = Spiral(centre, radius, side, level, s / (float)segments);
                    parts.Add(new RailingPart(RailingPartKind.Scroll, previous, next, bar * 0.8f));
                    previous = next;
                }
            }
        }
    }

    /// <summary>A point on a scroll: on the bar at t = 0, then out and round a tightening curve to t = 1.</summary>
    internal static Point3 Spiral(Point3 centre, float radius, int side, int level, float t)
    {
        double theta = t * 1.5 * Math.PI;
        float r = radius * (1f - 0.55f * t);
        return centre + new Point3(0f, level * (float)(r * Math.Sin(theta)), -side * (float)(r * Math.Cos(theta)));
    }
}
