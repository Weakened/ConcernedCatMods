namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Where a road observation came from. Sources build strokes
/// independently, so interleaved observations never break each other's
/// geometry, and a failing source can be disabled without touching the
/// others.</summary>
internal enum RoadObservationSource
{
    /// <summary>The v0.1 behavior: sampling the terrain beneath the local
    /// player as they walk.</summary>
    Traversal = 1,

    /// <summary>A confirmed successful terrain-paint action (hoe path,
    /// stonecutter paving) captured at its brush position.</summary>
    Construction = 2,

    /// <summary>Road paint recovered from an already-loaded terrain chunk
    /// that the player has previously explored.</summary>
    ChunkRecovery = 3,
}
