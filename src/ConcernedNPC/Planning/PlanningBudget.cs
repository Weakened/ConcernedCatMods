using System;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>How much looking one pass of planning is allowed to do.
///
/// <b>Why an object and not a number.</b> Because the phases share it. Observing
/// the world, choosing which chests to draw from and partitioning the job into
/// tours all cost, and a per-phase cap would let three cheap phases add up to a
/// frame the player feels. One budget spent by all of them is the only shape
/// where the total is the number that was promised.
///
/// <b>An exhausted budget is never an answer about the world.</b> Every phase
/// that runs out says so - <see cref="Work.AreaScanOutcome.Incomplete"/>,
/// <see cref="JobPlanVerdict.BudgetExhausted"/> - and every one of those means
/// "ask again next tick". None of them ever means "there is nothing there", and
/// none of them is a reason to stop a job. That distinction is the single most
/// expensive thing for an NPC to get wrong: a job reported finished because a
/// budget ran out is a wall that never gets built and a player who is told it
/// was.
///
/// <b>Zero refuses everything.</b> A defaulted budget is not an unlimited one.
/// Nothing here widens to a default, exactly as nothing else in this package
/// does.</summary>
internal sealed class PlanningBudget
{
    internal PlanningBudget(int allowance)
    {
        Allowance = allowance < 0 ? 0 : allowance;
    }

    /// <summary>What it started with.</summary>
    internal int Allowance { get; }

    /// <summary>What has been spent.</summary>
    internal int Spent { get; private set; }

    /// <summary>What is left.</summary>
    internal int Remaining => Allowance - Spent;

    /// <summary>Whether there is nothing left. A phase that sees this stops and
    /// reports that it stopped; it never reports that it finished.</summary>
    internal bool IsExhausted => Spent >= Allowance;

    /// <summary>A budget nothing will run out of, for tests and for a caller
    /// that has already bounded its input another way. Deliberately not the
    /// default: reaching it has to be a decision somebody wrote down.</summary>
    internal static PlanningBudget Unlimited() => new PlanningBudget(int.MaxValue);

    /// <summary>Spends <paramref name="cost"/>, or answers false and spends
    /// nothing. Partial spending is refused on purpose: a phase that got half
    /// of what it asked for has no way to do half a look.</summary>
    internal bool TrySpend(int cost = 1)
    {
        if (cost <= 0)
        {
            return true;
        }

        if (Remaining < cost)
        {
            return false;
        }

        Spent += cost;
        return true;
    }
}
