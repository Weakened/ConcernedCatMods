using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>One thing the NPC will do, in order.
///
/// <b>Opaque on purpose.</b> <see cref="Action"/> and <see cref="Subject"/> are
/// the role's own tokens - "fell", "carry", "place", and whatever it is being
/// done to. This library orders steps, counts them, reports which one a job
/// stopped at and rebuilds the sequence after an interruption; it never branches
/// on what a step means. A <c>switch</c> over these strings anywhere in this
/// package is the defect the whole package is defined against.</summary>
internal readonly struct JobStep
{
    internal JobStep(int index, string action, string subject, NpcPoint at, bool hasPlace, int units)
    {
        Index = index;
        Action = action ?? string.Empty;
        Subject = subject ?? string.Empty;
        At = at;
        HasPlace = hasPlace;
        Units = units;
    }

    /// <summary>Position in the plan, from zero. Stable for the life of the
    /// plan: it is half of every reservation id the step takes out, so
    /// renumbering steps renumbers reservations.</summary>
    internal int Index { get; }

    /// <summary>The role's token for what is done.</summary>
    internal string Action { get; }

    /// <summary>The role's token for what it is done to.</summary>
    internal string Subject { get; }

    /// <summary>Where, if anywhere. Meaningless unless <see cref="HasPlace"/>.
    /// </summary>
    internal NpcPoint At { get; }

    /// <summary>Whether this step happens somewhere in particular. A step
    /// without a place is not a step at the origin.</summary>
    internal bool HasPlace { get; }

    /// <summary>How many units this step handles, or zero where the step is not
    /// about a quantity.</summary>
    internal int Units { get; }
}

/// <summary>Why a plan is what it is.</summary>
internal enum JobPlanVerdict
{
    /// <summary>Nobody planned. Never a plan.</summary>
    Unspecified = 0,

    /// <summary>A complete, ordered plan for the whole job.</summary>
    Planned = 1,

    /// <summary>There is nothing to do: the job is already satisfied. The only
    /// verdict on which a job may be reported finished without doing
    /// anything.</summary>
    NothingToDo = 2,

    /// <summary>The job is understood and cannot be provisioned: the manifest
    /// asks for more than is reachable. Refused before anything starts, which is
    /// the entire benefit of planning the whole job first - a player is told
    /// what is missing instead of watching an NPC do a third of a wall.
    ///
    /// <b>Reachable means reachable in one round.</b> Material sitting in the
    /// ninth chest, when a round opens eight, is exactly as unreachable as
    /// material nobody has. The two are different <i>sentences</i> and the same
    /// verdict, and which one it is reads off the shortfall:
    ///
    /// <list type="bullet">
    /// <item><description>a <b>non-empty</b> shortfall - it is not there. The
    /// fix is to bring more.</description></item>
    /// <item><description>an <b>empty</b> shortfall - it is there, and not
    /// within one round. The fix is to bring it together.</description></item>
    /// </list>
    ///
    /// Not a new verdict, because no caller branches on the difference: both are
    /// terminal until a person acts, both produce a player sentence out of
    /// <see cref="JobPlan.Reason"/>, and a driver does the identical thing with
    /// each. The day a role actually responds to scattered material differently
    /// - consolidating rather than refusing - the member is additive and gets
    /// added then, with a caller to justify it.</summary>
    ShortOfMaterial = 3,

    /// <summary>The work area could not be read or does not exist. Fail closed;
    /// nothing widens to a default.</summary>
    AreaInvalid = 4,

    /// <summary>Planning ran out of its budget. Incomplete, not impossible: ask
    /// again next tick. Never a reason to stop a job.</summary>
    BudgetExhausted = 5,

    /// <summary>Refused for a reason the request itself carries - a malformed
    /// request, an identity that holds no body, an empty manifest where one was
    /// required.</summary>
    Refused = 6,
}

