namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Player-facing workflow status of a pin, orthogonal to the
/// vanilla checked (crossed-off) flag.</summary>
internal enum AtlasPinStatus
{
    None = 0,
    Todo = 1,
    InProgress = 2,
    Done = 3,
    Warning = 4,
}
