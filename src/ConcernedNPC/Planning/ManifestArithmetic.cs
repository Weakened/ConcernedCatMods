using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>The arithmetic that turns a list of targets into one total, and one
/// total into what is missing.
///
/// <b>This is the whole of "understand the job before acting".</b> Every loop
/// this package replaces works out what it needs one tick at a time from
/// whatever is in front of it, and that is why it walks to the chest eight
/// times: it never asked how many nails the whole wall takes. Totalling the
/// targets once, before anything is reserved or picked up, is the difference,
/// and everything downstream - provisioning in one trip, refusing an impossible
/// job before it starts, telling a player what is short - follows from having
/// the number.
///
/// <b>Order is part of the answer.</b> Items come out in the order they were
/// first seen across the targets, never sorted and never hashed. Two runs over
/// the same targets produce the same manifest line for line, which is what lets
/// a plan be compared against the manifest it was computed from, and what lets
/// a test assert a route rather than a set.
///
/// <b>It is requirement arithmetic only.</b> Nothing here knows what has been
/// picked up, what is reserved or what has been delivered. A manifest is what
/// the job needs and never what anybody has - the moment it tracked progress it
/// would be a second source of truth about custody, and the two would
/// drift.</summary>
internal static class ManifestArithmetic
{
    /// <summary>Everything a set of targets needs, added up. The job
    /// manifest.</summary>
    internal static JobManifest Total(IReadOnlyList<JobTarget>? targets)
    {
        if (targets == null || targets.Count == 0)
        {
            return JobManifest.Empty;
        }

        var order = new List<string>();
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JobTarget target in targets)
        {
            Accumulate(target.Needs, order, totals);
        }

        return Build(order, totals);
    }

    /// <summary>Several manifests added together.</summary>
    internal static JobManifest Merge(IReadOnlyList<JobManifest>? manifests)
    {
        if (manifests == null || manifests.Count == 0)
        {
            return JobManifest.Empty;
        }

        var order = new List<string>();
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JobManifest manifest in manifests)
        {
            Accumulate(manifest, order, totals);
        }

        return Build(order, totals);
    }

    /// <summary>What is left of <paramref name="wanted"/> once
    /// <paramref name="covered"/> has been taken off it. Clamped at zero and
    /// with emptied lines dropped, because a line asking for nothing is a line
    /// that should not have been written - and a negative one would make a
    /// surplus look like a need on the next subtraction.</summary>
    internal static JobManifest Subtract(JobManifest wanted, JobManifest covered)
    {
        if (wanted.IsEmpty)
        {
            return JobManifest.Empty;
        }

        var lines = new List<JobManifestLine>();
        foreach (JobManifestLine line in wanted.Lines)
        {
            int left = line.Required - covered.RequiredOf(line.Item);
            if (left > 0)
            {
                lines.Add(new JobManifestLine(line.Item, left));
            }
        }

        return new JobManifest(lines);
    }

    /// <summary>What a set of containers cannot supply of a manifest. Empty when
    /// they can supply all of it.
    ///
    /// <b>Only containers that may be used right now count.</b> A chest the
    /// player has not enabled for NPCs, one behind a ward, one somebody has open
    /// - none of them is material, and counting them would have the NPC start a
    /// job it cannot provision and discover it four stops in.
    ///
    /// <b>And only units no other job has set aside.</b> Units another job holds
    /// are not this job's material either, and counting them is how two plans
    /// are built on one pile - see
    /// <see cref="INpcSourceAvailability"/>.
    ///
    /// <b><paramref name="carrying"/> is required and has no default.</b> A
    /// round that could not fully provision one trip leaves the NPC holding the
    /// surplus, and the round after it would otherwise tell the player the
    /// settlement is short of material that is on the NPC's back. That case
    /// stopped being rare the day a chest-capped trip started shrinking rather
    /// than refusing, so the parameter is one a caller has to answer rather
    /// than one it can forget: <see cref="JobManifest.Empty"/> is the honest
    /// answer for a first round and a false one for any round after a partial
    /// one.</summary>
    internal static JobManifest Shortfall(
        JobManifest wanted,
        JobManifest carrying,
        IReadOnlyList<SourceStock>? sources,
        INpcSourceAvailability? availability = null)
    {
        if (wanted.IsEmpty)
        {
            return JobManifest.Empty;
        }

        JobManifest outstanding = carrying.IsEmpty ? wanted : Subtract(wanted, carrying);
        if (outstanding.IsEmpty)
        {
            // He is already holding all of it. Nothing about the chests can make
            // that a shortage.
            return JobManifest.Empty;
        }

        var lines = new List<JobManifestLine>();
        foreach (JobManifestLine line in outstanding.Lines)
        {
            int available = 0;
            if (sources != null)
            {
                foreach (SourceStock source in sources)
                {
                    if (source.IsUsable)
                    {
                        available += source.UnitsOf(line.Item, availability);
                    }
                }
            }

            int missing = line.Required - available;
            if (missing > 0)
            {
                lines.Add(new JobManifestLine(line.Item, missing));
            }
        }

        return new JobManifest(lines);
    }

    /// <summary>Whether <paramref name="wanted"/> asks for more of any item than
    /// <paramref name="allowed"/> permits.
    ///
    /// <b>The check that stops a snapshot over-withdrawing from a player's
    /// chest.</b> A role hands in what it says the job is for; the targets in
    /// the snapshot are what the job turns out to need. When the second is
    /// larger than the first the two disagree about the job, and provisioning
    /// for the larger number takes a player's material for something nobody
    /// asked for. So it is refused rather than clamped: clamping would silently
    /// under-provision half the targets and the NPC would find out at the far
    /// end of the round.</summary>
    internal static bool Exceeds(JobManifest wanted, JobManifest allowed, out JobManifestLine over)
    {
        over = default;
        if (allowed.IsEmpty)
        {
            return false;
        }

        foreach (JobManifestLine line in wanted.Lines)
        {
            int permitted = allowed.RequiredOf(line.Item);
            if (line.Required > permitted)
            {
                over = new JobManifestLine(line.Item, line.Required - permitted);
                return true;
            }
        }

        return false;
    }

    /// <summary>A manifest as a sentence a player could read: "40 wood, 12
    /// nails". Empty for an empty manifest.</summary>
    internal static string Describe(JobManifest manifest)
    {
        if (manifest.IsEmpty)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        foreach (JobManifestLine line in manifest.Lines)
        {
            if (text.Length != 0)
            {
                text.Append(", ");
            }

            text.Append(line.Required).Append(' ').Append(line.Item);
        }

        return text.ToString();
    }

    private static void Accumulate(JobManifest manifest, List<string> order, Dictionary<string, int> totals)
    {
        foreach (JobManifestLine line in manifest.Lines)
        {
            if (!totals.TryGetValue(line.Item, out int running))
            {
                order.Add(line.Item);
            }

            totals[line.Item] = running + line.Required;
        }
    }

    private static JobManifest Build(List<string> order, Dictionary<string, int> totals)
    {
        var lines = new List<JobManifestLine>(order.Count);
        foreach (string item in order)
        {
            lines.Add(new JobManifestLine(item, totals[item]));
        }

        return new JobManifest(lines);
    }
}
