using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep;

namespace TheConcernedCat.ConcernedSteward.Domain.Persistence;

/// <summary>An <see cref="IUpkeepJournal"/> whose open steps reach the disk.
///
/// <b>The whole file is rewritten on every change, and that is deliberate.</b>
/// The alternative — appending rows — is faster and is how a real journal
/// works, and it is the wrong shape here: the Steward's record is a handful of
/// lines, it is rewritten at most twice per piece of wood, and a rewrite through
/// <c>AtomicTextFile</c> is a whole-file swap that can never leave a half-row.
/// An append can, and then the next load has to decide what a truncated last
/// line meant.
///
/// <b>Memory always matches the disk.</b> Every method that fails to persist
/// undoes its own in-memory change before answering false, so the open set this
/// object reports is the open set a reload would find. Without that, a write
/// failure would leave the Steward believing a step was closed that the record
/// still says is open — the exact disagreement the record exists to prevent.
///
/// <b>Failing to write a receipt is not the same as failing to write an
/// intent.</b> An intent that will not persist means nothing may move. A
/// receipt that will not persist means something already moved and the record
/// cannot say what: the step stays open, the loop treats it as uncertain, and
/// the next load reconciles against his actual pack. Neither is retried.
/// </summary>
internal sealed class RecordBackedJournal : IUpkeepJournal
{
    private readonly List<UpkeepIntent> _open = new List<UpkeepIntent>();
    private readonly Func<IReadOnlyList<UpkeepIntent>, bool> _persist;

    /// <param name="persist">Writes the record with these open steps in it.
    /// False means the write did not happen.</param>
    internal RecordBackedJournal(Func<IReadOnlyList<UpkeepIntent>, bool> persist)
    {
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
    }

    /// <summary>Set from the load report: false when the file is damaged or
    /// could not be read, and then no step starts at all.</summary>
    public bool Writable { get; set; } = true;

    public bool IsWritable => Writable;

    public IReadOnlyList<UpkeepIntent> UnresolvedIntents => _open;

    /// <summary>Takes back the open steps a load found, so the loop sees what
    /// the previous session left behind.</summary>
    internal void Restore(IReadOnlyList<UpkeepIntent> open)
    {
        _open.Clear();
        if (open != null)
        {
            _open.AddRange(open);
        }
    }

    public bool TryRecordIntent(in UpkeepIntent intent)
    {
        if (!Writable)
        {
            return false;
        }

        _open.Add(intent);
        if (Persist())
        {
            return true;
        }

        _open.RemoveAt(_open.Count - 1);
        return false;
    }

    public bool TryRecordReceipt(in UpkeepReceipt receipt)
    {
        if (!Writable)
        {
            return false;
        }

        int index = IndexOf(receipt.Request.Value);
        if (index < 0)
        {
            // Nothing to close. Either this step was already closed or it was
            // never opened; both are answered the same way, because a receipt
            // is a removal and removing nothing succeeds.
            return true;
        }

        UpkeepIntent removed = _open[index];
        _open.RemoveAt(index);
        if (Persist())
        {
            return true;
        }

        _open.Insert(index, removed);
        return false;
    }

    public void Compact()
    {
        // Nothing to compact: only open steps are ever held. The call still
        // persists, so a record left holding a step that has since been closed
        // in memory is brought back into line.
        if (Writable)
        {
            Persist();
        }
    }

    private int IndexOf(string request)
    {
        for (int index = _open.Count - 1; index >= 0; index--)
        {
            if (string.Equals(_open[index].Request.Value, request, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private bool Persist()
    {
        try
        {
            return _persist(_open);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
