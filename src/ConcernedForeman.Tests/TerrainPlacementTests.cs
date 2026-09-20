using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Construction;
using UnityEngine;
namespace ConcernedForeman.Tests;

public class TerrainPlacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_blueprint_piece_that_changes_terrain_is_refused(bool operation)
    {
        var prefab = new GameObject("future-foundation");
        Piece piece = prefab.Add(new Piece());
        if (operation) prefab.Add(new TerrainOp());
        else prefab.Add(new TerrainModifier());
        Assert.Equal(ProbeAnswer.No, PieceConstraints.Judge(piece, Vector3.zero, out string reason));
        Assert.Contains("changes terrain", reason);
        Assert.Contains("not enabled", reason);
    }
}
