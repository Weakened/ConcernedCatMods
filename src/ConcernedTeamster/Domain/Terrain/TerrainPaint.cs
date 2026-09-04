namespace TheConcernedCat.ConcernedTeamster.Domain.Terrain;

/// <summary>Pure classification of a terrain paint sample (CT-004). The
/// channel mapping is the game's own (dirt = red, cultivated = green,
/// paved = blue — verified constants recorded in CART_INTERNALS.md); the
/// threshold rejects faint residue so worn-off paint counts as untouched.</summary>
public static class TerrainPaint
{
    /// <summary>Minimum winning channel strength; below it the ground is
    /// untouched. 0.4 accepts full paint (1.0) and blended edges while
    /// rejecting neighbor bleed, matching the threshold family Cartographer
    /// ships for road classification.</summary>
    public const float DefaultThreshold = 0.4f;

    /// <summary>Classifies one paint sample. The strongest channel wins when
    /// it reaches the threshold; ties between distinct leaders stay
    /// untouched rather than picking arbitrarily.</summary>
    public static TerrainSurfaceKind Classify(float red, float green, float blue, float threshold)
    {
        if (float.IsNaN(red) || float.IsNaN(green) || float.IsNaN(blue))
        {
            return TerrainSurfaceKind.Unavailable;
        }

        float strongest = red;
        TerrainSurfaceKind kind = TerrainSurfaceKind.Dirt;
        if (green > strongest)
        {
            strongest = green;
            kind = TerrainSurfaceKind.Cultivated;
        }

        if (blue > strongest)
        {
            strongest = blue;
            kind = TerrainSurfaceKind.Paved;
        }

        if (strongest < threshold)
        {
            return TerrainSurfaceKind.Untouched;
        }

        // A dead tie between two full channels has no single truthful
        // winner; report untouched instead of guessing.
        int leaders = 0;
        if (red == strongest)
        {
            leaders++;
        }

        if (green == strongest)
        {
            leaders++;
        }

        if (blue == strongest)
        {
            leaders++;
        }

        return leaders > 1 ? TerrainSurfaceKind.Untouched : kind;
    }
}
