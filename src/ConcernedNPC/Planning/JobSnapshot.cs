using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>Everything one pass of looking found, frozen.
///
/// <b>Frozen is the point.</b> A plan is a pure function of what it was given,
/// and it can only be that if what it was given stops changing. So a snapshot is
/// taken once, copied in, and handed to the planner - not a live view of the
/// world that the planner reads twice and gets two answers from. That is also
/// what makes the same inputs give the same plan in a test without the game
/// installed and in a session with it.
///
/// <b>It carries how it was taken, not only what it found.</b>
/// <see cref="Report"/> is the difference between "there is nothing there" and
/// "I did not finish looking", and it is the only thing standing between a
/// player and an NPC that reports a job finished because a zone had not streamed
/// in. Ask <see cref="IsConclusive"/> before believing an empty one.</summary>
internal readonly struct JobSnapshot
{
    private readonly JobTarget[]? _targets;
    private readonly SourceStock[]? _sources;

    internal JobSnapshot(
        IReadOnlyList<JobTarget>? targets,
        IReadOnlyList<SourceStock>? sources,
        AreaScanReport report,
        int areaRevision,
        NpcWorldEpoch epoch)
    {
        Report = report;
        AreaRevision = areaRevision;
        Epoch = epoch;

        if (targets == null || targets.Count == 0)
        {
            _targets = null;
        }
        else
        {
            var copy = new JobTarget[targets.Count];
            for (int index = 0; index < targets.Count; index++)
            {
                copy[index] = targets[index];
            }

            _targets = copy;
        }

        if (sources == null || sources.Count == 0)
        {
            _sources = null;
        }
        else
        {
            var copy = new SourceStock[sources.Count];
            for (int index = 0; index < sources.Count; index++)
            {
                copy[index] = sources[index];
            }

            _sources = copy;
        }
    }

    /// <summary>The targets worth planning for, in the order they were found.
    /// </summary>
    internal IReadOnlyList<JobTarget> Targets => _targets ?? Array.Empty<JobTarget>();

    /// <summary>The containers that were offered, whether or not they may be
    /// used - permission is asked again at the moment of use, never cached into
    /// a snapshot.</summary>
    internal IReadOnlyList<SourceStock> Sources => _sources ?? Array.Empty<SourceStock>();

    /// <summary>What the pass actually did. The evidence behind every "there is
    /// nothing to do".</summary>
    internal AreaScanReport Report { get; }

    /// <summary>The work area's revision when this was taken.</summary>
    internal int AreaRevision { get; }

    /// <summary>The world load this was taken in.</summary>
    internal NpcWorldEpoch Epoch { get; }

    /// <summary>Whether an empty snapshot proves anything. False whenever a zone
    /// was unloaded, the budget ran out, or the counts do not add up.</summary>
    internal bool IsConclusive => Report.IsConclusive;

    /// <summary>Everything the targets in here need, added up once. <b>This is
    /// the manifest</b>, and it exists before a single step is planned.</summary>
    internal JobManifest Wanted => ManifestArithmetic.Total(Targets);
}

