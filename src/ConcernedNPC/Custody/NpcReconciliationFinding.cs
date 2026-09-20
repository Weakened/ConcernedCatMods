using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>One thing reconciliation saw: which jobs it concerns, where, what,
/// what the record expected and what was actually there.
///
/// <b>What is deliberately missing: the sentence.</b> The shipped
/// implementation carries a player-facing line and the console command that
/// answers it, and both belong to a role: the command is a role's command, the
/// wording is a role's voice, and a library that owned either would be shipping
/// a product's user interface. Everything needed to write that sentence is
/// here as data, which is the split the rest of this package already
/// makes.</summary>
internal sealed class NpcReconciliationFinding
{
    internal NpcReconciliationFinding(
        NpcReconciliationFindingKind kind,
        IReadOnlyList<string>? jobIds,
        NpcCustodyLocation location,
        NpcMaterial material,
        int expected,
        int? actual,
        ReservationId request,
        int? sourceDelta,
        int? destinationDelta)
    {
        Kind = kind;
        JobIds = jobIds ?? Array.Empty<string>();
        Location = location;
        Material = material;
        Expected = expected;
        Actual = actual;
        Request = request;
        SourceDelta = sourceDelta;
        DestinationDelta = destinationDelta;
    }

    internal NpcReconciliationFindingKind Kind { get; }

    /// <summary>The jobs this concerns: the transfer's job, or every job the
    /// record says holds material at the place.</summary>
    internal IReadOnlyList<string> JobIds { get; }

    internal NpcCustodyLocation Location { get; }

    internal NpcMaterial Material { get; }

    internal int Expected { get; }

    /// <summary>What was counted, or null when the place could not be checked.
    /// </summary>
    internal int? Actual { get; }

    /// <summary>The uncertain transfer this is about; empty for a finding about
    /// a place.</summary>
    internal ReservationId Request { get; }

    /// <summary>For a transfer finding: how the source's count differs from the
    /// record, or null when it could not be checked. Carried so a role can say
    /// what it saw rather than only what it concluded.</summary>
    internal int? SourceDelta { get; }

    /// <summary>For a transfer finding: the same, for the destination.
    /// </summary>
    internal int? DestinationDelta { get; }

    internal bool NeedsAttention => Kind != NpcReconciliationFindingKind.Matches;

    /// <summary>How many units are missing, for a shortfall; zero otherwise.
    /// The number a role may offer to record as lost - and the cap on it, so no
    /// role can record more lost than was ever there.</summary>
    internal int Shortfall =>
        Kind == NpcReconciliationFindingKind.BelowExpected && Actual.HasValue
            ? Math.Max(0, Expected - Actual.Value)
            : 0;

    internal bool Concerns(string? jobId)
    {
        foreach (string candidate in JobIds)
        {
            if (string.Equals(candidate, jobId ?? string.Empty, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() =>
        Kind + " " + Material + " at " + Location + " expected " + Expected +
        ", actual " + (Actual.HasValue ? Actual.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?");
}
