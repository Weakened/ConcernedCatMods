using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedForeman.Runtime.Construction;

/// <summary>The one container a player permitted this settlement to draw
/// building material out of: its in-session key and where it stands.
///
/// <b>Named, never found.</b> It comes from the settlement register's
/// <c>SupplyContainer</c> designation, which the player set by looking at a chest
/// and saying so. The loop never reaches for the nearest chest, because a
/// settlement that helps itself to whatever is nearby is not a settlement a
/// person can leave running.</summary>
internal readonly struct SupplyChest
{
    internal SupplyChest(string containerKey, SitePoint at)
    {
        ContainerKey = containerKey ?? string.Empty;
        At = at;
    }

    internal string ContainerKey { get; }

    internal SitePoint At { get; }

    /// <summary>Whether a chest is actually named.
    ///
    /// <b>Safe on a default struct</b>, which is the one value no caller means to
    /// hand over and the one a missing designation always produces: the
    /// auto-property is null there rather than empty, and a length check would
    /// turn "no chest is marked" into a null reference in the middle of a round.
    /// </summary>
    internal bool IsNamed => !string.IsNullOrEmpty(ContainerKey);
}

/// <summary>Building material against the running game: out of the permitted
/// supply chest, into Thorstein's own persisted inventory, and out again when a
/// piece has gone up.
///
/// <b>Both inventories are resolved through custody and nothing else.</b>
/// <see cref="ICustodyRuntime.TryResolveContainer"/> is what applies the ward,
/// the privacy setting, the guard stone, the reach and the world-load epoch to
/// the chest, and <see cref="ICustodyRuntime.TryResolveWorker"/> is what applies
/// the duplicate-body census and the body's own persistence to the worker. This
/// class asks those two questions and moves between the answers; it never finds
/// an inventory of its own.
///
/// <b>Why the move is written here instead of going through the transfer
/// executor - stated plainly, because it is a real gap and not a preference.</b>
/// <c>TransferExecutor</c> is the product's vetted mover and this is not it. Its
/// ledger check (<c>MaterialCustodyLedger.Check</c>) requires an accepted
/// <c>CollectionOrderDefinition</c> and requires the ORDER to already hold the
/// units at the source: the model is material an order gathered and is moving
/// onward, and it refuses a chest the order never filled
/// (<c>MayLeave</c> is false for a destination, and <c>HoldingAt</c> is zero for
/// a player's own chest). A build order draws in the opposite direction, so the
/// units cannot be journalled as a collection transfer without either lying
/// about what they are or changing the ledger - which is a durable-format change
/// and therefore its own issue.
///
/// <b>What that costs, exactly.</b> Build-order material movements are gated on
/// custody being writable, they are resolved through custody's own container and
/// worker ports, and every one of them is logged with its measured amounts - but
/// they are <b>not</b> rows in the custody journal, so they are not replayed or
/// reconciled on the next world load the way collection transfers are. What
/// makes them conserved instead is that both sides of every movement are real
/// engine inventories, one of which (the worker body) persists itself inside the
/// same call, and that the loop above only ever spends for a piece that is
/// actually standing. A crash mid-movement therefore leaves real items in real
/// inventories rather than a record that disagrees with them.
///
/// <b>The movement discipline is <c>TransferExecutor</c>'s, deliberately
/// repeated rather than reinvented.</b> Count both sides, move through vanilla's
/// own instance-preserving move, count both sides again, and classify from the
/// measured deltas - never from what an add or a remove returned, and never from
/// what was asked for. A delta pair that does not agree is reported as uncertain
/// and nothing is compensated, which is the only reading that cannot invent
/// material.</summary>
internal sealed class WorldBuildMaterials : IBuildMaterials
{
    private readonly ICustodyRuntime _custody;
    private readonly WorkerKey _worker;
    private readonly Func<SupplyChest> _supply;
    private readonly Func<IReadOnlyList<string>> _kinds;
    private readonly Action<string> _log;

