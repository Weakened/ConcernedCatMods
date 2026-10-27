using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>The cross-section of a rail or rung.</summary>
internal enum LadderProfile
{
    /// <summary>A rectangle with sharp edges (wood rails and rungs, iron flat-bar rails).</summary>
    Box,

    /// <summary>A rectangle whose two narrow ends are half circles (copper rails: a flattened tube).</summary>
    Stadium,

    /// <summary>A circle (metal rungs).</summary>
    Round,
}

/// <summary>The visible geometry of one ladder material, in section-local metres (x right, y up, z toward the
/// climbing side). Owner note 2026-10-26: copper and iron get thinner metal rails and round rungs; wood is unchanged.
///
/// <para>Invariants every style keeps (unit-tested): 2 m tall; <see cref="RungShape.PerSection"/> rungs whose centres
/// are unchanged ((i + 0.5) / 3 m up, on the <see cref="RungShape.Plane"/> plane); the rails' outer edges at ±0.40 m
/// and inside the shared collider box (0.8 m wide, z 0.08 to 0.24), so collision, interaction, climbing lanes and the
/// edge snap's footprint (<see cref="LadderFootprint.Section"/>) are the same for every material. The metal rails fill
/// the collider's depth exactly, so their back touches the surface a snapped ladder rests against.</para>
///
/// <para>Rung contact for presentation: a box rung's top is <see cref="RungHalfHeight"/> above its centre and its
/// face <see cref="RungHalfDepth"/> in front of it; a round rung has both equal to <see cref="RungRadius"/>. The
/// placed section's <c>LadderSegment</c> carries these values.</para></summary>
internal readonly struct LadderStyle
{
    internal LadderStyle(LadderProfile railProfile, float railWidth, float railDepth, LadderProfile rungProfile,
        float rungHalfHeight, float rungHalfDepth, float rungHalfLength)
    {
        RailProfile = railProfile;
        RailWidth = railWidth;
        RailDepth = railDepth;
        RungProfile = rungProfile;
        RungHalfHeight = rungHalfHeight;
        RungHalfDepth = rungHalfDepth;
        RungHalfLength = rungHalfLength;
    }

    /// <summary>Distance from the centreline to the rails' outer edges (half the collider's width).</summary>
    internal const float OuterHalfWidth = 0.4f;

    /// <summary>The rail and rung centre plane.</summary>
    internal const float RailPlane = RungShape.Plane;

    /// <summary>Unchanged since 0.1: 0.12 × 0.14 m box rails, 0.62 × 0.08 × 0.12 m box rungs.</summary>
    internal static LadderStyle Wood => new LadderStyle(LadderProfile.Box, 0.12f, 0.14f, LadderProfile.Box,
        RungShape.HalfHeight, RungShape.HalfDepth, RungShape.HalfWidth);

    /// <summary>Flattened copper tube rails (5 × 16 cm, rounded front and back) and 5 cm round rungs.</summary>
    internal static LadderStyle Copper => new LadderStyle(LadderProfile.Stadium, 0.05f, 0.16f, LadderProfile.Round,
        0.025f, 0.025f, OuterHalfWidth - 0.025f);

    /// <summary>Sharp iron flat-bar rails (4 × 16 cm) and 4 cm round rungs.</summary>
    internal static LadderStyle Iron => new LadderStyle(LadderProfile.Box, 0.04f, 0.16f, LadderProfile.Round,
        0.02f, 0.02f, OuterHalfWidth - 0.02f);

    internal LadderProfile RailProfile { get; }
    internal float RailWidth { get; }
    internal float RailDepth { get; }
    internal LadderProfile RungProfile { get; }
    internal float RungHalfHeight { get; }
    internal float RungHalfDepth { get; }

    /// <summary>Half the rung's length: it runs from rail centre to rail centre (wood: into the rails).</summary>
    internal float RungHalfLength { get; }

    /// <summary>A round rung's radius, 0 for a box rung.</summary>
    internal float RungRadius => RungProfile == LadderProfile.Round ? RungHalfHeight : 0f;

    /// <summary>The rails' centres sit at ±this.</summary>
    internal float RailCentre => OuterHalfWidth - RailWidth * 0.5f;

    internal float RailBack => RailPlane - RailDepth * 0.5f;
    internal float RailFront => RailPlane + RailDepth * 0.5f;

    /// <summary>Rung i's centre height above the section base.</summary>
    internal static float RungHeight(int index) => (index + 0.5f) * (LadderEdgeSnap.SectionHeight / RungShape.PerSection);
}

