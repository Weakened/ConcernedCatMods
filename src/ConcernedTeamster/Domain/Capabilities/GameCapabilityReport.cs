using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Capabilities;

/// <summary>Immutable outcome of a startup capability probe (CT-002). The
/// capability is enabled only when every requirement verified; each missing
/// entry carries an actionable "Owner.member (reason)" string so the single
/// startup WARN line names exactly what the game changed.</summary>
public sealed class GameCapabilityReport
{
    public GameCapabilityReport(
        IReadOnlyList<string> verifiedMembers,
        IReadOnlyList<string> missingMembers)
    {
        VerifiedMembers = verifiedMembers;
        MissingMembers = missingMembers;
    }

    /// <summary>True when nothing is missing. An empty probe is trivially
    /// enabled; adapters always submit at least one requirement.</summary>
    public bool Enabled => MissingMembers.Count == 0;

    public IReadOnlyList<string> VerifiedMembers { get; }

    public IReadOnlyList<string> MissingMembers { get; }
}
