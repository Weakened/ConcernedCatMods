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

    /// <summary>Takes a holding out of <see cref="ToolHoldingState.Uncertain"/>
    /// on a person's say-so. The only way out, and only from there.</summary>
    internal bool TryResolve(bool workerHasIt)
    {
        if (State != ToolHoldingState.Uncertain)
        {
            return false;
        }

        State = workerHasIt ? ToolHoldingState.Held : ToolHoldingState.Returned;
        return true;
    }

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
/// never twice.
///
/// <b>IN MEMORY ONLY, and an earlier version of this comment claimed otherwise.</b>
/// It said a retry "after a crash, a relog or a full inventory" repeats the
/// request rather than the effect, and cited the material custody ledger as the
/// design being mirrored. The shape is mirrored; the thing that earns that
/// sentence is not. <c>CustodyLedger</c> is rebuilt by
/// <c>SettlementJournal.Replay()</c> from a persisted journal — there are no
/// tool entry kinds, no codec columns, no store and no replay, so this ledger
/// does not survive a session at all. Within one session every operation is
/// genuinely idempotent; across a restart the record of a real tool a player
/// handed over is simply gone, and a return could never be offered.
///
/// That is a data-loss path for somebody's actual axe, so **issuing a tool is
/// gated closed** until the persistence exists — see
/// <see cref="RequiresPersistence"/>. The gate is the honest interim: the
/// contract is right, and what is missing is missing visibly rather than
/// discovered by a player who relogged.
///
/// The <see cref="ToolHoldingState.Uncertain"/> state is still the material
/// ledger's, for a transfer whose two halves cannot be made atomic against a
/// game — but it is scoped to the worker it happened to, and it is resolvable.
/// An earlier version was neither, which meant one interrupted handover refused
/// every worker every job forever with no way out.</summary>
internal sealed class ToolLedger
{
    /// <summary>True while this build cannot persist a handover.
    ///
    /// Read by the adapter, which refuses to issue a tool while it is set. It is
    /// a constant rather than a setting because a player must not be able to
    /// turn off a guard against losing their own axe; it goes away when the
    /// journal learns to carry tool entries, and not before.</summary>
    public const bool RequiresPersistence = true;

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

        if (_holdings.TryGetValue(holding.Transaction.Value, out ToolHolding? recorded))
        {
            // Idempotent only for the SAME handover. A transaction id reused for
            // a different tool or a different worker is a caller bug, and
            // answering AlreadySatisfied would tell it the wrong thing happened
            // successfully.
            return recorded!.Worker.Equals(holding.Worker) && recorded.Tool.Equals(holding.Tool)
                ? ToolOutcome.AlreadySatisfied
                : ToolOutcome.Rejected;
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

    /// <summary>True when THIS worker has a handover in an unknown state.
    ///
    /// Scoped to the worker on purpose. An earlier version asked the question of
    /// the whole ledger, so one interrupted handover refused every worker every
    /// job — including bare-handed gathering that touches no tool at all. The
    /// material ledger parks one <i>order</i> for repair rather than the
    /// settlement; this parks one worker.</summary>
    public bool HasUncertainHandover(WorkerId worker)
    {
        if (worker.IsEmpty)
        {
            return false;
        }

        foreach (string key in _order)
        {
            ToolHolding holding = _holdings[key];
            if (holding.Worker.Equals(worker) && holding.State == ToolHoldingState.Uncertain)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when anybody has an unresolved handover. For reporting, not
    /// for gating work.</summary>
    public bool HasAnyUncertainHandover
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

    /// <summary>Resolves an interrupted handover, the way a person decided it
    /// actually went.
    ///
    /// <b>Only a person gets a holding out of Uncertain</b>, which is why this
    /// takes an explicit answer rather than working one out. It exists because
    /// the previous version had no way out at all: the player-facing text said
    /// "resolve it" and nothing could. The two answers are the only two there
    /// are — the worker really has it, or the player really still does.</summary>
    public ToolOutcome Resolve(RequestId transaction, bool workerHasIt)
    {
        if (transaction.IsEmpty
            || !_holdings.TryGetValue(transaction.Value, out ToolHolding? holding)
            || holding!.State != ToolHoldingState.Uncertain)
        {
            return ToolOutcome.Rejected;
        }

        return holding.TryResolve(workerHasIt) ? ToolOutcome.Applied : ToolOutcome.Rejected;
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