/// <summary>What the NPC intends to do, worked out in full before any of it
/// happens.
///
/// <b>What it guarantees: a plan knows what it was computed against, so a stale
/// one is refused rather than applied.</b> The area can be moved, the world can
/// be reloaded and the job can be re-issued between the tick that planned and
/// the tick that acts. A plan carries the area revision and the world epoch it
/// was built from, and <see cref="IsStale"/> is asked before every step, not
/// once at the start. The repository already has exactly one type that does
/// this - the undesignation plan, which carries the journal instance and
/// sequence it was worked out against and is refused if either moved - and it is
/// the right shape.
///
/// <b>What it guarantees about the manifest.</b> That the plan and the total it
/// was provisioned for travel together. A plan whose steps were re-derived
/// against a different manifest is a different plan, and comparing the two is
/// how an interruption decides between carrying on and planning again.
///
/// <b>What it is not.</b> Not progress, not custody, not a promise. It records
/// no completion and holds no material. Which steps are done lives in the job
/// runtime; what is actually held lives in a ledger. A plan that also tracked
/// progress would be a second source of truth about both.</summary>
internal readonly struct JobPlan
{
    private readonly JobStep[]? _steps;

    internal JobPlan(
        JobPlanVerdict verdict,
        JobManifest manifest,
        IReadOnlyList<JobStep>? steps,
        int areaRevision,
        NpcWorldEpoch epoch,
        string reason)
    {
        Verdict = verdict;
        Manifest = manifest;
        AreaRevision = areaRevision;
        Epoch = epoch;
        Reason = reason ?? string.Empty;

        if (steps == null || steps.Count == 0)
        {
            _steps = null;
        }
        else
        {
            var copy = new JobStep[steps.Count];
            for (int index = 0; index < steps.Count; index++)
            {
                copy[index] = steps[index];
            }

            _steps = copy;
        }
    }

    /// <summary>Why this plan is what it is.</summary>
    internal JobPlanVerdict Verdict { get; }

    /// <summary>Everything the whole job needs.</summary>
    internal JobManifest Manifest { get; }

    /// <summary>The steps, in order. Empty for every verdict but
    /// <see cref="JobPlanVerdict.Planned"/>.</summary>
    internal IReadOnlyList<JobStep> Steps => _steps ?? Array.Empty<JobStep>();

    /// <summary>The work area's revision when this was planned.</summary>
    internal int AreaRevision { get; }

    /// <summary>The world load this was planned in. Every in-session id inside
    /// the steps belongs to it and to no other.</summary>
    internal NpcWorldEpoch Epoch { get; }

    /// <summary>Why it was refused, for a player sentence. Empty when planned.
    ///
    /// <b>The verdict is the control-flow answer; this is the sentence.</b> A
    /// role branches on <see cref="Verdict"/> - retry, walk, finish, or stop and
    /// tell somebody - and shows <i>this</i>. One verdict deliberately covers
    /// more than one situation (see
    /// <see cref="JobPlanVerdict.ShortOfMaterial"/>), so a role that renders the
    /// verdict's own name will tell a player they are out of wood while they are
    /// standing on it.</summary>
    internal string Reason { get; }

    /// <summary>Whether this plan may still be acted on. True only for a
    /// <see cref="JobPlanVerdict.Planned"/> plan with steps in it: a verdict of
    /// nothing-to-do is a finished job, not something to walk through.</summary>
    internal bool IsActionable => Verdict == JobPlanVerdict.Planned && Steps.Count > 0;

    /// <summary>Whether the world has moved under this plan. Asked before every
    /// step. An unknown current epoch is stale, not fresh - and so is anything
    /// that is not a plan, because a refusal carries no area revision and would
    /// otherwise report itself fresh against a caller that happened to pass
    /// zero.</summary>
    internal bool IsStale(int currentAreaRevision, NpcWorldEpoch currentEpoch) =>
        Verdict != JobPlanVerdict.Planned
        || currentAreaRevision != AreaRevision
        || !Epoch.Matches(currentEpoch);

    /// <summary>A plan that is not one, carrying why.</summary>
    internal static JobPlan Refused(JobPlanVerdict verdict, string reason, NpcWorldEpoch epoch)
    {
        if (verdict == JobPlanVerdict.Planned || verdict == JobPlanVerdict.Unspecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verdict), "A refusal needs a verdict that explains it.");
        }

        return new JobPlan(verdict, JobManifest.Empty, null, 0, epoch, reason);
    }
}
