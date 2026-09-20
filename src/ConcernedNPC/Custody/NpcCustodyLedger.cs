using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Reservations;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Who holds what, on behalf of which job, and where every unit is.
///
/// <b>This is the truth, and the NPC's inventory is not.</b> An inventory lives
/// in a world object that can be lost to a death, a despawn or a zone unload,
/// and a record that loses material on events which are not cancellations is
/// not a record. What the NPC visibly carries is evidence that this ledger is
/// doing something; when the two disagree, reconciliation says so and a person
/// decides - and this one is what the answer is written against.
///
/// <b>The one invariant, stated so it can be tested rather than believed.</b>
/// For every job and every material,
///
/// <code>units acquired = sum over every place of the units held there</code>
///
/// and nothing in this file may break it. Every entry point either adds to
/// <see cref="Acquire"/>'s left-hand side or moves units between places on the
/// right. There is no subtraction: loss is a move to
/// <see cref="NpcCustodyPlace.Lost"/>, delivery is a move to
/// <see cref="NpcCustodyPlace.Delivered"/>, and handing something to the player
/// is a move to <see cref="NpcCustodyPlace.Handed"/>. A place is never emptied
/// by forgetting it.
///
/// <b>Idempotence is payload-checked, everywhere.</b> Every operation is named
/// by a <see cref="ReservationId"/>, which is derived from a job and a step and
/// is therefore the same name after a restart. The same name with the same
/// payload is <see cref="NpcCustodyOutcome.AlreadySatisfied"/> and changes
/// nothing; the same name with a different payload is
/// <see cref="NpcCustodyOutcome.RejectedDifferentPayload"/>, never waved
/// through. <b>This is what makes "a reservation must never duplicate an item
/// because it was restored twice" true</b>: restoring is re-stating names that
/// are already recorded, and a re-stated name applies nothing.
///
/// <b>The revision counts records, not holdings.</b> It rises with every row
/// the ledger takes, including rows later voided, so it never goes backwards
/// across a reload and a name derived from it is unique for the life of the
/// record. A transfer planned against an older revision is stale.</summary>
internal sealed class NpcCustodyLedger
{
    private readonly Dictionary<string, NpcTransferRecord> _transfers =
        new Dictionary<string, NpcTransferRecord>(StringComparer.Ordinal);
    private readonly List<string> _transferOrder = new List<string>();

    /// <summary>Named acquisitions, and - in its own dictionary - named
    /// write-offs.
    ///
    /// <b>Two name spaces, deliberately.</b> They were one, and that was a
    /// defect: a role naming a write-off after the step it happened at -
    /// <c>job#3</c>, the obvious convention - collided with the acquisition at
    /// that same step, and the person's answer was silently discarded as
    /// "already satisfied" while the ledger went on saying the material was
    /// still on the body. Acquiring and writing off are different operations;
    /// a name means one of each, and neither can swallow the other.</summary>
    private readonly Dictionary<string, Operation> _acquisitions =
        new Dictionary<string, Operation>(StringComparer.Ordinal);

    private readonly Dictionary<string, Operation> _writeOffs =
        new Dictionary<string, Operation>(StringComparer.Ordinal);

    private readonly Dictionary<HoldingKey, int> _holdings = new Dictionary<HoldingKey, int>();
    private readonly List<HoldingKey> _holdingOrder = new List<HoldingKey>();

    private readonly HashSet<string> _openJobs = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Rows taken, applied or voided. See the class summary.</summary>
    internal int Revision { get; private set; }

    /// <summary>Counts a row the ledger took. Called by the executor once the
    /// row has reached the role's record - never before, because a revision
    /// that moved for a row nobody wrote makes every planned transfer stale for
    /// no reason.</summary>
    internal void CountRecord() => Revision++;

    // ------------------------------------------------------------------
    // Jobs
    // ------------------------------------------------------------------

    /// <summary>A job exists and may hold material. Idempotent.</summary>
    internal void OpenJob(string? jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return;
        }

