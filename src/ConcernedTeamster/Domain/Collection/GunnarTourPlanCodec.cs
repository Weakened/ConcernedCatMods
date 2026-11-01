using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Where Gunnar's collection-tour plan lives (#379): one file per
/// world, beside Teamster's other per-world sidecars and named the same way, on
/// this computer only.
///
/// <b>The role's answer, because the library composes no path.</b> ConcernedNPC
/// takes an absolute path and invents none (<c>NpcPlanJournal.TryOpen</c>), so
/// the name below is part of Teamster's durable contract exactly like the death
/// record beside it (<c>teamster_worker_deaths_&lt;world&gt;.tsv</c>).
///
/// <b>No world, no file.</b> A world whose id is unknown or zero has no plan
/// file at all, and a tour refuses to start rather than writing one somewhere a
/// later world would find it.</summary>
internal static class GunnarTourPlanFiles
{
    internal const string Prefix = "teamster_gunnar_tour_";
    internal const string Extension = ".tsv";

    internal static string FileNameFor(long worldUid) =>
        Prefix + worldUid.ToString(CultureInfo.InvariantCulture) + Extension;

    /// <summary>The absolute path for one world's plan, or null with a reason
    /// when there is no world to name or no directory to put it in.</summary>
    internal static string? PathFor(string? directory, long? worldUid, out string reason)
    {
        if (worldUid == null || worldUid.Value == 0L)
        {
            reason = "no world could be named, so there is nowhere to write a tour plan down";
            return null;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            reason = "Teamster's data folder could not be found, so there is nowhere to write a tour plan down";
            return null;
        }

        try
        {
            reason = string.Empty;
            return Path.Combine(Path.GetFullPath(directory), FileNameFor(worldUid.Value));
        }
        catch (Exception exception)
        {
            reason = "Teamster's data folder is not a usable path (" + exception.GetType().Name + ")";
            return null;
        }
    }
}

/// <summary>The bytes of Gunnar's tour plan (#379). <b>Teamster's, not the
/// library's</b>: ConcernedNPC owns when a plan is written and what a reloaded
/// one may believe, and owns none of these tags, their order, the version or
/// the trailer (<c>INpcPlanCodec</c>).
///
/// <b>The rows.</b> A comment header; <c>format 1</c>; one <c>plan</c> row; one
/// <c>carried</c> row per material he holds; and the trailer <c>end n</c>, where
/// n is the number of rows before it that are not comments. Tab-separated.
///
/// <b>No escaping, so keys must not need it.</b> Teamster does not compile the
/// settlement layer's escaping helper, and inventing a second escaping scheme
/// for one file is a second thing to get wrong. So a key, an identity, a job
/// name or a material name containing a tab or a line break is <b>refused</b>
/// rather than written - a plan that cannot be written does not start, which is
/// the failing direction. The note is free text a person reads and is never
/// parsed back, so its tabs and line breaks become spaces instead.
///
/// <b>Everything it cannot account for is unreadable, never "no plan".</b> An
/// unknown row, a second format or plan row, a duplicate material, rows after
/// the trailer, a missing or wrong trailer, another identity or job, and a
/// finished plan that still claims to be holding something (a reservation, a
/// load or a vehicle) all refuse the whole record. Reading part of a custody
/// record would be choosing which half of a player's material to forget.
///
/// <b>Two fields a tour never fills, refused rather than ignored.</b> A tour
/// reserves nothing, so there is no reservation row and a state that has one is
/// not written. A tour is on foot (<c>DECISIONS.md</c> D17), so a plan naming a
/// vehicle is neither written nor read.</summary>
internal sealed class GunnarTourPlanCodec : INpcPlanCodec
{
    internal const string Header = "# Concerned Teamster: Gunnar's collection tour plan. One world. Do not edit.";
    internal const string FormatTag = "format";
    internal const string FormatVersion = "1";
    internal const string PlanTag = "plan";
    internal const string CarriedTag = "carried";
    internal const string TrailerTag = "end";

    private const int PlanFields = 12;
    private const int CarriedFields = 5;

    private readonly NpcIdentity _identity;
    private readonly string _jobId;

    internal GunnarTourPlanCodec(NpcIdentity identity, string jobId)
    {
        if (identity.IsEmpty)
        {
            throw new ArgumentException("A plan codec needs Gunnar's identity.", nameof(identity));
        }

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentException("A plan codec needs the tour's job id.", nameof(jobId));
        }

