using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Compares what the record expects with what the places actually
/// hold, after the role's marker rule has voided whatever the world rolled
/// back.
///
/// <b>Nothing here writes anything, applies anything or corrects anything.</b>
/// Every finding that is not a match waits for a person, with the evidence. A
/// reconciler that fixed things would be guessing, and every guess it could
/// make either conjures material or destroys it.
///
/// <b>Only the places the NPC is responsible for are compared</b> - see
/// <see cref="NpcCustodyLocation.IsObservable"/>. A delivery container also
/// holds the player's own things and later changes to it are theirs; handed
/// over and lost are dispositions rather than places; and a traced drop's id
/// means nothing once the world has been reloaded.
///
/// <b>Emptied places are looked at too.</b> A place the record says holds
/// nothing is exactly where unaccounted material would go unseen, because
/// nothing would ever look there.</summary>
internal static class NpcCustodyReconciler
{
    internal static NpcReconciliationReport Reconcile(
        NpcCustodyLedger ledger, INpcCustodyObserver observer, bool jobsWereRunning)
    {
        if (ledger == null)
        {
            throw new ArgumentNullException(nameof(ledger));
        }

        if (observer == null)
        {
            throw new ArgumentNullException(nameof(observer));
        }

        var findings = new List<NpcReconciliationFinding>();
        var places = new List<PlaceItem>();
        var seen = new HashSet<PlaceItem>();

        foreach (NpcHolding holding in ledger.EverHeld)
        {
            var key = new PlaceItem(holding.Location, holding.Material);
            if (holding.Location.IsObservable && seen.Add(key))
            {
                places.Add(key);
            }
        }

        var awaiting = new List<NpcTransferRecord>();
        foreach (NpcTransferRecord transfer in ledger.Transfers)
        {
            if (!transfer.AwaitsResolution && transfer.Status != NpcTransferStatus.Open)
            {
                continue;
            }

            awaiting.Add(transfer);
            AddPlace(places, seen, transfer.Intent.From, transfer.Intent.Material);
            AddPlace(places, seen, transfer.Intent.To, transfer.Intent.Material);
        }

        var actuals = new Dictionary<PlaceItem, int?>();
        foreach (PlaceItem place in places)
        {
            int? actual;
            try
            {
                actual = observer.Observe(place.Location, place.Material);
            }
            catch (Exception)
            {
                // An observer that throws has told us nothing, which is exactly
                // what null means. Never zero: zero is a shortfall.
                actual = null;
            }

            actuals[place] = actual;

            if (CountTouching(awaiting, place) > 0)
            {
                // Judged below, per transfer: an uncertain transfer explains
                // the difference, and reporting it twice is how a player stops
                // reading the report.
                continue;
            }

            findings.Add(PlaceFinding(ledger, place, actual));
        }

        foreach (NpcTransferRecord transfer in awaiting)
        {
            findings.Add(TransferFinding(ledger, transfer, awaiting, actuals));
        }

        return new NpcReconciliationReport(findings, jobsWereRunning);
    }

    private static void AddPlace(
        List<PlaceItem> places, HashSet<PlaceItem> seen, NpcCustodyLocation location, NpcMaterial material)
    {
        var key = new PlaceItem(location, material);
        if (location.IsObservable && seen.Add(key))
        {
            places.Add(key);
        }
    }

    private static int CountTouching(List<NpcTransferRecord> awaiting, PlaceItem place)
    {
        int count = 0;
        foreach (NpcTransferRecord transfer in awaiting)
        {
            if (!transfer.Intent.Material.Equals(place.Material))
            {
                continue;
            }

            if (transfer.Intent.From.Equals(place.Location) || transfer.Intent.To.Equals(place.Location))
            {
                count++;
            }
        }

        return count;
    }

    private static NpcReconciliationFinding PlaceFinding(
        NpcCustodyLedger ledger, PlaceItem place, int? actual)
    {
        int expected = ledger.TotalAt(place.Location, place.Material);
        IReadOnlyList<string> holders = HoldersAt(ledger, place);

        if (!actual.HasValue)
        {
            return new NpcReconciliationFinding(
                NpcReconciliationFindingKind.Unclear, holders, place.Location, place.Material,
                expected, null, default, null, null);
        }

        int difference = actual.Value - expected;
        NpcReconciliationFindingKind kind =
            difference == 0 ? NpcReconciliationFindingKind.Matches
            : difference < 0 ? NpcReconciliationFindingKind.BelowExpected
            : NpcReconciliationFindingKind.AboveExpected;

        return new NpcReconciliationFinding(
            kind, holders, place.Location, place.Material, expected, actual, default, null, null);
    }

