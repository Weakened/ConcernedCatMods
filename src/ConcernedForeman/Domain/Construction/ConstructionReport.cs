namespace TheConcernedCat.ConcernedForeman.Domain.Construction;

/// <summary>How a round of building ended, in the four shapes a runtime has to
/// tell apart.
///
/// <b>These are the shared runtime's own four, deliberately.</b>
/// <c>NpcJobProgress</c> is <c>Do</c>, <c>Finished</c>, <c>Waiting</c> and
/// <c>Stopped</c>, and that taxonomy - which verdict means walk, which means
/// finished, which means ask again, which means give up - is the library's and
/// lives there once. Restating it in a role's own words would be a second
/// taxonomy to keep in step with the first. This enum exists only so the
/// sentences below can be written and proved with no game and no library
/// assembly present; the mapping is one line in the adapter.</summary>
internal enum RoundOutcome
{
    /// <summary>Nobody asked. Never a result.</summary>
    Unspecified = 0,

    /// <summary>There is work in hand and it is going on.</summary>
    Working = 1,

    /// <summary>The job serviced every target it was for.</summary>
    Finished = 2,

    /// <summary>Incomplete, not impossible: something ran out of what it was
    /// allowed to spend, a zone was not loaded, a container was not free, the
    /// material is not there yet. <b>Never a reason to cancel an order.</b>
    /// </summary>
    Waiting = 3,

    /// <summary>The job is over and did not finish. Asking again does not help.
    /// Every hold it took out has been given back.</summary>
    Stopped = 4,
}

/// <summary>What one round of the shelter job came to, as this product sees it.
///
/// <b>Why <see cref="LeftForAnotherRound"/> is carried here at all.</b> The
/// shared runtime's planner caps how many trips one plan writes out and how many
/// chests one provisioning phase opens, and reports honestly how many targets
/// those caps left behind. The driver reads that number to decide whether the
/// job is finished - that is its job and this product does not repeat it - but a
/// player watching Thorstein walk away from a half-built cottage is owed the
/// number too, and turning it into a sentence about a shelter is the one thing
/// the library may never do.</summary>
internal readonly struct ShelterRound
{
    internal ShelterRound(
        RoundOutcome outcome,
        int built,
        int leftForAnotherRound,
        MaterialTally? carrying,
        MaterialTally? missing,
        string? reason)
    {
        Outcome = outcome;
        Built = built < 0 ? 0 : built;
        LeftForAnotherRound = leftForAnotherRound < 0 ? 0 : leftForAnotherRound;
        Carrying = carrying ?? new MaterialTally();
        Missing = missing ?? new MaterialTally();
        Reason = reason ?? string.Empty;
    }

    /// <summary>How it ended.</summary>
    internal RoundOutcome Outcome { get; }

    /// <summary>How many pieces went up.</summary>
    internal int Built { get; }

    /// <summary>How many targets of the job this round's plan did not reach.
    /// </summary>
    internal int LeftForAnotherRound { get; }

    /// <summary>What was fetched and not used, and is still on him.</summary>
    internal MaterialTally Carrying { get; }

    /// <summary>What nothing he may open holds, when that is why he is waiting.
    /// </summary>
    internal MaterialTally Missing { get; }

    /// <summary>The runtime's own sentence, in the register of evidence rather
    /// than the name of a verdict.</summary>
    internal string Reason { get; }
}

/// <summary>Everything a build order says about itself, once the work has
/// started.
///
/// <b>The one judgement in here, and why it is the role's.</b> A job can service
/// every target its plan was for and leave a cottage with a hole in it, because
/// a plan is capped and a phase may not have been its turn. The driver knows
/// whether <i>its plan</i> was covered; only this product knows whether
/// <i>seventeen pieces</i> are standing, because only this product knows what a
/// shelter is. So <see cref="IsShelterFinished"/> asks the world, and a round
/// that reports itself finished over an unfinished cottage is reported
/// honestly.</summary>
internal static class ConstructionReport
{
    /// <summary>Whether the shelter itself is done. <b>The world's answer, not
    /// the plan's.</b></summary>
    internal static bool IsShelterFinished(ConstructionProgress? progress) =>
        progress != null && progress.IsComplete;

    /// <summary>The line a player reads after a round.</summary>
    internal static string Line(in ShelterRound round, ConstructionProgress? progress)
    {
        if (IsShelterFinished(progress))
        {
            return "The shelter is finished.";
        }

        switch (round.Outcome)
        {
            case RoundOutcome.Finished:
                // The job says it serviced everything and the cottage is not
                // standing. Saying "finished" here is the failure the shared
                // runtime's own reviewers opened with, arrived at from the
                // role's side.
                return "That round finished and the shelter is not: " + Left(progress) +
                    " still to place. " + Carrying(round);

            case RoundOutcome.Working:
                return Built(round) + Left(progress) + " still to place." + Carrying(round);

            case RoundOutcome.Waiting:
                return Waiting(round);

            case RoundOutcome.Stopped:
                return ConstructionSentences.Stopped(
                    round.Reason.Length != 0 ? round.Reason : "no reason was given.",
                    refunded: null,
                    stillCarried: round.Carrying);

            default:
                return "The round did not say how it ended, so nothing is claimed about it.";
        }
    }

    private static string Waiting(in ShelterRound round)
    {
        if (!round.Missing.IsEmpty)
        {
            return ConstructionSentences.ShortOfMaterial(round.Missing);
        }

        return round.Reason.Length != 0
            ? "The order is waiting: " + round.Reason
            : "The order is waiting.";
    }

    private static string Built(in ShelterRound round)
    {
        if (round.Built == 0)
        {
            return "Nothing went up that round; ";
        }

        return "Built " + round.Built + (round.Built == 1 ? " piece" : " pieces") + "; ";
    }

    private static string Left(ConstructionProgress? progress) =>
        progress == null ? "some pieces are" : progress.Remaining.Count + " pieces are";

    private static string Carrying(in ShelterRound round)
    {
        string left = round.LeftForAnotherRound > 0
            ? " " + round.LeftForAnotherRound +
              " of them were more than this plan covered, so there will be another round."
            : string.Empty;
        string carried = round.Carrying.IsEmpty
            ? string.Empty
            : " He is still carrying " + round.Carrying.Describe() +
              "; it is in his own inventory and the next round plans against it.";
        return left + carried;
    }
}
