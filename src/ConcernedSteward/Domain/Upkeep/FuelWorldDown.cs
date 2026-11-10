using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>What became of a loss nobody had acknowledged when her world went
/// down. Zero means nobody said.</summary>
internal enum FuelWorldDownLoss
{
    Unspecified = 0,

    /// <summary>There was no loss to keep.</summary>
    NoLoss = 1,

    /// <summary>That world's record holds it - it already did, or it was
    /// written at the down edge. It comes back when that world loads.</summary>
    OnRecord = 2,

    /// <summary>That world's record could not be written. The loss is kept in
    /// memory against that world for the rest of the session and restored the
    /// next time it loads; it is in the log, and only there, across a game exit.
    /// </summary>
    KeptForThatWorld = 3,

    /// <summary>No world could be named to keep it against. In the log only.
    /// </summary>
    InLogOnly = 4,
}

/// <summary>What one world-down did with her run.</summary>
internal readonly struct FuelWorldDownResult
{
    public FuelWorldDownResult(long worldId, FuelRunAtWorldDown run, FuelWorldDownLoss loss)
    {
        WorldId = worldId;
        Run = run;
        Loss = loss;
    }

    public long WorldId { get; }

    public FuelRunAtWorldDown Run { get; }

    public FuelWorldDownLoss Loss { get; }

    /// <summary>One line for the log, or empty when nothing of the run can
    /// still matter.</summary>
    public string Describe()
    {
        if (Run.IsSettled)
        {
            return string.Empty;
        }

        string text = "Her world went down with a run unsettled (" + Run.Describe() + "). " +
            "The run ended with that world and the next one starts fresh.";
        if (Run.Carried > 0)
        {
            text += " The " + Run.Carried.ToString(CultureInfo.InvariantCulture) +
                " in her pack stay in her pack in that world and are counted again when it loads; " +
                "nothing was written off.";
        }

        switch (Loss)
        {
            case FuelWorldDownLoss.OnRecord:
                text += " The " + Run.Unaccounted.ToString(CultureInfo.InvariantCulture) +
                    " that could not be accounted for are in that world's record and come back when it loads.";
                break;
            case FuelWorldDownLoss.KeptForThatWorld:
                text += " The " + Run.Unaccounted.ToString(CultureInfo.InvariantCulture) +
                    " that could not be accounted for could NOT be written to that world's record. They are kept " +
                    "for that world until the game closes and come back if it loads again; after the game closes " +
                    "this line is the only record of them.";
                break;
            case FuelWorldDownLoss.InLogOnly:
                text += " The " + Run.Unaccounted.ToString(CultureInfo.InvariantCulture) +
                    " that could not be accounted for belong to no world that could be named, so this line is " +
                    "the only record of them.";
                break;
        }

        return text;
    }
}

/// <summary>The lead default for what her run becomes when its world goes down
/// (#376, third slice), <b>pending owner confirmation</b>
/// (docs/mods/concerned-npc/ARCHITECTURE.md section 10, lead default 8). The
/// one place to change it.
///
/// <b>The run ends with its world, unconditionally.</b> Her ledger is replaced
/// (<see cref="FuelCustody.CloseForWorld"/>), so nothing of one world is ever
/// counted against another's depot. What it still held is kept the way she
/// already keeps it, and never by asserting anything that was not measured:
///
/// <list type="bullet">
/// <item><b>Fuel in her pack</b> stays in her pack in that world. Her body's
/// inventory saves with the world and her durable plan has it suspended; the
/// next load of that world counts her pack and corrects the record to it
/// (<see cref="FuelCustody.RestateCarried"/>). It is not called lost, which
/// nothing measured.</item>
/// <item><b>A loss nobody acknowledged</b> is her loss ledger's: that world's
/// record. If the record was not last written with it, it is written once more
/// before the ledger closes. If that fails it is said, and kept in memory
/// against that world - restored the next time it loads, where she stops until
/// a person resolves it - and never written into another world's record or
/// written off.</item>
/// </list></summary>
internal sealed class FuelWorldDownCarry
{
    private readonly Dictionary<long, int> _unrecorded = new Dictionary<long, int>();

    /// <summary>Closes the run for the world that went down.</summary>
    /// <param name="worldId">The world the run belonged to; zero when none
    /// could be named.</param>
    /// <param name="custody">Her ledger, still holding the run.</param>
    /// <param name="lossOnRecord">The loss that world's record was last
    /// written with.</param>
    /// <param name="tryWriteLoss">Writes that world's record with the ledger
    /// as it stands; true when it was written. Asked only when the record is
    /// behind the ledger's loss, and before the ledger is closed.</param>
    public FuelWorldDownResult Close(long worldId, FuelCustody custody, int lossOnRecord, Func<bool> tryWriteLoss)
    {
        if (custody == null)
        {
            throw new ArgumentNullException(nameof(custody));
        }

        if (tryWriteLoss == null)
        {
            throw new ArgumentNullException(nameof(tryWriteLoss));
        }

        int unaccounted = custody.Unaccounted;
        bool onRecord = unaccounted <= lossOnRecord;
        if (!onRecord && worldId != 0L)
        {
            bool written;
            try
            {
                written = tryWriteLoss();
            }
            catch (Exception)
            {
                // A write that threw is a write that did not land.
                written = false;
            }

            onRecord = written;
        }

        FuelRunAtWorldDown run = custody.CloseForWorld();
        FuelWorldDownLoss loss;
        if (run.Unaccounted == 0)
        {
            loss = FuelWorldDownLoss.NoLoss;
        }
        else if (onRecord)
        {
            loss = FuelWorldDownLoss.OnRecord;
        }
        else if (worldId == 0L)
        {
            loss = FuelWorldDownLoss.InLogOnly;
        }
        else
        {
            _unrecorded.TryGetValue(worldId, out int already);
            _unrecorded[worldId] = Math.Max(already, run.Unaccounted);
            loss = FuelWorldDownLoss.KeptForThatWorld;
        }

        return new FuelWorldDownResult(worldId, run, loss);
    }

    /// <summary>A world loaded: the loss to restore for it - the larger of
    /// what its record says and what this session kept for it because the
    /// record could not take it. What was kept is handed back once; from then
    /// on the ledger holds it again, and the next world-down keeps it again if
    /// it is still not on record.</summary>
    public int LossToRestore(long worldId, int recorded)
    {
        int restore = recorded < 0 ? 0 : recorded;
        if (_unrecorded.TryGetValue(worldId, out int kept))
        {
            _unrecorded.Remove(worldId);
            restore = Math.Max(restore, kept);
        }

        return restore;
    }

    /// <summary>The loss kept for a world whose record could not take it.
    /// </summary>
    public int KeptFor(long worldId) => _unrecorded.TryGetValue(worldId, out int kept) ? kept : 0;
}
