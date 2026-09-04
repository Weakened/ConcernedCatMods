namespace TheConcernedCat.ConcernedTeamster.Domain.Terrain;

/// <summary>Slope classification relative to the cart's heading (CT-004):
/// climbing means the ground ahead of the pull handle is higher. Only
/// meaningful while grade data is available.</summary>
public enum GradeDirection
{
    Level,
    Climbing,
    Descending,
}
