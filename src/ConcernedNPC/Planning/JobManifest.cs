using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>One line of a job's requirement: how much of one thing the whole job
/// needs.
///
/// <see cref="Item"/> is opaque here - a role's own token for a material, a tool,
/// a fuel. This library never learns that one of them is wood.</summary>
public readonly struct JobManifestLine
{
    public JobManifestLine(string item, int required)
    {
        Item = item ?? string.Empty;
        Required = required;
    }

    /// <summary>The role's token for the thing needed.</summary>
    public string Item { get; }

    /// <summary>How many units of it the whole job needs. Never negative;
    /// zero means the line should not have been written.</summary>
    public int Required { get; }

    public bool IsValid => !string.IsNullOrEmpty(Item) && Required > 0;

    public override string ToString() => Required + " " + Item;
}

/// <summary>Everything one job needs, totalled before any of it is started.
///
/// <b>What it guarantees: that the NPC understands the whole job first.</b>
/// Every loop in the shipped products recomputes what it needs one tick at a
/// time, from whatever is in front of it. That works and it produces an NPC that
/// walks to the chest eight times because it never asked how many nails the
/// whole wall takes. A manifest is the total, computed once, so a job can be
/// provisioned in batches, can be refused up front for being impossible, and can
/// tell a player what it is short of before it starts rather than after four
/// trips.
///
/// <b>What it guarantees about honesty.</b> That the total is a requirement and
/// never a promise. A manifest says what the job needs; it says nothing about
/// what is available, what is reserved, or what has been delivered. Those live
/// in a ledger, separately, because the moment a plan starts tracking progress
/// it becomes a second source of truth about custody and the two drift.
///
/// <b>Immutable by construction.</b> The lines are copied in. A manifest handed
/// to a planner cannot be edited behind it, which is what lets a plan be
/// compared against the manifest it was computed from.</summary>
public readonly struct JobManifest
{
    private readonly JobManifestLine[]? _lines;

    public JobManifest(IEnumerable<JobManifestLine>? lines)
    {
        if (lines == null)
        {
            _lines = null;
            return;
        }

        var kept = new List<JobManifestLine>();
        foreach (JobManifestLine line in lines)
        {
            if (line.IsValid)
            {
                kept.Add(line);
            }
        }

        _lines = kept.Count == 0 ? null : kept.ToArray();
    }

    /// <summary>A job that needs nothing. Not the same as a job with nothing to
    /// do.</summary>
    public static JobManifest Empty => default;

    /// <summary>The lines, in the order they were given. Never null.</summary>
    public IReadOnlyList<JobManifestLine> Lines => _lines ?? Array.Empty<JobManifestLine>();

    public bool IsEmpty => _lines == null;

    /// <summary>Every unit of every line added together. For "he will need to
    /// carry this much", not for any accounting.</summary>
    public int TotalUnits
    {
        get
        {
            int total = 0;
            foreach (JobManifestLine line in Lines)
            {
                total += line.Required;
            }

            return total;
        }
    }

    /// <summary>How much of one item this job needs, or zero.</summary>
    public int RequiredOf(string? item)
    {
        if (string.IsNullOrEmpty(item))
        {
            return 0;
        }

        int total = 0;
        foreach (JobManifestLine line in Lines)
        {
            if (string.Equals(line.Item, item, StringComparison.Ordinal))
            {
                // Summed rather than first-wins: a caller that supplies two
                // lines for one item means both, and silently dropping one
                // under-provisions the job.
                total += line.Required;
            }
        }

        return total;
    }
}
