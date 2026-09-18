using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>Which kind of move a step is. Zero is not a step.</summary>
internal enum UpkeepStep
{
    Unspecified = 0,

    /// <summary>Depot to the Steward's hands.</summary>
    Withdraw = 1,

    /// <summary>The Steward's hands into a fire, one unit.</summary>
    Feed = 2,

    /// <summary>The Steward's hands back into the depot.</summary>
    Deposit = 3,
}

/// <summary>How one step ended. Zero never reads as success.</summary>
internal enum UpkeepOutcome
{
    Unspecified = 0,

    /// <summary>Everything asked for moved.</summary>
    Completed = 1,

    /// <summary>Some of it moved, and the rest is where it started.</summary>
    Partial = 2,

    /// <summary>Nothing moved, and nothing was supposed to: the fire was full,
    /// the depot was empty, there was no room. An ordinary answer.</summary>
    Declined = 3,

    /// <summary>Refused before any mutation, on a check.</summary>
    Refused = 4,

    /// <summary>A mutation may or may not have happened and the measurements
    /// cannot say which. Recorded with evidence; never replayed, never
    /// compensated.</summary>
    Uncertain = 5,
}

/// <summary>What is about to move, written down <b>before</b> anything does.
///
/// This is the row that makes a crash survivable. Without it, a process that
/// died between the withdrawal and its receipt leaves a record that says
/// nothing happened and a chest that says otherwise, and there is no way to
/// tell that from a chest somebody emptied by hand.</summary>
internal readonly struct UpkeepIntent
{
    public UpkeepIntent(RequestId request, UpkeepStep step, string detail, int count)
    {
        if (request.IsEmpty)
        {
            throw new ArgumentException("An intent needs a request id.", nameof(request));
        }

        if (step == UpkeepStep.Unspecified)
        {
            throw new ArgumentException("An intent needs a step.", nameof(step));
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "An intent moves at least one unit.");
        }

        Request = request;
        Step = step;
        Detail = detail ?? string.Empty;
        Count = count;
    }

    /// <summary>The idempotence key. Deterministic from the job and the step
    /// number, so the same step of the same job produces the same id after a
    /// restart — which is what makes a replayed attempt recognisable as the
    /// same attempt rather than a new one.</summary>
    public RequestId Request { get; }

    public UpkeepStep Step { get; }

    /// <summary>The fuel item name for a withdrawal or deposit; the fire's key
    /// for a feed.</summary>
    public string Detail { get; }

    public int Count { get; }

    public override string ToString() =>
        Step + " " + Count.ToString(CultureInfo.InvariantCulture) + " (" + Detail + ") as " + Request;
}

/// <summary>What actually happened, written down after the mutations. The only
/// number that counts is the measured one.</summary>
internal readonly struct UpkeepReceipt
{
    public UpkeepReceipt(RequestId request, UpkeepStep step, UpkeepOutcome outcome, int moved, string evidence)
    {
        if (request.IsEmpty)
        {
            throw new ArgumentException("A receipt needs a request id.", nameof(request));
        }

        if (outcome == UpkeepOutcome.Unspecified)
        {
            throw new ArgumentException("A receipt needs an outcome.", nameof(outcome));
        }

        if (moved < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moved), moved, "Moved units cannot be negative.");
        }

        Request = request;
        Step = step;
        Outcome = outcome;
        Moved = moved;
        Evidence = evidence ?? string.Empty;
    }

    public RequestId Request { get; }

    public UpkeepStep Step { get; }

    public UpkeepOutcome Outcome { get; }

    /// <summary>Units that measurably moved. Zero for every outcome but
    /// <see cref="UpkeepOutcome.Completed"/> and
    /// <see cref="UpkeepOutcome.Partial"/>.</summary>
    public int Moved { get; }

    public string Evidence { get; }

    public override string ToString() =>
        Step + " " + Outcome + " x" + Moved.ToString(CultureInfo.InvariantCulture) +
        (Evidence.Length == 0 ? string.Empty : " (" + Evidence + ")");
}

/// <summary>The durable record of what the Steward moved.
///
/// Every mutating step writes an intent, mutates, then writes a receipt, in
/// that order and never another — the same ordering the settlement custody
/// executor fixes in CONTRACTS.md section 5.2, for the same reason: an intent
/// with no receipt is recoverable evidence, and a mutation with no intent is
/// not.
///
/// <b>A write that fails is a refusal, not a warning.</b> If the intent cannot
/// be written, nothing moves. If the receipt cannot be written, the step is
/// uncertain in memory and the next load decides from the record and the actual
/// inventories.</summary>
internal interface IUpkeepJournal
{
    /// <summary>False when the record could not be fully read, or cannot be
    /// written. No new step starts while it is false.</summary>
    bool IsWritable { get; }

