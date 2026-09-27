using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Interruption;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Storage;

namespace TheConcernedCat.ConcernedSteward.Domain.Persistence;

/// <summary>The Steward's path for one maintenance plan.
///
/// The shared runtime accepts an absolute path and deliberately invents none.
/// This class is therefore part of the durable contract: the role owns the
/// suffix and the settlement scope that precedes it.</summary>
internal sealed class StewardPlanFiles
{
    private const string Extension = ".steward-plan.tsv";

    private readonly string _root;

    internal StewardPlanFiles(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("The Steward's plan root is required.", nameof(root));
        }

        _root = Path.GetFullPath(root);
    }

    internal string ResolvePath(SettlementScope scope) =>
        Path.Combine(_root, scope.ToStorageKey() + Extension);
}

/// <summary>The role-owned bytes for a durable maintenance plan.
///
/// ConcernedNPC owns the write ordering and the recovery rules. It does not
/// own these tags, their order, this schema, or the trailer. A malformed row is
/// refused as a whole: partially reading a custody record would be equivalent
/// to deciding which half of a player's material to forget.</summary>
internal sealed class StewardPlanCodec : INpcPlanCodec
{
    private const string Header = "# Concerned Steward maintenance plan. One settlement, one world.";
    private const string FormatTag = "format";
    private const string FormatVersion = "1";
    private const string PlanTag = "plan";
    private const string ReservationTag = "reservation";
    private const string CarriedTag = "carried";

    private readonly NpcIdentity _identity;
    private readonly string _jobId;

    internal StewardPlanCodec(NpcIdentity identity, string jobId)
    {
        if (identity.IsEmpty)
        {
            throw new ArgumentException("A plan codec needs the Steward's identity.", nameof(identity));
        }

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentException("A plan codec needs the maintenance job id.", nameof(jobId));
        }

        _identity = identity;
        _jobId = jobId;
    }

    public IReadOnlyList<string>? Encode(NpcPlanState state)
    {
        if (state == null || !state.Identity.Equals(_identity)
            || !string.Equals(state.JobId, _jobId, StringComparison.Ordinal))
        {
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

        foreach (ReservationId reservation in state.Reservations)
        {
            rows.Add(ReservationTag + "\t" + AtomicTextFile.Escape(reservation.Value));
        }

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
        var reservations = new List<ReservationId>();
        var carried = new List<NpcMaterialStack>();

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
                        return false;
                    }

                    break;

                case ReservationTag:
                    if (fields.Length != 2
                        || !ReservationId.TryParse(
                            AtomicTextFile.Unescape(fields[1]), out ReservationId reservation)
                        || !string.Equals(reservation.JobId, _jobId, StringComparison.Ordinal))
                    {
                        reason = "a reservation row does not name this maintenance job";
                        return false;
                    }

                    reservations.Add(reservation);
                    break;

                case CarriedTag:
                    if (carried.Count != 0 || !TryReadCarried(fields, out NpcMaterialStack stack))
                    {
                        reason = "a carried row is invalid or names more than one material";
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

        if ((plan.Phase == NpcPlanPhase.Settled || plan.Phase == NpcPlanPhase.Refunded)
            && (reservations.Count != 0 || carried.Count != 0 || plan.VehicleKey.Length != 0))
        {
            reason = "a finished plan still claims custody";
            return false;
        }

        state = plan.WithHoldings(reservations, carried);
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
            reason = "the plan row is invalid or belongs to another role";
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

        stack = new NpcMaterialStack(
            new NpcMaterial(AtomicTextFile.Unescape(fields[1]), quality, variant), count);
        return stack.IsValid;
    }

    private static bool TryNonNegative(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed >= 0;

    private static bool TryPositive(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
}
