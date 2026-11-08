using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Storage;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Where Thorstein's shared-interruption plans live (#379): one file
/// per settlement and job, beside the settlement's own record, on this computer
/// only. The library composes no path, so the name is part of Foreman's durable
/// contract. A new file name, never a new row in the settlement journal: the
/// marker-backed journal (CONTRACTS.md §5.5) stays exactly what it was.</summary>
internal sealed class ThorsteinPlanFiles
{
    private const string Infix = ".thorstein-";
    private const string Extension = ".plan.tsv";

    private readonly string _root;

    internal ThorsteinPlanFiles(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Thorstein's plan root is required.", nameof(root));
        }

        _root = Path.GetFullPath(root);
    }

    internal string ResolvePath(SettlementScope scope, string jobId) =>
        Path.Combine(_root, scope.ToStorageKey() + Infix + jobId + Extension);
}

/// <summary>The bytes of one of Thorstein's plans. ConcernedNPC owns the write
/// ordering and the recovery rules; this owns the tags, their order and the
/// trailer. A malformed row refuses the whole file, because reading half a
/// custody record is deciding which half of a player's material to forget.
/// </summary>
internal sealed class ThorsteinPlanCodec : INpcPlanCodec
{
    private const string Header = "# Concerned Foreman: Thorstein's plan. One settlement, one job.";
    private const string FormatTag = "format";
    private const string FormatVersion = "1";
    private const string PlanTag = "plan";
    private const string CarriedTag = "carried";

    private readonly NpcIdentity _identity;
    private readonly string _jobId;

    internal ThorsteinPlanCodec(NpcIdentity identity, string jobId)
    {
        if (identity.IsEmpty)
        {
            throw new ArgumentException("A plan codec needs Thorstein's identity.", nameof(identity));
        }

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentException("A plan codec needs the job id.", nameof(jobId));
        }

