namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>What the manifest panel is actually showing (CT-007), so empty,
/// filtered-to-nothing, and stale situations render explicitly.</summary>
public enum CargoManifestState
{
    /// <summary>No manifest at all — no cart selected or its container is
    /// unreadable.</summary>
    NoManifest,

    /// <summary>The cart's container is empty.</summary>
    Empty,

    /// <summary>Items exist but the filter matches none of them.</summary>
    NoMatch,

    /// <summary>Fresh rows are displayed.</summary>
    Live,

    /// <summary>Rows are displayed but the capture is old; visibly marked.</summary>
    Stale,
}
