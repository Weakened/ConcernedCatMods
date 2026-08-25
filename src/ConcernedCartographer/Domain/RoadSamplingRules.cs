namespace TheConcernedCat.ConcernedCartographer.Roads;

internal readonly struct RoadSamplingRules
{
    public RoadSamplingRules(
        float minimumSpacingMeters,
        float maximumGapMeters,
        float duplicateSuppressionMeters)
    {
        MinimumSpacingMeters = minimumSpacingMeters;
        MaximumGapMeters = maximumGapMeters;
        DuplicateSuppressionMeters = duplicateSuppressionMeters;
    }

    public float MinimumSpacingMeters { get; }
    public float MaximumGapMeters { get; }

    /// <summary>Radius within which a sample near already-recorded road ink of the
    /// same kind is skipped instead of stored. Zero disables suppression.</summary>
    public float DuplicateSuppressionMeters { get; }
}
