using TheConcernedCat.ConcernedTeamster.Domain.Support;

namespace ConcernedTeamster.Tests;

/// <summary>CT-041: the one link the in-game Report a Bug button and the
/// README must agree on.</summary>
public class FeedbackLinksTests
{
    [Fact]
    public void IssuesUrl_PointsAtThePublicGitHubTracker()
    {
        Assert.Equal("https://github.com/Weakened/ConcernedCatMods/issues", FeedbackLinks.IssuesUrl);
        Assert.StartsWith("https://github.com/", FeedbackLinks.IssuesUrl);
    }
}
