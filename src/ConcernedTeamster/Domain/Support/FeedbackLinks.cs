namespace TheConcernedCat.ConcernedTeamster.Domain.Support;

/// <summary>The one place beta feedback is routed to (CT-041). A single
/// frozen constant so the in-game Report a Bug button, the README, and any
/// future doc reference to "where to report" can never drift apart.
/// Opening this URL only ever happens on an explicit player click — nothing
/// in Teamster visits it automatically, and visiting it sends the browser
/// there, not any Teamster data.</summary>
public static class FeedbackLinks
{
    public const string IssuesUrl = "https://github.com/Weakened/ConcernedCatMods/issues";
}
