using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain.Animation;

/// <summary>Visible surface coordinates, independent of capsule/collider stand-off. Rear uses the actual
/// opposite side of the asymmetric rung plane; reversing a face never mirrors a player's skeleton scale.</summary>
internal static class OriginalRungSurface
{
    internal static ClimbPoint At(in LadderGeometry run, LadderFace face, float rungPlane, float worldY,
        float outwardOffset, float actorRightOffset)
    {
        ClimbPoint line = run.PointAt(worldY - run.Bottom.Y);
        float sign = (float)face;
        float awayX = run.StandingSide.X * sign, awayZ = run.StandingSide.Z * sign;
        return new ClimbPoint(line.X + run.StandingSide.X * rungPlane + awayX * outwardOffset - awayZ * actorRightOffset,
            worldY, line.Z + run.StandingSide.Z * rungPlane + awayZ * outwardOffset + awayX * actorRightOffset);
    }
}