    /// <summary>Persists an intent. False means nothing may move.</summary>
    bool TryRecordIntent(in UpkeepIntent intent);

    /// <summary>Persists a receipt. False means the step is uncertain.</summary>
    bool TryRecordReceipt(in UpkeepReceipt receipt);

    /// <summary>Intents with no receipt.
    ///
    /// On a healthy run this is empty or holds the one step in flight. After a
    /// crash it holds the step that was in flight when the process died, and
    /// <b>that step is never re-run</b>: the loop reconciles against what the
    /// Steward is actually carrying and abandons the job.</summary>
    IReadOnlyList<UpkeepIntent> UnresolvedIntents { get; }

    /// <summary>Forgets resolved rows once a run is closed and balanced, so the
    /// record does not grow without bound. Unresolved rows are never dropped.
    /// </summary>
    void Compact();
}

/// <summary>A journal that keeps everything in memory.
///
/// The default, and the one the tests drive. It is a real implementation of the
/// ordering rules rather than a stub: the file-backed store wraps one of these
/// and adds durability, so the ordering logic is exercised identically in both
/// places.</summary>
internal sealed class MemoryUpkeepJournal : IUpkeepJournal
{
    private readonly List<UpkeepIntent> _open = new List<UpkeepIntent>();
    private readonly List<string> _lines = new List<string>();
    private readonly List<UpkeepReceipt> _receipts = new List<UpkeepReceipt>();

    /// <summary>Set by a test, or by a store whose file went read-only.</summary>
    public bool Writable { get; set; } = true;

    /// <summary>Set by a test: the next write of this kind fails once, the way
    /// a disk full or a file lock does. The fault-injection seam the evidence
    /// requires.</summary>
    public UpkeepStep? FailNextIntentFor { get; set; }

    public UpkeepStep? FailNextReceiptFor { get; set; }

    public bool IsWritable => Writable;

    public IReadOnlyList<UpkeepIntent> UnresolvedIntents => _open;

    /// <summary>Every row still standing, in order, for assertions about
    /// ordering. Cleared by <see cref="Compact"/>, exactly as the file-backed
    /// store drops resolved rows.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Every receipt this journal was ever asked to write, kept
    /// forever.
    ///
    /// <b>The real store does not keep this and should not.</b> Its file is
    /// recovery state, not an audit log: a resolved step is dropped, because
    /// keeping it would grow the file without bound to answer a question
    /// nothing asks, and its evidence sentence is already in the game log. The
    /// in-memory journal keeps the list anyway so a test can assert what was
    /// written <i>before</i> compaction discarded it — an assertion that would
    /// otherwise be impossible to make without bending the real behaviour to
    /// suit the test.</summary>
    public IReadOnlyList<UpkeepReceipt> Receipts => _receipts;

    public bool TryRecordIntent(in UpkeepIntent intent)
    {
        if (!Writable)
        {
            return false;
        }

        if (FailNextIntentFor == intent.Step)
        {
            FailNextIntentFor = null;
            return false;
        }

        _open.Add(intent);
        _lines.Add("intent\t" + intent.Request.Value + "\t" + intent.Step + "\t" +
            intent.Detail + "\t" + intent.Count.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    public bool TryRecordReceipt(in UpkeepReceipt receipt)
    {
        if (!Writable)
        {
            return false;
        }

        if (FailNextReceiptFor == receipt.Step)
        {
            FailNextReceiptFor = null;
            return false;
        }

        for (int index = _open.Count - 1; index >= 0; index--)
        {
            if (_open[index].Request.Equals(receipt.Request))
            {
                _open.RemoveAt(index);
                break;
            }
        }

        _lines.Add("receipt\t" + receipt.Request.Value + "\t" + receipt.Step + "\t" +
            receipt.Outcome + "\t" + receipt.Moved.ToString(CultureInfo.InvariantCulture) + "\t" +
            receipt.Evidence);
        _receipts.Add(receipt);
        return true;
    }

    public void Compact()
    {
        _lines.Clear();
        foreach (UpkeepIntent intent in _open)
        {
            _lines.Add("intent\t" + intent.Request.Value + "\t" + intent.Step + "\t" +
                intent.Detail + "\t" + intent.Count.ToString(CultureInfo.InvariantCulture));
        }
    }
}