    /// <param name="custody">Agent D's runtime: the only thing that resolves a
    /// container or the worker's inventory, and the writable-record gate.</param>
    /// <param name="worker">Whose inventory carries the material.</param>
    /// <param name="supply">The permitted container, read live. A designation
    /// replaced while an order runs is a different chest, and the next draw uses
    /// the one that is marked now.</param>
    /// <param name="kinds">The item prefab names this order can involve - the
    /// plan's own manifest. Used to decide what to count as carried and what to
    /// put back, so a tool in his hands or somebody else's stone is never
    /// counted as this order's wood.</param>
    internal WorldBuildMaterials(
        ICustodyRuntime custody,
        WorkerKey worker,
        Func<SupplyChest> supply,
        Func<IReadOnlyList<string>> kinds,
        Action<string> log)
    {
        _custody = custody ?? throw new ArgumentNullException(nameof(custody));
        _worker = worker;
        _supply = supply ?? throw new ArgumentNullException(nameof(supply));
        _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Set when a movement's two measured deltas disagreed. <b>Latched
    /// for the world load</b>: once this product cannot say where a unit went,
    /// it does not move another one and the order stops with the evidence.
    /// </summary>
    internal string? Uncertain { get; private set; }

    /// <inheritdoc />
    public bool TrySupply(out SitePoint at, out string refusal)
    {
        at = default;
        SupplyChest chest = _supply();
        if (!chest.IsNamed)
        {
            refusal = "no supply chest is marked for this settlement. Look at the chest the material " +
                "should come out of and run: cf_settle supply";
            return false;
        }

        at = chest.At;
        refusal = string.Empty;
        return true;
    }

    /// <inheritdoc />
    public MaterialTally Carried
    {
        get
        {
            var tally = new MaterialTally();
            if (!TryWorker(out IInventoryPort? worker, out string _))
            {
                return tally;
            }

            foreach (string kind in _kinds())
            {
                tally.Add(kind, Counted(worker!, kind));
            }

            return tally;
        }
    }

    /// <inheritdoc />
    public BuildDraw Draw(MaterialTally wanted)
    {
        if (Uncertain != null)
        {
            return BuildDraw.Refused(Uncertain);
        }

        if (!_custody.IsWritable)
        {
            return BuildDraw.Refused(
                "the settlement's record cannot be written now, so nothing is taken out of a chest");
        }

        if (!TryWorker(out IInventoryPort? worker, out string workerRefusal))
        {
            return BuildDraw.Refused(workerRefusal);
        }

        if (!TryChest(out IInventoryPort? chest, out string chestRefusal))
        {
            return BuildDraw.Refused(chestRefusal);
        }

        if (!Ready(worker!) || !Ready(chest!))
        {
            return BuildDraw.Refused(
                "one of the two inventories is not available now, so nothing is taken out of a chest");
        }

        var drawn = new MaterialTally();
        var shortBy = new MaterialTally();
        foreach (PieceCost line in wanted.Lines)
        {
            int moved = Move(chest!, worker!, line.Item, line.Amount);
            if (moved < 0)
            {
                return BuildDraw.Refused(Uncertain ?? "a movement could not be accounted for");
            }

            drawn.Add(line.Item, moved);
            shortBy.Add(line.Item, line.Amount - moved);
        }

        if (!drawn.IsEmpty)
        {
            _log("Build order: took " + drawn.Describe() + " out of " + chest!.Describe + " into " +
                worker!.Describe + ".");
        }

        return BuildDraw.Moved(drawn, shortBy);
    }

    /// <inheritdoc />
    public bool Spend(PieceRecipe recipe, out MaterialTally spent, out string failure)
    {
        spent = new MaterialTally();
        if (Uncertain != null)
        {
            failure = Uncertain;
            return false;
        }

        if (!recipe.IsKnown)
        {
            failure = "what that piece costs was never established, so nothing is spent on it";
            return false;
        }

        if (!TryWorker(out IInventoryPort? worker, out failure))
        {
            return false;
        }

        // Checked in full before anything is removed, so an affordable piece is
        // paid for in one go and an unaffordable one costs nothing. The placement
        // gate has already asked the same question of the same inventory; asking
        // it again here is what makes a partial spend impossible rather than
        // unlikely.
        var owed = new MaterialTally();
        foreach (PieceCost cost in recipe.Costs)
        {
            owed.Add(cost.Item, cost.Amount);
        }

        foreach (PieceCost line in owed.Lines)
        {
            if (Counted(worker!, line.Item) < line.Amount)
            {
                failure = "he is carrying only " + Counted(worker!, line.Item) + " " + line.Item + " of the " +
                    line.Amount + " it costs";
                return false;
            }
        }

        foreach (PieceCost line in owed.Lines)
        {
            int removed = Removed(worker!, line.Item, line.Amount);
            spent.Add(line.Item, removed);
            if (removed != line.Amount)
            {
                failure = "only " + removed + " of the " + line.Amount + " " + line.Item +
                    " it costs could be taken out of " + worker!.Describe +
                    ", so the record of what that piece cost is incomplete";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }

    /// <inheritdoc />
    public MaterialTally PutBack(out string failure)
    {
        var back = new MaterialTally();
        if (Uncertain != null)
        {
            failure = Uncertain;
            return back;
        }

        if (!TryWorker(out IInventoryPort? worker, out failure))
        {
            return back;
        }

        if (!TryChest(out IInventoryPort? chest, out failure))
        {
            return back;
        }

        if (!Ready(worker!) || !Ready(chest!))
        {
            failure = "one of the two inventories is not available now, so nothing was moved and what he " +
                "carries is still his";
            return back;
        }

        foreach (string kind in _kinds())
        {
            int held = Counted(worker!, kind);
            if (held < 1)
            {
                continue;
            }

            int moved = Move(worker!, chest!, kind, held);
            if (moved < 0)
            {
                failure = Uncertain ?? "a movement could not be accounted for";
                return back;
            }

            back.Add(kind, moved);
            if (moved < held)
            {
                failure = "only " + moved + " of the " + held + " " + kind + " he carries would fit in " +
                    chest!.Describe + "; the rest is still in his own inventory";
            }
        }

        return back;
    }

    /// <summary>One measured movement between two engine inventories.
    ///
    /// The ordering is <c>TransferExecutor</c>'s and the reason is the same: add
    /// to the destination first, so a crash part way leaves a visible duplicate
    /// rather than an invisible loss. The return is what BOTH sides agree moved;
    /// -1 means they did not agree, which latches
    /// <see cref="Uncertain"/>.</summary>
    private int Move(IInventoryPort from, IInventoryPort to, string kind, int count)
    {
        if (count < 1)
        {
            return 0;
        }

        var item = new MaterialItem(kind, 1, 0);
        int fromBefore = Counted(from, kind);
        int toBefore = Counted(to, kind);
        int room = Room(to, item, count);
        int want = Math.Min(count, Math.Min(fromBefore, room));
        if (want < 1)
        {
            return 0;
        }

        try
        {
            if (to is IInventoryMoveTarget mover && mover.CanMoveFrom(from))
            {
                // Vanilla's own move, which keeps the item instances and
                // therefore everything the items carry.
                mover.MoveFrom(from, item, want);
            }
            else
            {
                to.Add(item, want);
                int gained = Counted(to, kind) - toBefore;
                if (gained > 0)
                {
                    from.Remove(item, gained);
                }
            }
        }
        catch (Exception exception)
        {
            Uncertain = "moving " + want + " " + kind + " between " + Describe(from) + " and " + Describe(to) +
                " failed part way (" + exception.GetType().Name + ": " + exception.Message +
                "), so nothing more is moved until a person has looked at both";
            _log("Build order: " + Uncertain);
            return -1;
        }

        int gainedAfter = Counted(to, kind) - toBefore;
        int lostAfter = fromBefore - Counted(from, kind);
        if (gainedAfter != lostAfter || gainedAfter < 0 || gainedAfter > want)
        {
            Uncertain = Describe(from) + " lost " + lostAfter + " " + kind + " and " + Describe(to) + " gained " +
                gainedAfter + "; the two do not agree, so nothing more is moved and nothing is put back on a guess";
            _log("Build order: " + Uncertain);
            return -1;
        }

        return gainedAfter;
    }

    private bool TryWorker(out IInventoryPort? port, out string refusal)
    {
        port = null;
        if (!_custody.TryResolveWorker(_worker, out IInventoryPort? found, out CollectionAttentionReason reason) ||
            found == null)
        {
            refusal = "Thorstein's own inventory could not be opened (" +
                CollectionSentences.Describe(reason) + ")";
            return false;
        }

        port = found;
        refusal = string.Empty;
        return true;
    }

    private bool TryChest(out IInventoryPort? port, out string refusal)
    {
        port = null;
        SupplyChest chest = _supply();
        if (!chest.IsNamed)
        {
            refusal = "no supply chest is marked for this settlement";
            return false;
        }

        DeliveryTarget target;
        try
        {
            target = DeliveryTarget.ToContainer(chest.ContainerKey, _custody.WorldLoadEpoch, chest.At);
        }
        catch (ArgumentException exception)
        {
            refusal = "the marked supply chest does not belong to this world load (" + exception.Message + ")";
            return false;
        }

        if (!_custody.TryResolveContainer(target, out IInventoryPort? found, out CollectionAttentionReason reason) ||
            found == null)
        {
            refusal = "the marked supply chest could not be opened (" + CollectionSentences.Describe(reason) + ")";
            return false;
        }

        port = found;
        refusal = string.Empty;
        return true;
    }

    /// <summary>Whether an inventory may be written now. Asked before every
    /// movement for the same reason <c>TransferExecutor</c> asks it: a chest that
    /// went away, a ward that closed, or a worker whose last inventory write did
    /// not persist is not a place material may be moved through, and the honest
    /// answer is a refusal rather than a half-move.</summary>
    private static bool Ready(IInventoryPort port)
    {
        try
        {
            return port.IsAvailable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int Counted(IInventoryPort port, string kind)
    {
        try
        {
            return Math.Max(0, port.Count(new MaterialItem(kind, 1, 0)));
        }
        catch (Exception)
        {
            // An inventory that cannot be counted holds nothing as far as this
            // runtime may claim, which refuses work rather than inventing it.
            return 0;
        }
    }

    private static int Room(IInventoryPort port, MaterialItem item, int count)
    {
        try
        {
            return Math.Max(0, port.CanAccept(item, count));
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int Removed(IInventoryPort port, string kind, int count)
    {
        int before = Counted(port, kind);
        try
        {
            port.Remove(new MaterialItem(kind, 1, 0), count);
        }
        catch (Exception)
        {
            // Measured either way: what the call returned is never trusted.
        }

        return Math.Max(0, before - Counted(port, kind));
    }

    private static string Describe(IInventoryPort port)
    {
        try
        {
            return string.IsNullOrEmpty(port.Describe) ? "an inventory" : port.Describe;
        }
        catch (Exception)
        {
            return "an inventory";
        }
    }
}
