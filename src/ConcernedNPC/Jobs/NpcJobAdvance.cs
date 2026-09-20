using TheConcernedCat.ConcernedNPC.Planning;

namespace TheConcernedCat.ConcernedNPC.Jobs;

/// <summary>What a job wants its runtime to do next.
///
/// <b>Public because a role branches on it, and four values because four is how
/// many different things a role must do.</b> The plan's own
/// <see cref="JobPlanVerdict"/> has six, and the mapping between them is exactly
/// the taxonomy three roles would otherwise each get wrong: <c>Planned</c> means
/// walk, <c>NothingToDo</c> means finished, <c>BudgetExhausted</c> means ask
/// again next tick, and the other three mean stop. That mapping lives in
/// <see cref="NpcJobDriver"/>, once.</summary>
public enum NpcJobProgress
{
    /// <summary>Nobody asked. Never an instruction.</summary>
    Unspecified = 0,

    /// <summary>Carry out <see cref="NpcJobAdvance.Step"/>, then report what
    /// became of it through <see cref="NpcJobDriver.Done"/>,
    /// <see cref="NpcJobDriver.Skipped"/> or
    /// <see cref="NpcJobDriver.Failed"/>. Until one of those is called, asking
    /// again answers with the same step: one body has one destination.</summary>
    Do = 1,

    /// <summary>Every target the job was for was serviced. <b>The only progress
    /// a job may be reported finished on</b> - it is
    /// <c>JobReconciliation.IsComplete</c>, which asks both whether every step
    /// came off and whether the plan covered the whole job, because a perfectly
    /// executed plan for a third of a job is not a finished job.</summary>
    Finished = 2,

    /// <summary>Incomplete, not impossible: the looking or the planning ran out
    /// of what it was allowed to spend, or a zone was not loaded. <b>Never a
    /// reason to stop a job.</b> Ask again next tick, from wherever the body
    /// then is.</summary>
    Waiting = 3,

    /// <summary>The job is over and did not finish.
    /// <see cref="NpcJobAdvance.Reason"/> says why, in words a role can render
    /// for a player. Asking again does not help: nothing about the world has
    /// been given a chance to change, and a runtime that retried unchanged would
    /// spin. Every hold the job took out has already been given back.</summary>
    Stopped = 4,
}

/// <summary>One answer from the pump: what to do, what it is about, and why.
///
/// <b>Public because it is what the driver hands back</b>, and it carries the
/// step rather than the plan because a role that held the plan would be a role
/// deciding what order to do it in.</summary>
public readonly struct NpcJobAdvance
{
    internal NpcJobAdvance(
        NpcJobProgress progress, PlannedStep step, bool hasStep, JobPlanVerdict verdict, string reason)
    {
        Progress = progress;
        Step = step;
        HasStep = hasStep;
        Verdict = verdict;
        Reason = reason ?? string.Empty;
    }

    /// <summary>What to do.</summary>
    public NpcJobProgress Progress { get; }

    /// <summary>The step to carry out, with the target or the container it is
    /// about still attached. Meaningless unless <see cref="HasStep"/>.</summary>
    public PlannedStep Step { get; }

    /// <summary>Whether there is a step. True only for
    /// <see cref="NpcJobProgress.Do"/>.</summary>
    public bool HasStep { get; }

    /// <summary>The verdict on the last plan this job made.
    ///
    /// <b>Only meaningful read together with <see cref="Progress"/>, and it is
    /// not a second progress.</b> Two things end a job after planning has
    /// already succeeded - the work area kept moving under it, and the round cap
    /// or a round that achieved nothing - and neither has a verdict of its own,
    /// so a <see cref="NpcJobProgress.Stopped"/> answer can carry
    /// <see cref="JobPlanVerdict.Planned"/>. That is not a contradiction: the
    /// plan really was planned, and then execution ended the job. <b>What
    /// happened is <see cref="Progress"/>, why is <see cref="Reason"/>, and
    /// every execution-level termination names its own rule in the reason.</b>
    /// This is here for a runtime that wants to log the planning outcome, not
    /// for one deciding what to do next.</summary>
    public JobPlanVerdict Verdict { get; }

    /// <summary>Why, in the register of evidence: "nothing he may use holds 40
    /// wood". <b>This, and never the verdict's name, is what a player is shown
    /// -</b> a role that renders <c>ShortOfMaterial</c> tells a player they are
    /// out of wood while they are standing on it. Empty when there is nothing to
    /// explain.</summary>
    public string Reason { get; }
}
