using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedForeman.Runtime.Settlement;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Tools;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Custody;

/// <summary>The custody subcommands of <c>cf_settle</c>: development aids for
/// handing Thorstein his tools, taking them back, answering what the record
/// could not settle, and seeing what reconciliation finds. Final player
/// controls are agent E's; these grant nothing of their own — every act goes
/// through the work authority answer and the same game-free procedures the
/// runtime uses.</summary>
internal sealed class CustodyTools
{
    /// <summary>How close the player stands to Thorstein to hand him a tool or
    /// take one back.</summary>
    private const float HandoverReach = 4f;

    private readonly SettlementRuntime _runtime;
    private readonly ForemanCustodyRuntime _custody;
    private readonly SettlementRecords _records;

    internal CustodyTools(SettlementRuntime runtime, ForemanCustodyRuntime custody, SettlementRecords records)
    {
        _runtime = runtime;
        _custody = custody;
        _records = records;
    }

    /// <summary><c>cf_settle give axe|hammer</c>: the tool the player is holding
    /// in hand, which must be the named kind — an explicit selection twice over.
    /// </summary>
    internal string Give(SettlementJournal journal, string[]? args)
    {
        if (!_runtime.HasWorkAuthority())
        {
            return "Refused: " + _runtime.DescribeMissingAuthority() + ".";
        }

        if (args == null || args.Length < 2 || !TryParseKind(args[1], out ToolKind kind))
        {
            return "Usage: cf_settle give axe|hammer — hold the tool in your hand, stand next to Thorstein.";
        }

        if (!TryHandoverParties(out Player player, out Humanoid worker, out string refusal))
        {
            return refusal;
        }

        ItemDrop.ItemData? selected = null;
        foreach (ItemDrop.ItemData? held in new[] { player.RightItem, player.LeftItem })
        {
            if (held != null && ToolClassifier.Classify(held) == kind)
            {
                selected = held;
                break;
            }
        }

        if (selected == null)
        {
            return "Hold the " + kind.ToString().ToLowerInvariant() + " you want to give him in your hand first. " +
                "Nothing was taken.";
        }

        var transaction = new RequestId("give-" + journal.NextSequence.ToString(CultureInfo.InvariantCulture));
        ToolLedger ledger = journal.Replay().Tools;

        ToolHandover.TryGive(
            player, worker, selected, _custody.ToolWorker, transaction, ledger, journal, _records.TryPersistJournal,
            _custody.Stamp(), out ToolSpecimen _, out string message);
        return message + " (request " + transaction.Value + ")";
    }

    /// <summary><c>cf_settle takeback [axe|hammer]</c>: every tool the record
    /// says he holds for you (of that kind), found by kind and identity in his
    /// own inventory.</summary>
    internal string TakeBack(SettlementJournal journal, string[]? args)
    {
        if (!_runtime.HasWorkAuthority())
        {
            return "Refused: " + _runtime.DescribeMissingAuthority() + ".";
        }

        ToolKind only = ToolKind.None;
        if (args != null && args.Length > 1 && !TryParseKind(args[1], out only))
        {
            return "Usage: cf_settle takeback [axe|hammer].";
        }

        if (!TryHandoverParties(out Player player, out Humanoid worker, out string refusal))
        {
            return refusal;
        }

        ToolLedger ledger = journal.Replay().Tools;
        var messages = new List<string>();
        foreach (ToolHolding holding in ledger.HeldBy(_custody.ToolWorker))
        {
            if (only != ToolKind.None && holding.Tool.Kind != only)
            {
                continue;
            }

            ItemDrop.ItemData? item = FindIssued(worker, holding);
            if (item == null)
            {
                messages.Add("His " + holding.Tool.Kind.ToString().ToLowerInvariant() + " (request " +
                    holding.Transaction.Value + ") is not in his inventory. Nothing was moved; run cf_settle reconcile.");
                continue;
            }

            ToolHandover.TryReturn(
                worker, player, item, holding.Transaction, ledger, journal, _records.TryPersistJournal,
                _custody.Stamp(), out string message);
            messages.Add(message);
        }

        return messages.Count == 0
            ? "He is not holding any tool you gave him."
            : string.Join(" ", messages.ToArray());
    }

