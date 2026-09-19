using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>The NPC's personal inventory, as the record knows it: what it
/// should be carrying, for whom, and whether that agrees with the body.
///
/// <b>Why this is a view and not a store.</b> An inventory kept here would be a
/// third answer to "what is the NPC carrying", beside the ledger and the body,
/// and three answers is worse than two. The ledger already records every unit
/// at <see cref="NpcCustodyPlace.Carried"/>; this is that, read back for the
/// two questions a role actually asks - "what am I carrying" and "does the body
/// agree" - and nothing else.
///
/// <b>The comparison never corrects anything.</b> A difference is returned as a
/// number. What to do about it is reconciliation's answer and, in the end, a
/// person's.</summary>
internal static class NpcCarriedInventory
{
    /// <summary>What the record says this NPC is carrying for one job, by
    /// material, in the order the materials first appeared. Empty stacks are
    /// left out: an absent stack and a zero stack are the same thing, and
    /// having both is how a total gets counted twice.</summary>
    internal static IReadOnlyList<NpcMaterialStack> CarriedBy(
        NpcCustodyLedger ledger, string? jobId, NpcCustodyLocation body)
    {
        if (ledger == null)
        {
            throw new ArgumentNullException(nameof(ledger));
        }

        var stacks = new List<NpcMaterialStack>();
        foreach (NpcHolding holding in ledger.Holdings)
        {
            if (!holding.Location.Equals(body)
                || !string.Equals(holding.JobId, jobId ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            stacks.Add(new NpcMaterialStack(holding.Material, holding.Count));
        }

        return stacks;
    }

    /// <summary>Everything the record says is in this body, whichever job it
    /// belongs to - what a physical inventory should contain. The number to
    /// compare against a port, because a body carries one inventory and may
    /// carry it for more than one job over its life.</summary>
    internal static int ExpectedIn(NpcCustodyLedger ledger, NpcCustodyLocation body, NpcMaterial material)
    {
        if (ledger == null)
        {
            throw new ArgumentNullException(nameof(ledger));
        }

        return ledger.TotalAt(body, material);
    }

    /// <summary>How far the body differs from the record for one material:
    /// positive when it holds more than the record expects, negative when it
    /// holds less, and <b>null when the body could not be counted</b> - which
    /// is not zero, because zero is a shortfall and a shortfall is something a
    /// player is asked to write off.</summary>
    internal static int? Difference(
        NpcCustodyLedger ledger, NpcCustodyLocation body, NpcMaterial material, INpcInventoryPort? port)
    {
        if (port == null)
        {
            return null;
        }

        int actual;
        try
        {
            if (!port.IsAvailable)
            {
                return null;
            }

            actual = port.Count(material);
        }
        catch (Exception)
        {
            return null;
        }

        if (actual < 0)
        {
            return null;
        }

        return actual - ExpectedIn(ledger, body, material);
    }
}
