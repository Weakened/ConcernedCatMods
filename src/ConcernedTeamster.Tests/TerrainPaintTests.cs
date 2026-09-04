using TheConcernedCat.ConcernedTeamster.Domain.Terrain;

namespace ConcernedTeamster.Tests;

/// <summary>CT-004: paint classification mirrors the game's verified channel
/// encoding (dirt = red, cultivated = green, paved = blue), rejects faint
/// residue, and never guesses on ties or invalid samples.</summary>
public class TerrainPaintTests
{
    [Theory]
    [InlineData(1f, 0f, 0f, TerrainSurfaceKind.Dirt)]
    [InlineData(0f, 1f, 0f, TerrainSurfaceKind.Cultivated)]
    [InlineData(0f, 0f, 1f, TerrainSurfaceKind.Paved)]
    [InlineData(0f, 0f, 0f, TerrainSurfaceKind.Untouched)]      // black = nothing
    [InlineData(0.39f, 0f, 0f, TerrainSurfaceKind.Untouched)]   // below threshold
    [InlineData(0.4f, 0f, 0f, TerrainSurfaceKind.Dirt)]         // at threshold
    [InlineData(0.7f, 0.2f, 0.3f, TerrainSurfaceKind.Dirt)]     // strongest wins
    [InlineData(0.2f, 0.3f, 0.9f, TerrainSurfaceKind.Paved)]
    [InlineData(1f, 0f, 1f, TerrainSurfaceKind.Untouched)]      // dead tie: no guess
    public void Classify_ChannelTable(float red, float green, float blue, TerrainSurfaceKind expected)
    {
        Assert.Equal(expected, TerrainPaint.Classify(red, green, blue, TerrainPaint.DefaultThreshold));
    }

    [Fact]
    public void Classify_NaNChannel_IsUnavailableNotAGuess()
    {
        Assert.Equal(TerrainSurfaceKind.Unavailable,
            TerrainPaint.Classify(float.NaN, 0f, 0f, TerrainPaint.DefaultThreshold));
    }
}
