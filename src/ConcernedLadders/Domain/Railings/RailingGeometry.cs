using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedLadders.Domain.Railings;

/// <summary>The slope a railing follows (#556). Measured from the installed game's stair pieces (Valheim 1.0.21, their snap
/// points, read by <c>cl-ladders-devtools/doors/stairs.py</c>): every straight stair - <c>wood_stair</c>,
/// <c>stone_stair</c>, <c>ashwood_stair</c>, the black marble and grausten stairs, <c>dvergrprops_wood_stair</c> - rises
/// 1 m over 2 m (26.57°), and <c>wood_stepladder</c> rises 2 m over 2 m (45°). The owner asked for "33 and 45 degrees";
/// the railings follow the stairs the game has.</summary>
internal enum RailingAngle
{
    /// <summary>A level railing for floors, landings and the top of a stairwell: 2 m long.</summary>
    Level = 0,

    /// <summary>The straight stairs: 1 m up over 2 m.</summary>
    Stair = 1,

    /// <summary>The step ladder: 2 m up over 2 m.</summary>
    Steep = 2,
}

/// <summary>The railing material tiers (#556), the ladders' own three (owner: "same material categorization applies"):
/// wood simple, copper slightly decorated, iron the most detailed.</summary>
internal enum RailingTier
{
    Wood = 0,
    Copper = 1,
    Iron = 2,
}

/// <summary>A railing's geometry contract (#556), in the piece's own space: x across, y up, z along the railing from its
/// low end. The base line runs from the bottom snap (the origin) to the top snap, <see cref="Run"/> forward and
/// <see cref="Rise"/> up, so a railing whose bottom snap meets a stair's bottom side snap, turned to run up the stair, has
/// its top snap on the stair's top side snap. The posts, the handrail and the bottom rail all follow that line, so every
/// height above the stair is the same along it. Lead defaults; in game, OWNER GO-AROUND PENDING.</summary>
internal static class RailingGeometry
{
    /// <summary>Every railing is 2 m along the ground (the floor grid and both stair runs).</summary>
    internal const float Run = 2f;

    /// <summary>The handrail's top above the base line: a stair railing's usual height.</summary>
    internal const float HandrailHeight = 0.95f;

    /// <summary>The bottom rail's centre above the base line.</summary>
    internal const float BottomRailHeight = 0.1f;

    /// <summary>The posts' top above the base line, a little above the handrail.</summary>
    internal const float PostHeight = 1.05f;

    /// <summary>The collider's thickness across the railing: what keeps a player from walking off the stair's side.</summary>
    internal const float ColliderThickness = 0.12f;

    internal static float Rise(RailingAngle angle) => angle switch
    {
        RailingAngle.Level => 0f,
        RailingAngle.Stair => 1f,
        RailingAngle.Steep => 2f,
        _ => throw new ArgumentOutOfRangeException(nameof(angle)),
    };

    internal static float Slope(RailingAngle angle) => Rise(angle) / Run;

    internal static float Degrees(RailingAngle angle) => (float)(Math.Atan2(Rise(angle), Run) * 180.0 / Math.PI);

    /// <summary>The base line's length along the slope.</summary>
    internal static float Length(RailingAngle angle) => (float)Math.Sqrt(Run * Run + Rise(angle) * Rise(angle));

    /// <summary>The base line's height at <paramref name="z"/> along the railing.</summary>
    internal static float BaseAt(RailingAngle angle, float z) => Slope(angle) * z;

    internal static Point3 BottomSnap => new Point3(0f, 0f, 0f);

    internal static Point3 TopSnap(RailingAngle angle) => new Point3(0f, Rise(angle), Run);

    /// <summary>The stair the railing is made for, by its measured side snap points: its bottom and top side snaps are
    /// <see cref="Run"/> apart along the stair and <see cref="Rise"/> apart in height (wood_stair: (±1, 0, 1) and
    /// (±1, 1, -1); wood_stepladder: (±0.5, 0, 1) and (±0.5, 2, -1)).</summary>
    internal static (Point3 Bottom, Point3 Top) StairSideSnaps(RailingAngle angle) => angle switch
    {
        RailingAngle.Stair => (new Point3(1f, 0f, 1f), new Point3(1f, 1f, -1f)),
        RailingAngle.Steep => (new Point3(0.5f, 0f, 1f), new Point3(0.5f, 2f, -1f)),
        _ => (new Point3(1f, 0f, 1f), new Point3(1f, 0f, -1f)),
    };

