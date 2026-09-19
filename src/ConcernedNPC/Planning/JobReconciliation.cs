using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>What became of one step.</summary>
internal enum StepOutcome
{
    /// <summary>Nobody said. Treated as never reached, which is the safe
    /// reading: a step nobody reported on certainly was not done.</summary>
    Unspecified = 0,

    /// <summary>Done.</summary>
    Done = 1,

    /// <summary>Looked at again and not worth doing - already done by somebody
    /// else, or gone. <b>The material stays carried</b>; where it ends up is
    /// what this reconciliation is for.</summary>
    Skipped = 2,

    /// <summary>Reached and it did not work.</summary>
    Failed = 3,

    /// <summary>The round ended before it got here.</summary>
    NotReached = 4,
}

/// <summary>One step's fate, as the runtime that walked it reports.</summary>
internal readonly struct StepResult
{
    internal StepResult(int step, StepOutcome outcome)
    {
        Step = step;
        Outcome = outcome;
    }

    internal int Step { get; }

    internal StepOutcome Outcome { get; }
}

/// <summary>The books, at the end of a round.
///
/// <b>What it answers.</b> Three things nobody can answer without the plan and
/// the outcomes together: is the job finished, what is still owed, and what is
/// he still carrying that the job no longer needs.
///
/// <b>Why the leftovers matter more than they look.</b> Every target skipped
/// mid-round was provisioned for, and the material for it is on the NPC's back.
/// A loop that fetched per target never had this problem and never had the
/// benefit either; a loop that provisions per trip has to be able to say, at the
/// end, "these twelve nails were for the wall that is already built". What is
/// done about them - put back, kept for the next round, handed over - is the
/// role's decision and the custody layer's job. This says what they are.
///
/// <b>What it is not.</b> Not custody. It subtracts what the plan said would be
/// moved, not what was actually moved, because what was actually moved is a
/// ledger's answer and a ledger is the only thing entitled to say "this may or
/// may not have happened". A reconciliation that guessed at real quantities
/// would be a second source of truth about custody, and the two would
/// drift.</summary>
internal readonly struct JobReconciliation
{
    internal JobReconciliation(
        int planned,
        int done,
        int skipped,
        int failed,
        int notReached,
        JobManifest outstanding,
        JobManifest leftOver,
        int leftForAnotherRound)
    {
        Planned = planned;
        Done = done;
        Skipped = skipped;
        Failed = failed;
        NotReached = notReached;
        Outstanding = outstanding;
        LeftOver = leftOver;
        LeftForAnotherRound = leftForAnotherRound < 0 ? 0 : leftForAnotherRound;
    }

    /// <summary>How many steps the plan had.</summary>
    internal int Planned { get; }

    internal int Done { get; }

    internal int Skipped { get; }

    internal int Failed { get; }

    internal int NotReached { get; }

    /// <summary>What the targets that were not serviced still need. The manifest
    /// of the next round, if there is one.</summary>
    internal JobManifest Outstanding { get; }

    /// <summary>What was fetched and not used up. Still carried.</summary>
    internal JobManifest LeftOver { get; }

    /// <summary>How many targets of the job the plan never reached at all,
    /// because the plan was for part of the job.
    ///
    /// <b>Not the same as unreached steps.</b> These are targets that were never
    /// written into a step: the plan covered eight trips of twelve, or one trip
    /// took more chests than a provisioning phase opens. Carried here because
    /// this is the type a job is reported finished on, and a count of perfectly
    /// executed steps says nothing about work the plan left out.
    ///
    /// <b>The constructor has no default for it.</b> It had one, and the default
    /// was nought - which is the claim "this plan left nothing out", on the type
    /// that owns <see cref="IsComplete"/>, the only thing a job may be reported
    /// finished on. A silent parameter that defaults to complete is a latent bug
    /// while this type is internal and a bug every role author can write without
    /// noticing the moment it is not.</summary>
    internal int LeftForAnotherRound { get; }

    /// <summary>Whether every target <b>the job</b> was for was serviced.
    /// <b>The only thing a job may be reported finished on</b> - so it asks two
    /// questions, not one: did every step of this plan come off, and did this
    /// plan cover the whole job. A plan for eight trips out of twelve can
    /// satisfy the first perfectly and it is not a finished job, and an NPC that
    /// said it was would be the most expensive kind of wrong.</summary>
    internal bool IsComplete =>
        Planned > 0 && Failed == 0 && NotReached == 0 && Outstanding.IsEmpty && LeftForAnotherRound == 0;

    /// <summary>Whether there is more to do. True whenever anything is still
    /// owed or was never planned for, whatever the step counts say.
    ///
    /// <b>A statement of fact, and named as one.</b> It was called
    /// <c>NeedsAnotherRound</c>, which reads as a recommendation - and a driver
    /// that looped on the recommendation would spin for ever on a job that
    /// cannot proceed, because "there is unfinished work" is true of a job
    /// refused for want of material just as it is of a job stopped by the trip
    /// cap. Whether to plan again is the <b>verdict's</b> answer:
    /// <see cref="JobPlanVerdict.BudgetExhausted"/> retries,
    /// <see cref="JobPlanVerdict.Planned"/> walks,
    /// <see cref="JobPlanVerdict.NothingToDo"/> finishes, and the rest stop and
    /// surface <see cref="JobPlan.Reason"/>.</summary>
    internal bool HasUnfinishedWork =>
        !Outstanding.IsEmpty || Failed > 0 || NotReached > 0 || LeftForAnotherRound > 0;
}

