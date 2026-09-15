using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Tools;

internal enum ToolHoldingState
{
    /// <summary>The player gave it and the worker has it. The only state a
    /// return is owed from.</summary>
    Held = 0,

    /// <summary>Given back to the player. Terminal.</summary>
    Returned = 1,

    /// <summary>A transfer was started and its outcome is unknown: this build
    /// does not know whether the item left the player's hands. Terminal for
    /// automatic handling, exactly as an interrupted material commit is —
    /// guessing either way loses a tool or duplicates one.</summary>
    Uncertain = 2,
}

internal enum ToolOutcome
{
    /// <summary>The ledger changed.</summary>
    Applied = 0,

    /// <summary>Already in this state. Nothing changed and nothing is wrong —
    /// this is what a retried handover looks like.</summary>
    AlreadySatisfied = 1,

    /// <summary>Refused: unknown transaction, or a settled holding being
    /// settled a second, different way.</summary>
    Rejected = 2,
}

/// <summary>One tool a worker is holding on the player's behalf.</summary>
internal sealed class ToolHolding
{
    internal ToolHolding(RequestId transaction, WorkerId worker, ToolSpecimen tool)
    {
        if (transaction.IsEmpty)
        {
            throw new ArgumentException("A tool handover needs a transaction id.", nameof(transaction));
        }

        if (worker.IsEmpty)
        {
            throw new ArgumentException("A tool handover needs a worker.", nameof(worker));
        }

        if (tool.IsEmpty)
        {
            throw new ArgumentException("A tool handover needs a tool.", nameof(tool));
        }

        Transaction = transaction;
        Worker = worker;
        Tool = tool;
    }

    public RequestId Transaction { get; }

    public WorkerId Worker { get; }

    public ToolSpecimen Tool { get; }

    public ToolHoldingState State { get; private set; } = ToolHoldingState.Held;

    public bool IsSettled => State != ToolHoldingState.Held;

    internal bool TrySettle(ToolHoldingState settled)
    {
        if (settled == ToolHoldingState.Held || State == settled || State != ToolHoldingState.Held)
        {
            // Never un-settle, never re-settle a different way. Changing a
            // settled holding is how one axe becomes two.
            return false;
        }

        State = settled;
        return true;
    }

    public override string ToString()
    {
        return Transaction.Value + " " + State + " " + Worker.Value + " " + Tool;
    }
}

/// <summary>Which real tools a worker is holding, and on whose behalf.
///
/// <b>The player's axe is still the player's axe.</b> A handover moves one item
/// instance from one inventory to another; it does not create a worker-owned
/// copy, and this ledger exists so that the move can be undone exactly once and
/// never twice. Every operation is keyed by a transaction id and is idempotent,
/// so a retry after a crash, a relog or a full inventory repeats the
/// <i>request</i> rather than the <i>effect</i>.
///
/// It is deliberately the same shape as the material custody ledger, including
/// the <see cref="ToolHoldingState.Uncertain"/> state for a transfer whose two
/// halves cannot be made atomic against a game. That design has already been
/// reviewed and adversarially tested for material; a tool is the same problem
/// with a smaller number, and inventing a second mechanism for it would mean
/// two places to get conservation wrong.
///
/// What this type deliberately cannot do: describe a tool well enough to build
/// one. See <see cref="ToolSpecimen"/>.</summary>
internal sealed class ToolLedger
{
    private readonly Dictionary<string, ToolHolding> _holdings =
        new Dictionary<string, ToolHolding>(StringComparer.Ordinal);
    private readonly List<string> _order = new List<string>();

    public IReadOnlyList<ToolHolding> Holdings
    {
        get
        {
            var ordered = new List<ToolHolding>(_order.Count);
            foreach (string key in _order)
            {
                ordered.Add(_holdings[key]);
            }

            return ordered;
        }
    }

    public bool TryGet(RequestId transaction, out ToolHolding holding)
    {
        if (transaction.IsEmpty)
        {
            holding = null!;
            return false;
        }

        return _holdings.TryGetValue(transaction.Value, out holding!);
    }

    /// <summary>Records that a real tool has moved to a worker.
    ///
    /// A duplicate transaction id reports
    /// <see cref="ToolOutcome.AlreadySatisfied"/> and records nothing further,
    /// which is what makes a retried handover safe: the adapter may not know
    /// whether its previous attempt reached disk, and does not have to.</summary>
    public ToolOutcome Issue(ToolHolding holding)
    {
        if (holding == null)
        {
            throw new ArgumentNullException(nameof(holding));
        }

        if (_holdings.ContainsKey(holding.Transaction.Value))
        {
            return ToolOutcome.AlreadySatisfied;
        }

        _holdings.Add(holding.Transaction.Value, holding);
        _order.Add(holding.Transaction.Value);
        return ToolOutcome.Applied;
    }

    /// <summary>Gives a tool back to the player. Once.</summary>
    public ToolOutcome Return(RequestId transaction) =>
        Settle(transaction, ToolHoldingState.Returned);

    /// <summary>Marks a handover whose outcome is unknown.
    ///
    /// Neither held nor returnable: this build does not know whether the item
    /// left the player's inventory, and inventing an answer would either lose a
    /// tool or duplicate one. A person decides.</summary>
    public ToolOutcome MarkUncertain(RequestId transaction) =>
        Settle(transaction, ToolHoldingState.Uncertain);

    private ToolOutcome Settle(RequestId transaction, ToolHoldingState settled)
    {
        if (transaction.IsEmpty
            || !_holdings.TryGetValue(transaction.Value, out ToolHolding? holding))
        {
            return ToolOutcome.Rejected;
        }

        if (holding!.State == settled)
        {
            return ToolOutcome.AlreadySatisfied;
        }

        return holding.TrySettle(settled) ? ToolOutcome.Applied : ToolOutcome.Rejected;
    }

    /// <summary>Everything this worker is currently holding.</summary>
    public IReadOnlyList<ToolHolding> HeldBy(WorkerId worker)
    {
        var held = new List<ToolHolding>();
        if (worker.IsEmpty)
        {
            return held;
        }

        foreach (string key in _order)
        {
            ToolHolding holding = _holdings[key];
            if (holding.Worker.Equals(worker) && holding.State == ToolHoldingState.Held)
            {
                held.Add(holding);
            }
        }

        return held;
    }

    /// <summary>The tool of this kind the worker is holding, if any.
    ///
    /// The <b>first</b> one, in issue order, when more than one was handed over.
    /// Deterministic on purpose: which axe a worker swings must not depend on
    /// dictionary iteration order.</summary>
    public bool TryGetHeld(WorkerId worker, ToolKind kind, out ToolHolding holding)
    {
        holding = null!;
        if (kind == ToolKind.None)
        {
            return false;
        }

        foreach (ToolHolding candidate in HeldBy(worker))
        {
            if (candidate.Tool.Kind == kind)
            {
                holding = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>True when any handover is in an unknown state, so the runtime
    /// must stop and say so rather than continue.</summary>
    public bool HasUncertainHandover
    {
        get
        {
            foreach (string key in _order)
            {
                if (_holdings[key].State == ToolHoldingState.Uncertain)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>How many tools are in each state. The conservation tests compare
    /// these before and after forced failures — a tool must never appear twice
    /// and must never vanish.</summary>
    public int CountIn(ToolHoldingState state)
    {
        int count = 0;
        foreach (string key in _order)
        {
            if (_holdings[key].State == state)
            {
                count++;
            }
        }

        return count;
    }
}
