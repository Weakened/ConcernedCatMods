using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

public class RecoveryShapeHeuristicTests
{
    /// <summary>Builds a paint lookup from row strings ('#' = road paint),
    /// treating everything outside the map as unpainted, like a chunk seam.</summary>
    private static Func<int, int, bool> Grid(params string[] rows)
    {
        return (x, y) =>
            y >= 0 && y < rows.Length &&
            x >= 0 && x < rows[y].Length &&
            rows[y][x] == '#';
    }

    [Fact]
    public void SingleWidthPath_IsPathLike()
    {
        var grid = Grid(
            ".....",
            ".....",
            "#####",
            ".....",
            ".....");

        Assert.True(RecoveryShapeHeuristic.IsPathLike(grid, 2, 2));
    }

    [Fact]
    public void DoubleWidthPath_IsPathLike()
    {
        var grid = Grid(
            ".....",
            "#####",
            "#####",
            ".....",
            ".....");

        Assert.True(RecoveryShapeHeuristic.IsPathLike(grid, 2, 2));
    }

    [Fact]
    public void DiagonalPath_IsPathLike()
    {
        var grid = Grid(
            "#....",
            ".#...",
            "..#..",
            "...#.",
            "....#");

        Assert.True(RecoveryShapeHeuristic.IsPathLike(grid, 2, 2));
    }

    [Fact]
    public void IsolatedDab_IsPathLike()
    {
        var grid = Grid(
            ".....",
            ".....",
            "..#..",
            ".....",
            ".....");

        Assert.True(RecoveryShapeHeuristic.IsPathLike(grid, 2, 2));
    }

    [Fact]
    public void BlobInterior_IsNotPathLike()
    {
        var grid = Grid(
            "#####",
            "#####",
            "#####",
            "#####",
            "#####");

        Assert.False(RecoveryShapeHeuristic.IsPathLike(grid, 2, 2));
    }

    [Fact]
    public void PlazaBoundary_IsNotPathLike()
    {
        // A half-plane of paint: the edge of a broad leveled base. The
        // boundary cell sees 15 of 25 painted cells and must be rejected,
        // or every plaza would grow an outline of fake road.
        var grid = Grid(
            ".....",
            ".....",
            "#####",
            "#####",
            "#####");

        Assert.False(RecoveryShapeHeuristic.IsPathLike(grid, 2, 2));
    }

    [Fact]
    public void PathAlongChunkSeam_IsPathLike()
    {
        // Cells beyond the map read as unpainted, so a road hugging the
        // seam still qualifies.
        var grid = Grid(
            "#####",
            ".....",
            ".....",
            ".....",
            ".....");

        Assert.True(RecoveryShapeHeuristic.IsPathLike(grid, 2, 0));
    }
}
