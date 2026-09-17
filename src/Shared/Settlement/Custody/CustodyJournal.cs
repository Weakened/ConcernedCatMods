using System;
using System.Globalization;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>Where custody rows are written, behind a seam so the ordering of
/// every transfer can be tested with a journal that fails on command.</summary>
internal interface ICustodyJournal
{
    /// <summary>Loaded and this build may write it.</summary>
    bool IsWritable { get; }

    /// <summary>Appends one row and persists it. True only when the row is on
    /// disk. On false nothing is left behind: a row that did not reach disk is
    /// removed, so no later unrelated save can carry it there as a record of
    /// something that did not happen.</summary>
    bool TryRecord(CustodyRow row);

    /// <summary>Appends and persists a row with an explicit world time — a load
    /// restatement, or a marker owed from an earlier save.</summary>
    bool TryRecordAt(CustodyRow row, double worldTime);
}

/// <summary>The settlement journal as an <see cref="ICustodyJournal"/>.
///
/// <b>"Persisted" is decided by the journal, not by the save call's answer.</b>
/// A save can report failure after the journal half reached disk (the register
/// half failed, or something threw after the replace). #299's final review found
/// exactly that: a caller told "nothing was written" about a row that was on
/// disk. So on a reported failure this discards the row with
/// <see cref="SettlementJournal.TryDiscardUnsaved"/>, and that method's refusal
/// — it will not remove anything that reached disk — is how a row that was
/// saved after all is recognised as saved.</summary>
internal sealed class SettlementCustodyJournal : ICustodyJournal
{
    private readonly Func<bool> _persist;
    private readonly Func<double> _worldTime;

    public SettlementCustodyJournal(SettlementJournal journal, Func<bool> persist, Func<double> worldTime, Guid loadEpoch)
    {
        Journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _worldTime = worldTime ?? throw new ArgumentNullException(nameof(worldTime));

        if (loadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A custody journal writes for one world load.", nameof(loadEpoch));
        }

        LoadEpoch = loadEpoch;
    }

    public SettlementJournal Journal { get; }

    public Guid LoadEpoch { get; }

    public bool IsWritable => !Journal.IsReadOnly;

    /// <summary>The current world time, or null when it could not be read.
    /// </summary>
    public double? Now()
    {
        try
        {
            double time = _worldTime();
            return double.IsNaN(time) || double.IsInfinity(time) ? (double?)null : time;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool TryRecord(CustodyRow row)
    {
        double? time = Now();
        return time.HasValue && TryRecordAt(row, time.Value);
    }

    public bool TryRecordAt(CustodyRow row, double worldTime)
    {
        if (row == null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        if (Journal.IsReadOnly)
        {
            return false;
        }

        int before = Journal.Entries.Count;
        Journal.AppendCustody(row, worldTime, LoadEpoch);

        if (TryPersist(_persist))
        {
            return true;
        }

        // Refused only when the row is already on disk: then it IS recorded.
        return !Journal.TryDiscardUnsaved(before);
    }

    /// <summary>Calls a save and turns any answer we did not get into "no". A
    /// save that throws established exactly as much as one that returned
    /// false.</summary>
    internal static bool TryPersist(Func<bool> persist)
    {
        try
        {
            return persist();
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Request ids the custody runtime mints.
///
/// <b>Unique for the life of the journal.</b> The ledger's revision counts
/// every custody row the record holds, voided ones included, so it never goes
/// backwards across a reload; an id built from it is never reused, even after a
/// crash rolled the world back. That matters because the ledger answers a
/// reused id with a different payload as a caller bug, not as a fresh request.
/// </summary>
internal static class CustodyIds
{
    /// <summary><c>order-tag-revision</c>, shortening the order part when the
    /// whole would exceed a slug. The revision keeps it unique; the order part is
    /// only there so a person reading the record can tell whose it is.</summary>
    public static RequestId Mint(OrderId order, string tag, int revision)
    {
        if (order.IsEmpty)
        {
            throw new ArgumentException("An id belongs to an order.", nameof(order));
        }

        if (!SettlementSlug.IsValid(tag) || revision < 0)
        {
            throw new ArgumentException("A tag is a slug and a revision is not negative.");
        }

        string suffix = "-" + tag + "-" + revision.ToString(CultureInfo.InvariantCulture);
        string prefix = order.Value;
        int room = SettlementSlug.MaxLength - suffix.Length;
        if (room < 1)
        {
            throw new ArgumentException("The tag is too long for an id.", nameof(tag));
        }

        if (prefix.Length > room)
        {
            prefix = prefix.Substring(0, room).TrimEnd('-');
        }

        return new RequestId(prefix + suffix);
    }

    /// <summary>The id a transfer planned against <paramref name="view"/> should
    /// carry. Consumers (the collection loop, cooperation) build their intents
    /// with it immediately before executing, so the revision they pass is the
    /// one the ledger still has.</summary>
    public static RequestId ForTransfer(OrderId order, IMaterialCustodyView view)
    {
        if (view == null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        return Mint(order, "t", view.Revision);
    }
}
