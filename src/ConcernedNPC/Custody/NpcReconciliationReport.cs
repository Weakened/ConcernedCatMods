using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>What reconciliation found, in the order it looked.</summary>
internal sealed class NpcReconciliationReport
{
    private readonly IReadOnlyList<NpcReconciliationFinding> _findings;

    internal NpcReconciliationReport(IReadOnlyList<NpcReconciliationFinding>? findings, bool jobsWereRunning)
    {
        _findings = findings ?? Array.Empty<NpcReconciliationFinding>();
        JobsWereRunning = jobsWereRunning;
    }

    internal IReadOnlyList<NpcReconciliationFinding> Findings => _findings;

    /// <summary>Whether anything was in flight when the session ended. A
    /// shortfall with nothing running is likelier to be the player having taken
    /// the material than a failure, and a role that says so is more use than
    /// one that reports a fault.</summary>
    internal bool JobsWereRunning { get; }

    internal bool AllMatch
    {
        get
        {
            foreach (NpcReconciliationFinding finding in _findings)
            {
                if (finding.NeedsAttention)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The findings that concern one job, in order.</summary>
    internal IReadOnlyList<NpcReconciliationFinding> For(string? jobId)
    {
        var list = new List<NpcReconciliationFinding>();
        foreach (NpcReconciliationFinding finding in _findings)
        {
            if (finding.Concerns(jobId))
            {
                list.Add(finding);
            }
        }

        return list;
    }

    /// <summary>The one finding a job should be judged on, or null when nothing
    /// concerns it.
    ///
    /// <b>An uncertain transfer outranks a count mismatch</b>, because the
    /// mismatch may well <i>be</i> that transfer, and asking a player about the
    /// same material twice under two different descriptions is how a report
    /// stops being believed.</summary>
    internal NpcReconciliationFinding? Foremost(string? jobId)
    {
        NpcReconciliationFinding? best = null;
        foreach (NpcReconciliationFinding finding in _findings)
        {
            if (!finding.NeedsAttention || !finding.Concerns(jobId))
            {
                continue;
            }

            if (!finding.Request.IsEmpty)
            {
                return finding;
            }

            if (best == null)
            {
                best = finding;
            }
        }

        return best;
    }

    /// <summary>Shortfalls a role could offer to record as lost, capped at what
    /// the record says is there.</summary>
    internal IReadOnlyList<NpcReconciliationFinding> ShortfallsFor(string? jobId)
    {
        var list = new List<NpcReconciliationFinding>();
        foreach (NpcReconciliationFinding finding in _findings)
        {
            if (finding.Kind == NpcReconciliationFindingKind.BelowExpected && finding.Concerns(jobId))
            {
                list.Add(finding);
            }
        }

        return list;
    }
}