        _identity = identity;
        _jobId = jobId;
    }

    public IReadOnlyList<string>? Encode(NpcPlanState state)
    {
        if (state == null
            || !state.Identity.Equals(_identity)
            || !string.Equals(state.JobId, _jobId, StringComparison.Ordinal)
            || state.Reservations.Count != 0
            || state.VehicleKey.Length != 0
            || !IsKey(state.Identity.Value)
            || !IsKey(state.JobId)
            || !IsKey(state.SourceKey)
            || !IsKey(state.DestinationKey))
        {
            return null;
        }

        if (IsFinished(state.Phase) && state.Carried.Count != 0)
        {
            // The same refusal the reader makes, made first: a finished plan
            // that still claims a load is never written, so "refunded" can never
            // become the way a load stops being anybody's.
            return null;
        }

        var rows = new List<string>
        {
            Header,
            FormatTag + "\t" + FormatVersion,
            string.Join(
                "\t",
                new[]
                {
                    PlanTag,
                    state.Identity.Value,
                    state.JobId,
                    ((int)state.Phase).ToString(CultureInfo.InvariantCulture),
                    ((int)state.Custody).ToString(CultureInfo.InvariantCulture),
                    state.TargetsDone.ToString(CultureInfo.InvariantCulture),
                    state.TargetsTotal.ToString(CultureInfo.InvariantCulture),
                    state.SourceKey,
                    state.DestinationKey,
                    state.VehicleKey,
                    state.Attempt.ToString(CultureInfo.InvariantCulture),
                    Flatten(state.Note),
                }),
        };

        var seen = new HashSet<NpcMaterial>();
        foreach (NpcMaterialStack stack in state.Carried)
        {
            if (!stack.IsValid || !IsKey(stack.Material.ItemName) || !seen.Add(stack.Material))
            {
                return null;
            }

            rows.Add(string.Join(
                "\t",
                new[]
                {
                    CarriedTag,
                    stack.Material.ItemName,
                    stack.Material.Quality.ToString(CultureInfo.InvariantCulture),
                    stack.Material.Variant.ToString(CultureInfo.InvariantCulture),
                    stack.Count.ToString(CultureInfo.InvariantCulture),
                }));
        }

        // Every row but the comment header is counted.
        rows.Add(TrailerTag + "\t" + (rows.Count - 1).ToString(CultureInfo.InvariantCulture));
        return rows;
    }

    public bool TryDecode(IReadOnlyList<string> lines, out NpcPlanState? state, out string reason)
    {
        state = null;
        reason = string.Empty;
        if (lines == null)
        {
            reason = "there were no plan rows";
            return false;
        }

        bool sawFormat = false;
        bool sawTrailer = false;
        int counted = 0;
        NpcPlanState? plan = null;
        var carried = new List<NpcMaterialStack>();
        var seen = new HashSet<NpcMaterial>();

        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (sawTrailer)
            {
                reason = "the plan has rows after its end row";
                return false;
            }

            string[] fields = line.Split('\t');
            switch (fields[0])
            {
                case FormatTag:
                    if (sawFormat || fields.Length != 2
                        || !string.Equals(fields[1], FormatVersion, StringComparison.Ordinal))
                    {
                        reason = "the plan's format row is not one this build writes";
                        return false;
                    }

                    sawFormat = true;
                    counted++;
                    break;

                case PlanTag:
                    if (plan != null)
                    {
                        reason = "the file holds more than one plan row";
                        return false;
                    }

                    if (!TryReadPlan(fields, out plan, out reason))
                    {
                        return false;
                    }

                    counted++;
                    break;

                case CarriedTag:
                    if (!TryReadCarried(fields, out NpcMaterialStack stack) || !seen.Add(stack.Material))
                    {
                        reason = "a carried row is invalid or names a material twice";
                        return false;
                    }

                    carried.Add(stack);
                    counted++;
                    break;

                case TrailerTag:
                    if (fields.Length != 2
                        || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int declared)
                        || declared != counted)
                    {
                        reason = "the plan's end row does not match the rows above it, so the file is incomplete";
                        return false;
                    }

                    sawTrailer = true;
                    break;

                default:
                    reason = "the plan holds a row this build does not know (" + Flatten(fields[0]) + ")";
                    return false;
            }
        }

        if (!sawFormat || plan == null)
        {
            reason = "the plan is missing its format row or its plan row";
            return false;
        }

        if (!sawTrailer)
        {
            reason = "the plan has no end row, so it cannot be told from a file that was cut short";
            return false;
        }

        if (IsFinished(plan.Phase) && carried.Count != 0)
        {
            reason = "a finished plan still claims he is carrying something";
            return false;
        }

        state = plan.WithHoldings(null, carried);
        return true;
    }

    /// <summary>Whether a key can be written and read back unchanged: no tab
    /// and no line break. Empty is allowed - an unset source or destination is
    /// written as an empty field.</summary>
    internal static bool IsKey(string? value)
    {
        if (value == null)
        {
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (c == '\t' || c == '\r' || c == '\n')
            {
                return false;
            }
        }

        return true;
    }

    private bool TryReadPlan(string[] fields, out NpcPlanState? plan, out string reason)
    {
        plan = null;
        reason = string.Empty;
        if (fields.Length != PlanFields
            || !NpcIdentity.TryParse(fields[1], out NpcIdentity identity)
            || !identity.Equals(_identity)
            || !string.Equals(fields[2], _jobId, StringComparison.Ordinal)
            || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int phase)
            || !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out int custody)
            || !Enum.IsDefined(typeof(NpcPlanPhase), phase)
            || !Enum.IsDefined(typeof(NpcPlanCustody), custody)
            || !TryCount(fields[5], out int done)
            || !TryCount(fields[6], out int total)
            || !TryCount(fields[10], out int attempt))
        {
            reason = "the plan row is not one this build writes, or belongs to someone else";
            return false;
        }

        if (fields[9].Length != 0)
        {
            reason = "the plan names a vehicle, and a collection tour is on foot";
            return false;
        }

        plan = new NpcPlanState(
            identity,
            _jobId,
            (NpcPlanPhase)phase,
            (NpcPlanCustody)custody,
            null,
            null,
            done,
            total,
            fields[7],
            fields[8],
            string.Empty,
            NpcWorldEpoch.Unknown,
            attempt,
            fields[11]);
        return true;
    }

    private static bool TryReadCarried(string[] fields, out NpcMaterialStack stack)
    {
        stack = default;
        if (fields.Length != CarriedFields
            || fields[1].Length == 0
            || !TryCount(fields[2], out int quality) || quality < 1
            || !TryCount(fields[3], out int variant)
            || !TryCount(fields[4], out int count) || count < 1)
        {
            return false;
        }

        stack = new NpcMaterialStack(new NpcMaterial(fields[1], quality, variant), count);
        return stack.IsValid;
    }

    private static bool TryCount(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static bool IsFinished(NpcPlanPhase phase) =>
        phase == NpcPlanPhase.Settled || phase == NpcPlanPhase.Refunded;

    private static string Flatten(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text!.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