    /// <summary>The collider: one thin box from just above the base line to the handrail's top, along the slope, in the
    /// slope's own frame (turned by <see cref="Degrees"/> about x). Centre and size in that frame.</summary>
    internal static (Point3 Centre, Point3 Size) Collider(RailingAngle angle)
    {
        float bottom = 0.05f, top = HandrailHeight;
        float length = Length(angle);
        // In the slope frame z runs along the base line and y is the vertical height above it, so the box is a
        // parallelogram's bounding box: its height measured square to the slope.
        float cos = Run / length;
        return (new Point3(0f, (bottom + top) * 0.5f * cos, length * 0.5f), new Point3(ColliderThickness, (top - bottom) * cos, length));
    }
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

/// <summary>What each tier's railing is made of (#556; owner references 2026-11-10): wood is simple - square posts with
/// a cap, a handrail and square balusters; copper is slightly decorated - twisted balusters with a knuckle half way up and
/// capped posts; iron is the most detailed - thinner twisted bars, a pair of scrolls between every few bars, a bottom rail
/// and finials on the posts. Game-free so every tier's parts are tested.</summary>
internal static class RailingDesign
{
    /// <summary>The widest gap between balusters (centre to centre), so a railing reads as a railing.</summary>
    internal const float BalusterPitch = 0.16f;

    internal static float PostWidth(RailingTier tier) => tier == RailingTier.Wood ? 0.11f : tier == RailingTier.Copper ? 0.08f : 0.07f;

    internal static float HandrailWidth(RailingTier tier) => tier == RailingTier.Wood ? 0.08f : 0.06f;

    internal static float BalusterWidth(RailingTier tier) => tier == RailingTier.Wood ? 0.045f : tier == RailingTier.Copper ? 0.03f : 0.022f;

    /// <summary>Twist of one baluster over its length: none for wood, a turn and a half for copper, three turns for iron.</summary>
    internal static float Twist(RailingTier tier) => tier == RailingTier.Wood ? 0f : tier == RailingTier.Copper ? 540f : 1080f;

    /// <summary>Iron puts a pair of scrolls in place of every this many balusters.</summary>
    internal const int IronScrollEvery = 4;

    /// <summary>Every part of a railing piece, in the piece's space.</summary>
    internal static IReadOnlyList<RailingPart> Parts(RailingTier tier, RailingAngle angle)
    {
        var parts = new List<RailingPart>();
        float post = PostWidth(tier), rail = HandrailWidth(tier), bar = BalusterWidth(tier);
        float run = RailingGeometry.Run;

        // The posts, at both ends, standing on the base line.
        foreach (float z in new[] { 0f, run })
        {
            float baseY = RailingGeometry.BaseAt(angle, z);
            var foot = new Point3(0f, baseY, z);
            var head = new Point3(0f, baseY + RailingGeometry.PostHeight, z);
            parts.Add(new RailingPart(RailingPartKind.Post, foot, head, post));
            if (tier == RailingTier.Iron)
            {
                parts.Add(new RailingPart(RailingPartKind.Finial, head + new Point3(0f, post * 0.9f, 0f), head + new Point3(0f, post * 0.9f, 0f), post * 1.4f));
            }
            else
            {
                parts.Add(new RailingPart(RailingPartKind.PostCap, head, head + new Point3(0f, post * 0.35f, 0f), post * 1.35f));
            }
        }

        // The handrail between the posts' inner faces, its top at the handrail height above the base line.
        float inset = post * 0.5f;
        Point3 Along(float z, float above) => new Point3(0f, RailingGeometry.BaseAt(angle, z) + above, z);
        float railCentre = RailingGeometry.HandrailHeight - rail * 0.5f;
        parts.Add(new RailingPart(RailingPartKind.Handrail, Along(inset, railCentre), Along(run - inset, railCentre), rail));
        if (tier != RailingTier.Wood || angle == RailingAngle.Level)
            parts.Add(new RailingPart(RailingPartKind.BottomRail, Along(inset, RailingGeometry.BottomRailHeight), Along(run - inset, RailingGeometry.BottomRailHeight), bar * 1.4f));

        // The balusters: evenly spaced between the posts, vertical, from the bottom rail (or the base line) to the handrail.
        float clear = run - 2f * inset;
        int count = Math.Max(1, (int)Math.Ceiling(clear / BalusterPitch) - 1);
        float pitch = clear / (count + 1);
        float bottomAbove = tier == RailingTier.Wood && angle != RailingAngle.Level ? 0f : RailingGeometry.BottomRailHeight;
        float topAbove = RailingGeometry.HandrailHeight - rail;
        for (int index = 1; index <= count; index++)
        {
            float z = inset + index * pitch;
            Point3 low = Along(z, bottomAbove), high = Along(z, topAbove);
            if (tier == RailingTier.Iron && index % IronScrollEvery == 0)
            {
                AddScrollPair(parts, low, high, pitch, bar);
                continue;
            }

            parts.Add(new RailingPart(RailingPartKind.Baluster, low, high, bar, Twist(tier)));
            if (tier == RailingTier.Copper)
            {
                Point3 middle = (low + high) * 0.5f;
                parts.Add(new RailingPart(RailingPartKind.Knuckle, middle, middle, bar * 2.6f));
            }
        }

        return parts;
    }

    /// <summary>Iron's decoration: a straight bar with two C-scrolls back to back on it, each a quarter of the gap wide,
    /// facing along the railing (in the railing's own y-z plane).</summary>
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
                // A three-quarter spiral, tightening inward, as straight segments.
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