    private static NpcReconciliationFinding TransferFinding(
        NpcCustodyLedger ledger,
        NpcTransferRecord transfer,
        List<NpcTransferRecord> awaiting,
        Dictionary<PlaceItem, int?> actuals)
    {
        NpcTransferIntent intent = transfer.Intent;
        var jobs = new[] { transfer.JobId };

        var from = new PlaceItem(intent.From, intent.Material);
        var to = new PlaceItem(intent.To, intent.Material);

        // Two uncertain changes over one place make the counts mean nothing:
        // either transfer's units could account for the difference, and
        // attributing it to one of them at random is worse than saying so.
        bool shared = (intent.From.IsObservable && CountTouching(awaiting, from) > 1)
            || (intent.To.IsObservable && CountTouching(awaiting, to) > 1);

        int? fromDelta = Delta(ledger, actuals, from);
        int? toDelta = Delta(ledger, actuals, to);

        if (shared || (!fromDelta.HasValue && !toDelta.HasValue))
        {
            return Finding(NpcReconciliationFindingKind.Unclear, jobs, intent, fromDelta, toDelta);
        }

        // An unchecked side never contradicts: with one side readable, the
        // readable side decides, and the finding carries which was which.
        bool fromMoved = !fromDelta.HasValue || fromDelta.Value == -intent.Count;
        bool toMoved = !toDelta.HasValue || toDelta.Value == intent.Count;
        bool fromStayed = !fromDelta.HasValue || fromDelta.Value == 0;
        bool toStayed = !toDelta.HasValue || toDelta.Value == 0;

        if (fromMoved && toMoved)
        {
            return Finding(NpcReconciliationFindingKind.EffectVisible, jobs, intent, fromDelta, toDelta);
        }

        if (fromStayed && toStayed)
        {
            return Finding(NpcReconciliationFindingKind.NoEffect, jobs, intent, fromDelta, toDelta);
        }

        return Finding(NpcReconciliationFindingKind.Unclear, jobs, intent, fromDelta, toDelta);
    }

    private static NpcReconciliationFinding Finding(
        NpcReconciliationFindingKind kind,
        IReadOnlyList<string> jobs,
        NpcTransferIntent intent,
        int? fromDelta,
        int? toDelta) =>
        new NpcReconciliationFinding(
            kind, jobs, intent.From, intent.Material, intent.Count, null, intent.Request, fromDelta, toDelta);

    private static IReadOnlyList<string> HoldersAt(NpcCustodyLedger ledger, PlaceItem place)
    {
        var jobs = new List<string>();
        foreach (NpcHolding holding in ledger.EverHeld)
        {
            if (holding.Location.Equals(place.Location)
                && holding.Material.Equals(place.Material)
                && !jobs.Contains(holding.JobId))
            {
                jobs.Add(holding.JobId);
            }
        }

        return jobs;
    }

    private static int? Delta(
        NpcCustodyLedger ledger, Dictionary<PlaceItem, int?> actuals, PlaceItem place)
    {
        if (!place.Location.IsObservable
            || !actuals.TryGetValue(place, out int? actual)
            || !actual.HasValue)
        {
            return null;
        }

        return actual.Value - ledger.TotalAt(place.Location, place.Material);
    }

    private readonly struct PlaceItem : IEquatable<PlaceItem>
    {
        internal PlaceItem(NpcCustodyLocation location, NpcMaterial material)
        {
            Location = location;
            Material = material;
        }

        internal NpcCustodyLocation Location { get; }

        internal NpcMaterial Material { get; }

        public bool Equals(PlaceItem other) =>
            Location.Equals(other.Location) && Material.Equals(other.Material);

        public override bool Equals(object? obj) => obj is PlaceItem other && Equals(other);

        public override int GetHashCode() => unchecked((Location.GetHashCode() * 397) ^ Material.GetHashCode());
    }
}