        _openJobs.Add(jobId!);
    }

    /// <summary>A job has ended. Its holdings are <b>not</b> discarded: whatever
    /// it still holds is exactly what a person needs to see, and a job that
    /// ended holding forty units of stone is a fact rather than a rounding
    /// error. Ending it only stops new transfers.</summary>
    internal void CloseJob(string? jobId)
    {
        if (!string.IsNullOrEmpty(jobId))
        {
            _openJobs.Remove(jobId!);
        }
    }

    internal bool IsOpen(string? jobId) => !string.IsNullOrEmpty(jobId) && _openJobs.Contains(jobId!);

    // ------------------------------------------------------------------
    // Entry
    // ------------------------------------------------------------------

    /// <summary>The one way units enter the ledger: a job took possession of
    /// material that was not previously recorded anywhere - it picked it up, it
    /// withdrew it from a container, the world gave it to it.
    ///
    /// <b>Named, and therefore safe to re-state - but only as the same thing
    /// it was.</b> Restating a name that applied units adds nothing, which is
    /// what stops a resumed job from acquiring the same material twice.
    /// Restating a name that applied <i>nothing</i> - because the marker rule
    /// voided it, or could not place it - is
    /// <see cref="NpcCustodyOutcome.Stale"/>, not a success: the world rolled
    /// that work back, and a live retry under its name must take a new name or
    /// the units it is carrying land in no holding at all.
    ///
    /// <b>A row that applies nothing is remembered even for a job nobody has
    /// opened.</b> Remembering a name costs nothing and applies nothing, and
    /// the alternative is worse in exactly one way that matters: a voided row
    /// refused for arriving before its job would leave the name free, and the
    /// next live row under it would apply units the world had already undone.
    /// The transfer half has always remembered unconditionally; these two
    /// halves of one rule now agree about replay order.</summary>
    internal NpcCustodyOutcome Acquire(
        ReservationId request,
        NpcCustodyLocation at,
        NpcMaterial material,
        int count,
        NpcRowStanding standing = NpcRowStanding.Live)
    {
        if (request.IsEmpty || !at.IsSpecified || !material.IsNamed || count < 1)
        {
            return NpcCustodyOutcome.Rejected;
        }

        var payload = new Operation(request.JobId, at, material, count, Applies(standing));
        if (_acquisitions.TryGetValue(request.Value, out Operation recorded))
        {
            return Restated(recorded, payload);
        }

        if (!payload.Applied)
        {
            // Voided or unplaceable: the name is taken, the units are not. Kept
            // ahead of the open-job check on purpose - see the summary.
            _acquisitions.Add(request.Value, payload);
            return NpcCustodyOutcome.Applied;
        }

        if (!IsOpen(request.JobId))
        {
            return NpcCustodyOutcome.Rejected;
        }

        _acquisitions.Add(request.Value, payload);
        Add(request.JobId, at, material, count);
        return NpcCustodyOutcome.Applied;
    }

    /// <summary>How many units this job has ever taken possession of, at all -
    /// the left-hand side of the conservation invariant, for the tests that
    /// check it and for anybody who wants to say why a number is what it is.
    /// </summary>
    internal int Acquired(string? jobId, NpcMaterial material)
    {
        int total = 0;
        foreach (KeyValuePair<string, Operation> pair in _acquisitions)
        {
            Operation acquisition = pair.Value;
            if (string.Equals(acquisition.JobId, jobId ?? string.Empty, StringComparison.Ordinal)
                && acquisition.Material.Equals(material)
                && acquisition.Applied)
            {
                total += acquisition.Count;
            }
        }

        return total;
    }

    /// <summary>Whether a standing puts units into the ledger.
    ///
    /// <b><see cref="NpcRowStanding.Ambiguous"/> credits nothing</b>, and that
    /// is a deliberate direction rather than an oversight. A row the marker
    /// rule could not place before or after the loaded save may or may not have
    /// happened. Crediting it and being wrong asks a player to write off
    /// material that never existed; not crediting it and being wrong leaves
    /// real material for reconciliation to find at a place it can count. Only
    /// one of those two mistakes destroys something, so custody fails closed,
    /// as the safety rules require of it.</summary>
    private static bool Applies(NpcRowStanding standing) =>
        standing != NpcRowStanding.Voided && standing != NpcRowStanding.Ambiguous;

    /// <summary>Puts a transfer record into the standing the marker rule gave
    /// its row, and <b>refuses to do so over a record that has already moved
    /// units</b>.
    ///
    /// A row that applies nothing may only land on a record that has applied
    /// nothing: one still open, or one already in the very standing being
    /// restated. Anything else is the record and the holdings disagreeing about
    /// the same units, with the record marked settled so nothing ever looks at
    /// it again.</summary>
    private static NpcCustodyOutcome Restanding(NpcTransferRecord record, NpcRowStanding standing)
    {
        NpcTransferStatus wanted = standing == NpcRowStanding.Voided
            ? NpcTransferStatus.Voided
            : NpcTransferStatus.Ambiguous;

        if (record.Status == wanted)
        {
            return NpcCustodyOutcome.AlreadySatisfied;
        }

        if (record.Status != NpcTransferStatus.Open)
        {
            return NpcCustodyOutcome.Rejected;
        }

        record.Status = wanted;
        record.Evidence = standing == NpcRowStanding.Voided
            ? "the world was loaded from a save made before this was written"
            : "this could not be placed before or after the loaded world save";
        return NpcCustodyOutcome.Applied;
    }

    /// <summary>The answer to a named operation arriving a second time.
    ///
    /// <b>The bug this method exists to make impossible.</b> Equality over the
    /// payload alone said a rolled-back row and a live row under one name were
    /// the same operation, so a live retry was told
    /// <see cref="NpcCustodyOutcome.AlreadySatisfied"/> - documented as
    /// "nothing changed and nothing is wrong" - and recorded nothing. Forty
    /// units of a player's stone could then be physically out of a chest and in
    /// no holding, with <see cref="IsConserved"/> true because nothing had ever
    /// been recorded to conserve.</summary>
    private static NpcCustodyOutcome Restated(Operation recorded, Operation asked)
    {
        if (!recorded.SamePayloadAs(asked))
        {
            return NpcCustodyOutcome.RejectedDifferentPayload;
        }

        if (recorded.Applied)
        {
            // The ordinary retry: this operation happened, under this name,
            // with these units. Saying so is what makes a resumed job safe.
            return NpcCustodyOutcome.AlreadySatisfied;
        }

        if (!asked.Applied)
        {
            // Restating the rolled-back row itself, which a replay does. Still
            // nothing to apply.
            return NpcCustodyOutcome.AlreadySatisfied;
        }

        return NpcCustodyOutcome.Stale;
    }

    // ------------------------------------------------------------------
    // Transfers
    // ------------------------------------------------------------------

    internal bool TryGetTransfer(ReservationId request, out NpcTransferRecord record)
    {
        record = null!;
        return !request.IsEmpty && _transfers.TryGetValue(request.Value, out record!);
    }

    /// <summary>The ledger's half of the executor's first step.
    /// <see cref="NpcTransferOutcome.Unspecified"/> means the record permits
    /// this transfer; anything else <b>is</b> the answer, and nothing has
    /// changed.</summary>
    internal NpcTransferOutcome Check(NpcTransferIntent intent, out string reason)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (_transfers.TryGetValue(intent.Request.Value, out NpcTransferRecord? existing))
        {
            if (!existing!.Intent.SamePayloadAs(intent))
            {
                reason = "that name was already used for a different transfer";
                return NpcTransferOutcome.RejectedDifferentPayload;
            }

            switch (existing.Status)
            {
                case NpcTransferStatus.Completed:
                case NpcTransferStatus.Partial:
                case NpcTransferStatus.Refused:
                case NpcTransferStatus.Resolved:
                    reason = "already recorded as " + existing.Status;
                    return NpcTransferOutcome.AlreadySatisfied;

                case NpcTransferStatus.Voided:
                    reason = "that name belongs to work the world rolled back; plan a new step";
                    return NpcTransferOutcome.Stale;

                default:
                    // Open, uncertain or ambiguous: it may have happened.
                    // Never started again, by anybody, for any reason.
                    reason = "that transfer was started and its result is not recorded";
                    return NpcTransferOutcome.Uncertain;
            }
        }

        if (!intent.IsWellFormed)
        {
            reason = "that is not a transfer: it needs a name, two different places in one world load, " +
                "a named material and at least one unit";
            return NpcTransferOutcome.Refused;
        }

        if (!IsOpen(intent.JobId))
        {
            reason = "no open job " + intent.JobId;
            return NpcTransferOutcome.Refused;
        }

        if (intent.ExpectedRevision != Revision)
        {
            reason = "planned against custody revision " +
                intent.ExpectedRevision.ToString(CultureInfo.InvariantCulture) + ", now " +
                Revision.ToString(CultureInfo.InvariantCulture);
            return NpcTransferOutcome.Stale;
        }

        if (!intent.From.MayLeave)
        {
            reason = "material recorded at " + intent.From + " is not moved again by the job";
            return NpcTransferOutcome.Refused;
        }

        if (intent.To.Place == NpcCustodyPlace.Ground)
        {
            reason = "nothing is put back on the ground as material the job is carrying";
            return NpcTransferOutcome.Refused;
        }

        if (HasUnresolvedTransfer(intent.JobId))
        {
            reason = "job " + intent.JobId + " has a transfer waiting on a person's answer";
            return NpcTransferOutcome.Refused;
        }

        int held = HoldingAt(intent.JobId, intent.From, intent.Material);
        if (held < intent.Count)
        {
            reason = "the record holds " + held.ToString(CultureInfo.InvariantCulture) + " " + intent.Material +
                " at " + intent.From + " for this job, not " + intent.Count.ToString(CultureInfo.InvariantCulture);
            return NpcTransferOutcome.Refused;
        }

        reason = string.Empty;
        return NpcTransferOutcome.Unspecified;
    }

    /// <summary>Records a persisted intent. Called only once the intent has
    /// reached the role's record.</summary>
    internal NpcCustodyOutcome Begin(NpcTransferIntent intent, NpcRowStanding standing = NpcRowStanding.Live)
    {
        if (intent == null)
        {
            throw new ArgumentNullException(nameof(intent));
        }

        if (_transfers.TryGetValue(intent.Request.Value, out NpcTransferRecord? existing))
        {
            if (!existing!.Intent.SamePayloadAs(intent))
            {
                return NpcCustodyOutcome.RejectedDifferentPayload;
            }

            if (standing == NpcRowStanding.Voided || standing == NpcRowStanding.Ambiguous)
            {
                // The intent row again, this time carrying a standing that
                // applies nothing. Answering "already satisfied" here dropped
                // the void on the floor and left a rolled-back transfer sitting
                // in the record as Completed, with its units moved.
                return Restanding(existing, standing);
            }

            return NpcCustodyOutcome.AlreadySatisfied;
        }

        var record = new NpcTransferRecord(intent);
        if (!Applies(standing))
        {
            // A fresh record is Open, so this never refuses here; it is the
            // same call the restated path makes, so the two cannot drift.
            Restanding(record, standing);
        }

        _transfers.Add(intent.Request.Value, record);
        _transferOrder.Add(intent.Request.Value);
        return NpcCustodyOutcome.Applied;
    }

    /// <summary>Applies a persisted receipt: moves exactly the accepted units
    /// from the intent's source to its destination. An uncertain receipt moves
    /// nothing and keeps its evidence.</summary>
    internal NpcCustodyOutcome Finish(NpcTransferReceipt receipt, NpcRowStanding standing = NpcRowStanding.Live)
    {
        if (receipt == null)
        {
            throw new ArgumentNullException(nameof(receipt));
        }

        if (!TryGetTransfer(receipt.Request, out NpcTransferRecord record))
        {
            return NpcCustodyOutcome.Rejected;
        }

        if (standing == NpcRowStanding.Voided || standing == NpcRowStanding.Ambiguous)
        {
            // Both halves of one call are placed together, so a receipt that
            // applies nothing belongs to an intent that applied nothing.
            //
            // The guard is the point. Without it, a voided receipt arriving for
            // a record already Completed rewrote it to Voided - "never
            // credited, refunded or replayed; there is nothing to undo" - while
            // its units stayed moved in the holdings, and Voided counts as
            // settled, so reconciliation never looked at it again. Silent
            // divergence, no finding raised. Its neighbour had this guard and
            // it did not.
            NpcCustodyOutcome restanding = Restanding(record, standing);
            if (restanding == NpcCustodyOutcome.Applied)
            {
                record.Evidence = Join(receipt.Evidence, record.Evidence);
            }

            return restanding;
        }

        switch (record.Status)
        {
            case NpcTransferStatus.Open:
                break;

            case NpcTransferStatus.Uncertain:
            case NpcTransferStatus.Ambiguous:
                // A receipt arriving for something already uncertain is either
                // a duplicate of the uncertain receipt or evidence nobody has
                // looked at. Never applied on its own.
                return receipt.Outcome == NpcTransferOutcome.Uncertain
                    ? NpcCustodyOutcome.AlreadySatisfied
                    : NpcCustodyOutcome.Rejected;

            case NpcTransferStatus.Voided:
            case NpcTransferStatus.Resolved:
                return NpcCustodyOutcome.Rejected;

            default:
                return StatusFor(receipt.Outcome) == record.Status && receipt.Accepted == record.Applied
                    ? NpcCustodyOutcome.AlreadySatisfied
                    : NpcCustodyOutcome.RejectedDifferentPayload;
        }

        switch (receipt.Outcome)
        {
            case NpcTransferOutcome.Completed:
            case NpcTransferOutcome.Partial:
            {
                int accepted = receipt.Accepted;
                if (accepted < 1
                    || accepted > record.Intent.Count
                    || (receipt.Outcome == NpcTransferOutcome.Completed && accepted != record.Intent.Count)
                    || HoldingAt(record.JobId, record.Intent.From, record.Intent.Material) < accepted)
                {
                    // A receipt that cannot be true against the record: more
                    // than intended, or more than the source holds. Kept as
                    // evidence rather than applied - applying it is where a
                    // ledger starts inventing units.
                    record.Status = NpcTransferStatus.Uncertain;
                    record.Evidence = Join(receipt.Evidence, "the receipt's counts do not fit the record");
                    return NpcCustodyOutcome.Applied;
                }

                Move(record.JobId, record.Intent.From, record.Intent.To, record.Intent.Material, accepted);
                record.Applied = accepted;
                record.Status = accepted == record.Intent.Count
                    ? NpcTransferStatus.Completed
                    : NpcTransferStatus.Partial;
                record.Evidence = receipt.Evidence;
                return NpcCustodyOutcome.Applied;
            }

            case NpcTransferOutcome.Refused:
            case NpcTransferOutcome.Stale:
                record.Status = NpcTransferStatus.Refused;
                record.Evidence = receipt.Evidence;
                return NpcCustodyOutcome.Applied;

            case NpcTransferOutcome.Uncertain:
                record.Status = NpcTransferStatus.Uncertain;
                record.Evidence = receipt.Evidence;
                return NpcCustodyOutcome.Applied;

            default:
                return NpcCustodyOutcome.Rejected;
        }
    }

    /// <summary>In memory only: a transfer whose receipt could not be written,
    /// or whose engine step threw.</summary>
    internal NpcCustodyOutcome MarkUncertain(ReservationId request, string? evidence)
    {
        if (!TryGetTransfer(request, out NpcTransferRecord record))
        {
            return NpcCustodyOutcome.Rejected;
        }

        if (record.Status == NpcTransferStatus.Uncertain)
        {
            return NpcCustodyOutcome.AlreadySatisfied;
        }

        if (record.Status != NpcTransferStatus.Open)
        {
            return NpcCustodyOutcome.Rejected;
        }

        record.Status = NpcTransferStatus.Uncertain;
        record.Evidence = evidence ?? string.Empty;
        return NpcCustodyOutcome.Applied;
    }

    /// <summary>Every intent still open becomes uncertain. <b>Called once, when
    /// a record has been replayed from disk</b>: nothing replayed is still in
    /// flight, and an intent with no receipt is the exact shape of an
    /// interruption. Returns how many.</summary>
    internal int CloseOpenIntents()
    {
        int closed = 0;
        foreach (string key in _transferOrder)
        {
            NpcTransferRecord record = _transfers[key];
            if (record.Status != NpcTransferStatus.Open)
            {
                continue;
            }

            record.Status = NpcTransferStatus.Uncertain;
            record.Evidence = "the session ended after this was written down and before its result was";
            closed++;
        }

        return closed;
    }

    /// <summary>A person's answer to an uncertain or ambiguous transfer.
    /// <paramref name="units"/> is how many arrived when the answer is
    /// <see cref="NpcTransferSide.Destination"/>; the rest stays at the source.
    ///
    /// <b>Exactly once.</b> A resolved transfer is terminal, and a second
    /// answer - the same one or a different one - changes nothing.</summary>
    internal NpcCustodyOutcome Resolve(ReservationId request, NpcTransferSide side, int units)
    {
        if (!TryGetTransfer(request, out NpcTransferRecord record))
        {
            return NpcCustodyOutcome.Rejected;
        }

        if (record.Status == NpcTransferStatus.Resolved)
        {
            return NpcCustodyOutcome.AlreadySatisfied;
        }

        if (!record.AwaitsResolution || side == NpcTransferSide.Unspecified)
        {
            return NpcCustodyOutcome.Rejected;
        }

        int moved = 0;
        if (side == NpcTransferSide.Destination)
        {
            moved = units < 0 ? 0 : units > record.Intent.Count ? record.Intent.Count : units;
            int held = HoldingAt(record.JobId, record.Intent.From, record.Intent.Material);
            if (moved > held)
            {
                moved = held;
            }

            if (moved > 0)
            {
                Move(record.JobId, record.Intent.From, record.Intent.To, record.Intent.Material, moved);
            }
        }

        record.Applied = moved;
        record.Status = NpcTransferStatus.Resolved;
        record.Evidence = Join(
            record.Evidence,
            "a person said the material is at the " + (side == NpcTransferSide.Source ? "source" : "destination"));
        return NpcCustodyOutcome.Applied;
    }

    /// <summary>A person accepting observed loss: moves units from where the
    /// record holds them to <see cref="NpcCustodyPlace.Lost"/>. Never below
    /// zero, and never a subtraction - loss is a place, so the conservation
    /// invariant still holds afterwards, which is what lets a shortfall be
    /// reported honestly instead of vanishing.
    ///
    /// <b>Write-offs have their own name space.</b> A role may name this after
    /// the step the loss happened at without colliding with that step's
    /// acquisition. They shared one, and the collision discarded a person's
    /// answer while telling the caller it had been applied.</summary>
    internal NpcCustodyOutcome RecordLoss(
        ReservationId request,
        NpcCustodyLocation at,
        NpcMaterial material,
        int count,
        NpcRowStanding standing = NpcRowStanding.Live)
    {
        if (request.IsEmpty || !at.IsSpecified || !material.IsNamed || count < 1)
        {
            return NpcCustodyOutcome.Rejected;
        }

        // A write-off moves units between places; it never creates them, so it
        // is not counted on the acquired side of the invariant. Applies() here
        // says only whether this row does anything at all.
        var payload = new Operation(request.JobId, at, material, count, Applies(standing));
        if (_writeOffs.TryGetValue(request.Value, out Operation recorded))
        {
            return Restated(recorded, payload);
        }

        if (!payload.Applied)
        {
            _writeOffs.Add(request.Value, payload);
            return NpcCustodyOutcome.Applied;
        }

        int held = HoldingAt(request.JobId, at, material);
        if (held < count)
        {
            return NpcCustodyOutcome.Rejected;
        }

        _writeOffs.Add(request.Value, payload);
        Move(request.JobId, at, new NpcCustodyLocation(NpcCustodyPlace.Lost, at.Key, at.Epoch), material, count);
        return NpcCustodyOutcome.Applied;
    }

    // ------------------------------------------------------------------
    // Views
    // ------------------------------------------------------------------

    internal int HoldingAt(string? jobId, NpcCustodyLocation location, NpcMaterial material) =>
        _holdings.TryGetValue(new HoldingKey(jobId ?? string.Empty, location, material), out int count) ? count : 0;

    /// <summary>Every job's holding at one location, per material - what a
    /// physical inventory should contain of this job's material.</summary>
    internal int TotalAt(NpcCustodyLocation location, NpcMaterial material)
    {
        int total = 0;
        foreach (HoldingKey key in _holdingOrder)
        {
            if (key.Location.Equals(location) && key.Material.Equals(material))
            {
                total += _holdings[key];
            }
        }

        return total;
    }

    /// <summary>How many units of one material this job holds anywhere at all -
    /// the right-hand side of the conservation invariant.</summary>
    internal int TotalEverywhere(string? jobId, NpcMaterial material)
    {
        int total = 0;
        foreach (HoldingKey key in _holdingOrder)
        {
            if (string.Equals(key.JobId, jobId ?? string.Empty, StringComparison.Ordinal)
                && key.Material.Equals(material))
            {
                total += _holdings[key];
            }
        }

        return total;
    }

    /// <summary>Non-zero holdings, in the order they first appeared.</summary>
    internal IReadOnlyList<NpcHolding> Holdings => Snapshot(false);

    /// <summary>Every place and material this record has ever held for a job -
    /// <b>including the ones it has emptied</b>.
    ///
    /// Reconciliation needs the emptied ones. A place the record says holds
    /// nothing is exactly where material nobody accounted for would otherwise
    /// go unseen, because nothing would ever look there.</summary>
    internal IReadOnlyList<NpcHolding> EverHeld => Snapshot(true);

    internal IReadOnlyList<NpcTransferRecord> Transfers
    {
        get
        {
            var list = new List<NpcTransferRecord>(_transferOrder.Count);
            foreach (string key in _transferOrder)
            {
                list.Add(_transfers[key]);
            }

            return list;
        }
    }

    /// <summary>True while anything of this job waits on a person.</summary>
    internal bool HasUnresolvedTransfer(string? jobId)
    {
        foreach (string key in _transferOrder)
        {
            NpcTransferRecord record = _transfers[key];
            if (string.Equals(record.JobId, jobId ?? string.Empty, StringComparison.Ordinal)
                && (record.AwaitsResolution || record.Status == NpcTransferStatus.Open))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether this job's records and its holdings agree. The
    /// conservation invariant, as a question anybody can ask at any moment -
    /// and what the tests assert at every transition rather than only at the
    /// end.</summary>
    internal bool IsConserved(string? jobId, NpcMaterial material) =>
        Acquired(jobId, material) == TotalEverywhere(jobId, material);

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private IReadOnlyList<NpcHolding> Snapshot(bool includeEmpty)
    {
        var list = new List<NpcHolding>();
        foreach (HoldingKey key in _holdingOrder)
        {
            int count = _holdings[key];
            if (count > 0 || includeEmpty)
            {
                list.Add(new NpcHolding(key.JobId, key.Location, key.Material, count));
            }
        }

        return list;
    }

    private void Move(
        string jobId, NpcCustodyLocation from, NpcCustodyLocation to, NpcMaterial material, int count)
    {
        Add(jobId, from, material, -count);
        Add(jobId, to, material, count);
    }

    private void Add(string jobId, NpcCustodyLocation location, NpcMaterial material, int delta)
    {
        var key = new HoldingKey(jobId, location, material);
        if (!_holdings.TryGetValue(key, out int current))
        {
            _holdingOrder.Add(key);
        }

        int next = current + delta;
        if (next < 0)
        {
            // Every caller checks the holding first, so reaching here is a
            // defect in this file. The ledger refuses to go negative rather
            // than pretending a unit exists twice somewhere else.
            throw new InvalidOperationException(
                "Custody for " + jobId + " at " + location + " would go negative.");
        }

        _holdings[key] = next;
    }

    private static NpcTransferStatus StatusFor(NpcTransferOutcome outcome)
    {
        switch (outcome)
        {
            case NpcTransferOutcome.Completed: return NpcTransferStatus.Completed;
            case NpcTransferOutcome.Partial: return NpcTransferStatus.Partial;
            case NpcTransferOutcome.Refused:
            case NpcTransferOutcome.Stale: return NpcTransferStatus.Refused;
            case NpcTransferOutcome.Uncertain: return NpcTransferStatus.Uncertain;
            default: return NpcTransferStatus.Unspecified;
        }
    }

    private static string Join(string first, string second) =>
        string.IsNullOrEmpty(first) ? second : first + "; " + second;

    /// <summary>One named row the ledger has taken, and whether it did
    /// anything.
    ///
    /// <b>There is no <c>Equals</c> here, on purpose.</b> There used to be, and
    /// it compared the payload and left <see cref="Applied"/> out, which made a
    /// rolled-back row and a live row indistinguishable to every caller that
    /// asked "is this the same operation?" - and one of those two must be
    /// refused while the other is waved through. Asking that question now means
    /// calling <see cref="SamePayloadAs"/> and then deciding about
    /// <see cref="Applied"/> separately, which is a decision somebody has to
    /// write down rather than one an omission can make for them.</summary>
    private readonly struct Operation
    {
        internal Operation(string jobId, NpcCustodyLocation at, NpcMaterial material, int count, bool applied)
        {
            JobId = jobId;
            At = at;
            Material = material;
            Count = count;
            Applied = applied;
        }

        internal string JobId { get; }

        internal NpcCustodyLocation At { get; }

        internal NpcMaterial Material { get; }

        internal int Count { get; }

        /// <summary>Whether this row put units where it said. False for a row
        /// the world rolled back, and for one the marker rule could not place.
        /// </summary>
        internal bool Applied { get; }

        /// <summary>The same operation, described. <b>Says nothing about
        /// whether either of them happened</b> - see the type summary.
        /// </summary>
        internal bool SamePayloadAs(Operation other) =>
            string.Equals(JobId, other.JobId, StringComparison.Ordinal)
            && At.Equals(other.At) && Material.Equals(other.Material) && Count == other.Count;
    }

    private readonly struct HoldingKey : IEquatable<HoldingKey>
    {
        internal HoldingKey(string jobId, NpcCustodyLocation location, NpcMaterial material)
        {
            JobId = jobId;
            Location = location;
            Material = material;
        }

        internal string JobId { get; }

        internal NpcCustodyLocation Location { get; }

        internal NpcMaterial Material { get; }

        public bool Equals(HoldingKey other) =>
            string.Equals(JobId, other.JobId, StringComparison.Ordinal)
            && Location.Equals(other.Location) && Material.Equals(other.Material);

        public override bool Equals(object? obj) => obj is HoldingKey other && Equals(other);

        public override int GetHashCode() =>
            unchecked((StringComparer.Ordinal.GetHashCode(JobId ?? string.Empty) * 397)
                ^ (Location.GetHashCode() * 31) ^ Material.GetHashCode());
    }
}