/// <summary>One vertex of a cross-section ring: position and outward normal in the profile plane (a = width axis,
/// b = depth axis), and the distance around the ring (for texture coordinates).</summary>
internal readonly struct ProfileVertex
{
    internal ProfileVertex(float a, float b, float normalA, float normalB, float distance)
    {
        A = a;
        B = b;
        NormalA = normalA;
        NormalB = normalB;
        Distance = distance;
    }

    internal float A { get; }
    internal float B { get; }
    internal float NormalA { get; }
    internal float NormalB { get; }
    internal float Distance { get; }
}

/// <summary>Cross-section rings for the procedural rail and rung meshes. A ring is a closed loop listed
/// counter-clockwise seen from +length (a toward b); a sharp corner is two vertices at the same place with different
/// normals, so the extruded surface shades flat there. The last vertex repeats the first at the full perimeter so the
/// texture wraps without a seam jump.</summary>
internal static class LadderProfiles
{
    internal const int RoundSegments = 16;

    internal static IReadOnlyList<ProfileVertex> Ring(LadderProfile profile, float width, float depth)
    {
        if (!LadderRules.Finite(width) || !LadderRules.Finite(depth) || width <= 0f || depth <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A profile needs a positive finite size.");
        }

        var ring = new List<ProfileVertex>();
        float ha = width * 0.5f, hb = depth * 0.5f;
        switch (profile)
        {
            case LadderProfile.Box:
            {
                // Four faces, each with its own two vertices: +a, +b, -a, -b (counter-clockwise).
                float[,] corners = { { ha, -hb }, { ha, hb }, { -ha, hb }, { -ha, -hb } };
                float[,] normals = { { 1f, 0f }, { 0f, 1f }, { -1f, 0f }, { 0f, -1f } };
                float distance = 0f;
                for (int face = 0; face < 4; face++)
                {
                    int next = (face + 1) % 4;
                    float na = normals[face, 0], nb = normals[face, 1];
                    ring.Add(new ProfileVertex(corners[face, 0], corners[face, 1], na, nb, distance));
                    distance += Length(corners[next, 0] - corners[face, 0], corners[next, 1] - corners[face, 1]);
                    ring.Add(new ProfileVertex(corners[next, 0], corners[next, 1], na, nb, distance));
                }

                break;
            }
            case LadderProfile.Round:
            {
                float radius = Math.Min(ha, hb);
                for (int index = 0; index <= RoundSegments; index++)
                {
                    double angle = 2.0 * Math.PI * index / RoundSegments;
                    float ca = (float)Math.Cos(angle), cb = (float)Math.Sin(angle);
                    ring.Add(new ProfileVertex(radius * ca, radius * cb, ca, cb, (float)(radius * angle)));
                }

                return ring;
            }
            case LadderProfile.Stadium:
            {
                // Half circles at both ends of the long axis, joined by two flat sides. The long axis is b (depth).
                float radius = Math.Min(ha, hb);
                float straight = Math.Max(0f, Math.Max(ha, hb) - radius);
                bool alongB = hb >= ha;
                int half = RoundSegments / 2;
                float distance = 0f;
                ProfileVertex? previous = null;
                for (int end = 0; end < 2; end++)
                {
                    float centre = end == 0 ? straight : -straight;
                    for (int index = 0; index <= half; index++)
                    {
                        double angle = (alongB ? 0.0 : -Math.PI / 2.0) + Math.PI * end + Math.PI * index / half;
                        float ca = (float)Math.Cos(angle), cb = (float)Math.Sin(angle);
                        float a = radius * ca + (alongB ? 0f : centre);
                        float b = radius * cb + (alongB ? centre : 0f);
                        if (previous.HasValue) { distance += Length(a - previous.Value.A, b - previous.Value.B); }
                        var vertex = new ProfileVertex(a, b, ca, cb, distance);
                        ring.Add(vertex);
                        previous = vertex;
                    }
                }

                ProfileVertex first = ring[0];
                distance += Length(first.A - previous!.Value.A, first.B - previous.Value.B);
                ring.Add(new ProfileVertex(first.A, first.B, first.NormalA, first.NormalB, distance));
                return ring;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(profile), "Unknown profile.");
        }

        return ring;
    }

    private static float Length(float a, float b) => (float)Math.Sqrt(a * a + b * b);
}
