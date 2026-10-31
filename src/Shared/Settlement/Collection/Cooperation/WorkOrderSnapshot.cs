using System;
using System.Collections.Generic;
using TheConcernedCat.Interop.WorkOrders;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>#296: what Foreman publishes on <c>concernedcat.workorders/1</c>,
/// built from the very facts and view its own collection panel shows, so the
/// map and the panel can never say different things.
///
/// Only an order working in a Concerned Cartographer work area that still has
/// its boundary is published: a harvest-area or camp-circle order has no map
/// area to show it on, and a recovered order that has not been rebound is not
/// working anywhere yet. A finished or cancelled order is not listed.</summary>
internal static class WorkOrderSnapshot
{
    public const string WorkerName = "Thorstein";

    /// <summary>Null when no world is loaded (the contract says Unavailable,
    /// never "no orders").</summary>
    public static PublishedWorkOrders? From(string worldUid, OrderPanelFacts? facts, OrderPanelView? view)
    {
        if (string.IsNullOrEmpty(worldUid))
        {
            return null;
        }

        CollectionOrderDefinition? order = facts?.Order;
        if (facts == null || view == null || order == null || CollectionOrderStates.IsTerminal(facts.State) ||
            order.Scope.Source != WorkScopeSource.CartographerWorkArea || order.Scope.IsMissingBoundary)
        {
            return new PublishedWorkOrders(worldUid, Array.Empty<PublishedWorkOrder>());
        }

        return new PublishedWorkOrders(worldUid, new[]
        {
            new PublishedWorkOrder(
                order.Scope.AreaId,
                WorkerName,
                view.StateLine,
                view.ReasonLine,
                NeedsAttention(facts),
                new List<string>(view.ProgressLines)),
        });
    }

    /// <summary>Stopped for a reason the player did not choose, or unable to
    /// work at all because the settlement runtime is off or work is not
    /// authorised here (the panel's reason line already says which).</summary>
    public static bool NeedsAttention(OrderPanelFacts facts) =>
        facts.State == CollectionOrderState.NeedsAttention ||
        (facts.State == CollectionOrderState.Paused && !facts.PausedByPlayer && facts.Reason != CollectionAttentionReason.Unspecified) ||
        (facts.Order != null && !CollectionOrderStates.IsTerminal(facts.State) &&
            (!facts.RuntimeEnabled || facts.Authority != TheConcernedCat.Workers.WorkAuthorityVerdict.Granted));
}