        _identity = identity;
        _jobId = jobId;
    }

    public IReadOnlyList<string>? Encode(NpcPlanState state)
    {
        if (state == null || !state.Identity.Equals(_identity)
            || !string.Equals(state.JobId, _jobId, StringComparison.Ordinal)
            || state.Reservations.Count != 0)
        {
            // Thorstein's reservations are the settlement record's, never the
            // plan's; a plan that claims one is not one this build wrote.
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
                    AtomicTextFile.Escape(state.Identity.Value),
                    AtomicTextFile.Escape(state.JobId),
                    ((int)state.Phase).ToString(CultureInfo.InvariantCulture),
                    ((int)state.Custody).ToString(CultureInfo.InvariantCulture),
                    state.TargetsDone.ToString(CultureInfo.InvariantCulture),
                    state.TargetsTotal.ToString(CultureInfo.InvariantCulture),
                    AtomicTextFile.Escape(state.SourceKey),
                    AtomicTextFile.Escape(state.DestinationKey),
                    AtomicTextFile.Escape(state.VehicleKey),
                    state.Attempt.ToString(CultureInfo.InvariantCulture),
                    AtomicTextFile.Escape(state.Note),
                }),
        };

        foreach (NpcMaterialStack stack in state.Carried)
        {
            rows.Add(string.Join(
                "\t",
                new[]
                {
                    CarriedTag,
                    AtomicTextFile.Escape(stack.Material.ItemName),
                    stack.Material.Quality.ToString(CultureInfo.InvariantCulture),
                    stack.Material.Variant.ToString(CultureInfo.InvariantCulture),
                    stack.Count.ToString(CultureInfo.InvariantCulture),
                }));
        }

        return new List<string>(RecordTrailer.Seal(rows, _ => -1L));
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
        bool afterTrailer = false;
        TrailerVerdict trailer = TrailerVerdict.Missing;
        var accumulator = new RecordTrailer.Accumulator();
        NpcPlanState? plan = null;
        var carried = new List<NpcMaterialStack>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (afterTrailer)
            {
                trailer = TrailerVerdict.LinesAfterTrailer;
                break;
            }

            string[] fields = line.Split('\t');
            if (RecordTrailer.IsTrailer(fields))
            {
                trailer = RecordTrailer.Check(fields, accumulator);
                afterTrailer = true;
                continue;
            }

            accumulator.AddLine(line);
            accumulator.CountRow(-1L);

            switch (fields[0])
            {
                case FormatTag:
                    if (sawFormat || fields.Length != 2
                        || !string.Equals(fields[1], FormatVersion, StringComparison.Ordinal))
                    {
                        reason = "the plan format is not one this build writes";
                        return false;
                    }

                    sawFormat = true;
                    break;

                case PlanTag:
                    if (plan != null || !TryReadPlan(fields, out plan, out reason))
                    {
                        if (reason.Length == 0)
                        {
                            reason = "the plan has two plan rows";
                        }

                        return false;
                    }

                    break;

                case CarriedTag:
                    if (!TryReadCarried(fields, out NpcMaterialStack stack) || !names.Add(stack.Material.ItemName))
                    {
                        reason = "a carried row is invalid or names a material twice";
                        return false;
                    }

                    carried.Add(stack);
                    break;

                default:
                    reason = "the plan contains an unknown row named " + fields[0];
                    return false;
            }
        }

        if (!sawFormat || plan == null)
        {
            reason = "the plan is missing its format or plan row";
            return false;
        }

        if (trailer != TrailerVerdict.Intact)
        {
            reason = "the plan is damaged: " + RecordTrailer.Describe(trailer);
            return false;
        }

        if ((plan.Phase == NpcPlanPhase.Settled || plan.Phase == NpcPlanPhase.Refunded) && carried.Count != 0)
        {
            reason = "a finished plan still claims to be carrying something";
            return false;
        }

        state = plan.WithHoldings(null, carried);
        return true;
    }

    private bool TryReadPlan(string[] fields, out NpcPlanState? plan, out string reason)
    {
        plan = null;
        reason = string.Empty;
        if (fields.Length != 12
            || !NpcIdentity.TryParse(AtomicTextFile.Unescape(fields[1]), out NpcIdentity identity)
            || !identity.Equals(_identity)
            || !string.Equals(AtomicTextFile.Unescape(fields[2]), _jobId, StringComparison.Ordinal)
            || !TryNonNegative(fields[5], out int targetsDone)
            || !TryNonNegative(fields[6], out int targetsTotal)
            || !TryNonNegative(fields[10], out int attempt)
            || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int phaseValue)
            || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int custodyValue)
            || !Enum.IsDefined(typeof(NpcPlanPhase), phaseValue)
            || !Enum.IsDefined(typeof(NpcPlanCustody), custodyValue))
        {
            reason = "the plan row is invalid or belongs to another job";
            return false;
        }

        plan = new NpcPlanState(
            identity,
            _jobId,
            (NpcPlanPhase)phaseValue,
            (NpcPlanCustody)custodyValue,
            null,
            null,
            targetsDone,
            targetsTotal,
            AtomicTextFile.Unescape(fields[7]),
            AtomicTextFile.Unescape(fields[8]),
            AtomicTextFile.Unescape(fields[9]),
            NpcWorldEpoch.Unknown,
            attempt,
            AtomicTextFile.Unescape(fields[11]));
        return true;
    }

    private static bool TryReadCarried(string[] fields, out NpcMaterialStack stack)
    {
        stack = default;
        if (fields.Length != 5
            || !TryPositive(fields[2], out int quality)
            || !TryNonNegative(fields[3], out int variant)
            || !TryPositive(fields[4], out int count))
        {
            return false;
        }

        stack = new NpcMaterialStack(new NpcMaterial(AtomicTextFile.Unescape(fields[1]), quality, variant), count);
        return stack.IsValid;
    }

    private static bool TryNonNegative(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed >= 0;

    private static bool TryPositive(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
}