    /// <summary>The material answers to <c>cf_settle resolve</c>:
    /// <c>&lt;request&gt; source</c>, <c>&lt;request&gt; destination [count]</c>, and
    /// <c>&lt;order&gt; lost</c> for the shortfalls reconciliation reports.</summary>
    internal string Resolve(string id, string answer, string[] args)
    {
        CustodyCore? core = _custody.Core;
        if (core == null)
        {
            return "Custody is not open for this world yet.";
        }

        switch (answer)
        {
            case "source":
            case "destination":
            {
                RequestId request;
                try
                {
                    request = new RequestId(id);
                }
                catch (ArgumentException)
                {
                    return "That is not a request reference. Run cf_settle reconcile to see the ones waiting.";
                }

                TransferSide side = answer == "source" ? TransferSide.Source : TransferSide.Destination;
                int units = 0;
                if (side == TransferSide.Destination)
                {
                    if (core.Ledger.TryGetTransfer(request, out TransferRecord transfer))
                    {
                        units = transfer.Intent.Count;
                    }

                    if (args.Length > 3 && (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out units) || units < 1))
                    {
                        return "Say how many arrived as a whole number, or leave it out when all of them did.";
                    }
                }

                core.Resolve(request, side, units, "answered at the console", out string message);
                return message;
            }

            case "lost":
            {
                OrderId order;
                try
                {
                    order = new OrderId(id);
                }
                catch (ArgumentException)
                {
                    return "That is not an order reference.";
                }

                ReconciliationReport? report = _custody.Reconcile(ordersWereRunning: false);
                if (report == null)
                {
                    return "Nothing can be checked now.";
                }

                IReadOnlyList<ReconciliationFinding> shortfalls = report.ShortfallsFor(order);
                if (shortfalls.Count == 0)
                {
                    return "Nothing is missing for order " + order.Value + " among what can be checked now.";
                }

                var messages = new List<string>();
                foreach (ReconciliationFinding shortfall in shortfalls)
                {
                    int missing = shortfall.Expected - (shortfall.Actual ?? shortfall.Expected);
                    int held = core.Ledger.HoldingAt(order, shortfall.Location, shortfall.Item);
                    int count = Math.Min(missing, held);
                    if (count < 1)
                    {
                        continue;
                    }

                    core.RecordLoss(order, shortfall.Location, shortfall.Item, count, "recorded as lost at the console", out string message);
                    messages.Add(message);
                }

                return messages.Count == 0 ? "Nothing to record." : string.Join(" ", messages.ToArray());
            }

            default:
                return "Answer a transfer with \"source\" (it did not move) or \"destination [count]\" (it arrived), " +
                    "a tool handover with \"mine\" or \"his\", or an order's shortfall with \"lost\".";
        }
    }

    /// <summary><c>cf_settle reconcile</c>: what the record expects against
    /// what can be checked now. Writes nothing.</summary>
    internal string Reconcile()
    {
        ReconciliationReport? report = _custody.Reconcile(ordersWereRunning: false);
        if (report == null)
        {
            return "Custody is not open for this world yet.";
        }

        var text = new StringBuilder();
        text.Append(report.AllMatch
            ? "Everything the record holds matches what can be checked now."
            : "Reconciliation:");
        foreach (ReconciliationFinding finding in report.Findings)
        {
            if (finding.NeedsAttention)
            {
                text.Append(Environment.NewLine).Append("  ").Append(finding.Sentence);
            }
        }

        WorkerBodyCensus? census = _custody.Census;
        if (census != null && census.IsDuplicated)
        {
            text.Append(Environment.NewLine).Append(
                "  Two bodies in this world carry Thorstein's identity. Neither is used, and neither is removed " +
                "automatically.");
        }

        return text.ToString();
    }

    /// <summary>Custody lines for <c>cf_settle status</c>.</summary>
    internal string Status()
    {
        CustodyCore? core = _custody.Core;
        if (core == null)
        {
            return "Custody: not open for this world.";
        }

        var text = new StringBuilder();
        text.Append("Custody: ");
        text.Append(core.WriteBlock == CustodyWriteBlock.None ? "writable" : "not taking new work (" + core.WriteBlock + ")");
        text.Append(", revision ").Append(core.Ledger.Revision.ToString(CultureInfo.InvariantCulture)).Append('.');

        foreach (CollectionOrderRecord order in core.Ledger.Orders)
        {
            text.Append(Environment.NewLine).Append("  order ").Append(order.Order.Value).Append(": ").Append(order.State);
            if (order.State == CollectionOrderState.NeedsAttention || order.State == CollectionOrderState.Paused)
            {
                text.Append(" (").Append(order.Reason).Append(')');
            }

            foreach (ResourceQuota quota in order.Definition.Quotas)
            {
                ResourceProgress progress = core.Ledger.ProgressFor(order.Definition, quota.Resource);
                text.Append(Environment.NewLine).Append(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {0}: requested {1}, carried {2}, in cart {3}, delivered {4}, handed over {5}, lost {6}",
                    quota.Resource, progress.Requested, progress.Carried, progress.InCart, progress.Delivered,
                    progress.HandedOver, progress.Lost));
            }
        }

        return text.ToString();
    }

    private bool TryHandoverParties(out Player player, out Humanoid worker, out string refusal)
    {
        player = Player.m_localPlayer;
        worker = null!;
        if (player == null)
        {
            refusal = "Refused: there is no local player.";
            return false;
        }

        ForemanWorkerAI? ai = _runtime.Worker;
        WorkerBody? body = ai == null ? null : ai.GetComponent<WorkerBody>();
        if (ai == null || body == null || !body.IsLoaded || body.Humanoid == null)
        {
            refusal = "Thorstein is not here: no worker body in loaded ground.";
            return false;
        }

        Vector3 flat = ai.transform.position - player.transform.position;
        flat.y = 0f;
        if (flat.magnitude > HandoverReach)
        {
            refusal = "Stand next to Thorstein first. Nothing was moved.";
            return false;
        }

        worker = body.Humanoid;
        refusal = string.Empty;
        return true;
    }

    private static ItemDrop.ItemData? FindIssued(Humanoid worker, ToolHolding holding)
    {
        Inventory inventory = worker.GetInventory();
        if (inventory == null)
        {
            return null;
        }

        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (ToolClassifier.TryDescribe(item, out ToolSpecimen live)
                && live.Kind == holding.Tool.Kind
                && string.Equals(live.ItemKey, holding.Tool.ItemKey, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private static bool TryParseKind(string text, out ToolKind kind)
    {
        switch ((text ?? string.Empty).ToLowerInvariant())
        {
            case "axe":
                kind = ToolKind.Axe;
                return true;
            case "hammer":
                kind = ToolKind.Hammer;
                return true;
            default:
                kind = ToolKind.None;
                return false;
        }
    }
}