/// <summary>Taking one bounded look at the world.
///
/// <b>Bounded, and bounded in the way the world can honestly answer.</b> Nothing
/// in the host sweeps an area for standable ground; it answers "is this point
/// loaded" and "may I stand here" one point at a time. So this walks a list of
/// candidates the role has already narrowed, spends a budget asking about each
/// one, and stops when the budget is gone - reporting that it stopped.
///
/// <b>Three things are asked about each candidate, in this order.</b> Is it
/// inside the work area, which is a question about permission and is
/// authoritative - nothing widens it, nothing falls back to anywhere. Is it
/// still worth doing, which is the role's completion condition and the one place
/// a role's meaning enters. And is there ground to stand on, which is a fact
/// about the world and is asked of the probe. Confusing the first and the third
/// is how an NPC refuses to work in a marked area because a zone had not
/// streamed in, so they are separate questions with separate answers.
///
/// <b>Cost.</b> At most two budget units per candidate and one per container
/// offered, so a pass costs what the caller allowed and not a unit more. There
/// is no loop here that runs until it finds something.</summary>
internal static class JobSnapshotBuilder
{
    /// <summary>Looks once.</summary>
    /// <param name="area">Where work is allowed. Null is refused, never widened:
    /// the report comes back <see cref="AreaScanOutcome.AreaInvalid"/> and the
    /// snapshot is empty.</param>
    /// <param name="epoch">The world load this pass belongs to. A candidate from
    /// another load is refused rather than resolved.</param>
    /// <param name="candidates">What the role thinks might be worth doing.</param>
    /// <param name="offered">The containers the role is willing to let this job
    /// draw from.</param>
    /// <param name="observer">The role's completion condition, or null - in
    /// which case nothing is known about any candidate and the pass reports that
    /// rather than assuming they are all good.</param>
    /// <param name="probe">The ground, or null to take the candidates' own
    /// places on trust. A role whose targets are world objects it has just read
    /// has already proved they exist; a role proposing places has not.</param>
    /// <param name="budget">How much looking is allowed.</param>
    internal static JobSnapshot Take(
        INpcWorkArea? area,
        NpcWorldEpoch epoch,
        IReadOnlyList<JobTarget>? candidates,
        IReadOnlyList<SourceStock>? offered,
        IStopObserver? observer,
        INpcAreaProbe? probe,
        PlanningBudget budget)
    {
        if (budget == null)
        {
            throw new ArgumentNullException(nameof(budget));
        }

        if (area == null || epoch.IsUnknown)
        {
            // Fail closed. A null area is not "anywhere", and a pass with no
            // world is a pass whose every name points at nothing.
            return new JobSnapshot(
                null, null, new AreaScanReport(AreaScanOutcome.AreaInvalid, 0, 0, 0, 0, false), 0, epoch);
        }

        var kept = new List<JobTarget>();
        int examined = 0;
        int notLoaded = 0;
        int rejected = 0;
        int exhausted = 0;
        int unreadable = 0;
        bool truncated = false;

        if (candidates != null)
        {
            foreach (JobTarget candidate in candidates)
            {
                if (!budget.TrySpend())
                {
                    truncated = true;
                    break;
                }

                examined++;

                if (!candidate.IsValid || !epoch.Matches(candidate.Epoch) || !area.Contains(candidate.At))
                {
                    rejected++;
                    continue;
                }

                StopStatus status = Look(observer, candidate);
                if (status == StopStatus.AlreadyDone)
                {
                    exhausted++;
                    continue;
                }

                if (status == StopStatus.Unreadable)
                {
                    // Unknown, never empty - which is what stops an unfinished
                    // look being reported as a finished job. Counted apart from
                    // ground nobody has reached because the two are different
                    // problems wearing the same face: a role whose completion
                    // condition cannot answer is a defect that will still be
                    // there tomorrow, and telling the player to walk over there
                    // fixes nothing at all.
                    unreadable++;
                    continue;
                }

                if (status != StopStatus.Actionable)
                {
                    rejected++;
                    continue;
                }

                if (probe == null)
                {
                    kept.Add(candidate);
                    continue;
                }

                if (!budget.TrySpend())
                {
                    truncated = true;
                    break;
                }

                AreaSample sample = Sample(probe, candidate.At);
                if (sample.Verdict == AreaSampleVerdict.Standable)
                {
                    kept.Add(new JobTarget(
                        candidate.Key,
                        candidate.Epoch,
                        sample.Ground,
                        candidate.Action,
                        candidate.Priority,
                        candidate.Needs));
                    continue;
                }

                if (sample.Verdict == AreaSampleVerdict.Rejected)
                {
                    rejected++;
                    continue;
                }

                if (sample.Verdict == AreaSampleVerdict.NotLoaded)
                {
                    notLoaded++;
                    continue;
                }

                // The probe could not answer. Unknown like unloaded ground, and
                // it weighs the same on the conclusion - neither may ever add
                // up to "there is nothing there" - but it is counted apart,
                // because walking over there is the fix for one of them and no
                // part of the fix for the other.
                unreadable++;
            }
        }

        var sources = new List<SourceStock>();
        if (offered != null)
        {
            foreach (SourceStock source in offered)
            {
                if (!budget.TrySpend())
                {
                    truncated = true;
                    break;
                }

                if (source.Container == null || !epoch.Matches(source.Epoch))
                {
                    continue;
                }

                sources.Add(source);
            }
        }

        var report = new AreaScanReport(
            Outcome(kept.Count, notLoaded + unreadable, rejected, exhausted, truncated),
            examined,
            notLoaded,
            rejected,
            exhausted,
            truncated,
            unreadable);

        return new JobSnapshot(kept, sources, report, area.Revision, epoch);
    }

    /// <summary>The one answer, derived from the counts rather than guessed.
    ///
    /// Found comes first because something usable having been found is true
    /// whatever else happened; a truncated pass that found work still has work.
    /// <see cref="AreaScanReport.IsConclusive"/> is what stops that being read as
    /// a finished job, and it is false for anything truncated, unloaded or
    /// unreadable whatever this says.
    ///
    /// <b><paramref name="unknown"/> is unloaded ground and an unanswerable
    /// probe added together, and that is deliberate.</b> The two are counted
    /// apart on the report because their fixes differ, and they weigh the same
    /// here because the conclusion they bear on is the same one: neither may
    /// ever add up to "there is nothing here".</summary>
    private static AreaScanOutcome Outcome(int kept, int unknown, int rejected, int exhausted, bool truncated)
    {
        if (kept > 0)
        {
            return AreaScanOutcome.Found;
        }

        if (truncated)
        {
            return AreaScanOutcome.Incomplete;
        }

        if (unknown > 0)
        {
            return AreaScanOutcome.NotLoaded;
        }

        if (exhausted > 0)
        {
            return AreaScanOutcome.Exhausted;
        }

        _ = rejected;
        return AreaScanOutcome.Empty;
    }

    private static StopStatus Look(IStopObserver? observer, in JobTarget target)
    {
        if (observer == null)
        {
            return StopStatus.Unreadable;
        }

        try
        {
            return observer.Observe(target.AsStop());
        }
        catch (Exception)
        {
            // A role's broken completion condition is a broken role, not a
            // broken NPC in somebody else's product.
            return StopStatus.Unreadable;
        }
    }

    private static AreaSample Sample(INpcAreaProbe probe, NpcPoint at)
    {
        try
        {
            return probe.Probe(at);
        }
        catch (Exception)
        {
            return new AreaSample(AreaSampleVerdict.Unreadable, default, AreaRejection.None);
        }
    }
}