/// <summary>Closing the books on a round.</summary>
internal static class JobReconciler
{
    /// <summary>Works out what the round actually achieved.</summary>
    /// <param name="plan">What was planned, with the subjects attached.</param>
    /// <param name="results">What became of each step. A step with no result is
    /// taken as never reached rather than as done.</param>
    internal static JobReconciliation Reconcile(in JobTourPlan plan, IReadOnlyList<StepResult>? results)
    {
        var outcomes = new Dictionary<int, StepOutcome>();
        if (results != null)
        {
            foreach (StepResult result in results)
            {
                // Last word wins: a step reported twice was walked twice, and
                // the later report is the one that is still true.
                outcomes[result.Step] = result.Outcome;
            }
        }

        int done = 0;
        int skipped = 0;
        int failed = 0;
        int notReached = 0;
        var fetched = new List<JobManifest>();
        var consumed = new List<JobManifest>();
        var owed = new List<JobManifest>();

        foreach (PlannedStep step in plan.Steps)
        {
            if (!outcomes.TryGetValue(step.Step.Index, out StepOutcome outcome))
            {
                outcome = StepOutcome.Unspecified;
            }

            switch (outcome)
            {
                case StepOutcome.Done:
                    done++;
                    if (step.IsCollect)
                    {
                        fetched.Add(step.Moves);
                    }
                    else
                    {
                        consumed.Add(step.Moves);
                    }

                    break;

                case StepOutcome.Skipped:
                    skipped++;
                    break;

                case StepOutcome.Failed:
                    failed++;
                    if (!step.IsCollect)
                    {
                        owed.Add(step.Moves);
                    }

                    break;

                default:
                    notReached++;
                    if (!step.IsCollect)
                    {
                        owed.Add(step.Moves);
                    }

                    break;
            }
        }

        // A skipped target is not owed: somebody else did it, or it is gone.
        // A failed or unreached one is. That distinction is the whole reason a
        // skip and a failure are different outcomes.
        JobManifest outstanding = ManifestArithmetic.Merge(owed);
        JobManifest leftOver = ManifestArithmetic.Subtract(
            ManifestArithmetic.Merge(fetched), ManifestArithmetic.Merge(consumed));

        return new JobReconciliation(
            plan.Steps.Count,
            done,
            skipped,
            failed,
            notReached,
            outstanding,
            leftOver,
            plan.LeftForAnotherRound);
    }
}
