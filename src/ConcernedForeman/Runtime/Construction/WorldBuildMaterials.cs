using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedForeman.Domain.Construction;
using TheConcernedCat.ConcernedForeman.Runtime.Work;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using TheConcernedCat.Diagnostics;

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

/// <summary>One whole reservation per piece cost, fetched in the loop's phase
/// batches. Custody owns durable intent, receipts and retry decisions. This
/// adapter owns only measured inventory effects, through custody's vetted ports.
/// A refund always resolves the original source and epoch, never the currently
/// designated chest. Pre-existing, unrecorded items are not building credit.</summary>
internal sealed class WorldBuildMaterials : IBuildMaterials
{
    private sealed class Holding
    {
        internal Holding(Reservation reservation, CostedPiece piece, DeliveryTarget source)
        {
            Reservation = reservation;
            Piece = piece;
            Source = source;
        }

        internal Reservation Reservation { get; }
        internal CostedPiece Piece { get; }
        internal DeliveryTarget Source { get; }
        internal bool Drawn { get; set; }
        internal bool Settled { get; set; }
    }

    private readonly ICustodyRuntime _custody;
    private readonly WorkerKey _worker;
    private readonly Func<SupplyChest> _supply;
    private readonly Action<string> _log;
    private readonly List<Holding> _holdings = new List<Holding>();
    private Guid _epoch;
    private OrderId _order;
    private string _tag = string.Empty;

    internal WorldBuildMaterials(ICustodyRuntime custody, WorkerKey worker,
        Func<SupplyChest> supply, Action<string> log)
    {
        _custody = custody ?? throw new ArgumentNullException(nameof(custody));
        _worker = worker;
        _supply = supply ?? throw new ArgumentNullException(nameof(supply));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    private string? _uncertain;
    internal string? Uncertain
    {
        get { World(); return _uncertain; }
        private set => _uncertain = value;
    }

    public void BeginOrder(string tag)
    {
        World();
        if (string.Equals(_tag, tag, StringComparison.Ordinal)) return;
        foreach (Holding holding in _holdings)
        {
            if (holding.Drawn && !holding.Settled)
                throw new InvalidOperationException("the previous build still holds a reservation; run cf_settle reconcile");
        }

        _tag = tag;
        _order = new OrderId("build-" + Guid.NewGuid().ToString("N"));
        _holdings.Clear();
    }

    private void World()
    {
        if (_epoch == _custody.WorldLoadEpoch && !_order.IsEmpty) return;
        _epoch = _custody.WorldLoadEpoch;
        _order = new OrderId("build-" + Guid.NewGuid().ToString("N"));
        _holdings.Clear();
        _tag = string.Empty;
        Uncertain = null;
    }

    public bool TrySupply(out SitePoint at, out string refusal)
    {
        SupplyChest chest = _supply();
        at = chest.At;
        refusal = chest.IsNamed ? string.Empty : "no supply chest is marked; look at it and run: cf_settle supply";
        return chest.IsNamed;
    }

    public MaterialTally Carried
    {
        get
        {
            World();
            var held = new MaterialTally();
            if (!TryWorker(out IInventoryPort? worker, out _)) return held;
            foreach (Holding holding in _holdings)
            {
                if (!holding.Drawn || holding.Settled) continue;
                foreach (MaterialStack stack in holding.Reservation.Stacks) held.Add(stack.Item, stack.Count);
            }

            var measured = new MaterialTally();
            foreach (PieceCost line in held.Lines)
                measured.Add(line.Item, Math.Min(line.Amount, Counted(worker!, line.Item)));
            return measured;
        }
    }

    public BuildDraw Draw(IReadOnlyList<CostedPiece> pieces)
    {
        World();
        if (Uncertain != null) return BuildDraw.Refused(Uncertain);
        if (!_custody.IsWritable) return BuildDraw.Refused("the settlement record cannot be written; run cf_settle reconcile");
        if (!TryWorker(out IInventoryPort? worker, out string failure)) return BuildDraw.Refused(failure);
        SupplyChest supply = _supply();
        if (!supply.IsNamed) return BuildDraw.Refused("no supply chest is marked; run cf_settle supply");

        var drawn = new MaterialTally();
        var shortBy = new MaterialTally();
        foreach (CostedPiece piece in pieces)
        {
            if (!piece.Recipe.IsKnown) return BuildDraw.Refused("what that piece costs was never established");
            MaterialTally cost = new MaterialTally().Add(piece.Recipe);
            if (cost.IsEmpty) return BuildDraw.Refused("that piece has no reservable material cost");
            Holding? holding = Find(piece);
            if (holding != null && !SamePiece(holding.Piece, piece))
                return BuildDraw.Refused("that piece request already names a different placement or cost");
            if (holding != null && holding.Drawn && !holding.Settled) continue;

            // A failed intent retains its request AND its source. Validate the
            // inventory this attempt will actually withdraw from, even if the
            // player has since designated a different, reachable chest.
            DeliveryTarget source = holding != null && !holding.Settled ? holding.Source :
                DeliveryTarget.ToContainer(supply.ContainerKey, _epoch, supply.At);
            if (!TryChest(source, out IInventoryPort? chest, out failure)) return BuildDraw.Refused(failure);
            if (!Ready(worker!) || !Ready(chest!)) return BuildDraw.Refused("one of the inventories is not available now");
            if (!Fits(chest!, worker!, cost))
            {
                foreach (PieceCost line in cost.Lines) shortBy.Add(line.Item, line.Amount);
                continue;
            }

            if (holding == null || holding.Settled)
            {
                var stacks = new List<MaterialStack>();
                foreach (PieceCost line in cost.Lines) stacks.Add(new MaterialStack(line.Item, line.Amount));
                var reservation = new Reservation(new RequestId("piece-" + piece.Key + "-" + Guid.NewGuid().ToString("N")),
                    _order, supply.ContainerKey, stacks, _epoch.ToString("N"));
                holding = new Holding(reservation, piece, source);
                _holdings.Add(holding);
            }

            Holding current = holding;
            CustodyOutcome outcome = _custody.ReserveBuild(current.Reservation, () =>
            {
                // Persistence precedes the effect, so recheck the retained
                // source's availability/reach and whole cost after the write.
                if (!Fits(chest!, worker!, cost)) return false;
                foreach (MaterialStack stack in current.Reservation.Stacks)
                {
                    if (!Ready(chest!) || !Ready(worker!)) return false;
                    int moved = Move(chest!, worker!, stack.Item, stack.Count);
                    if (moved != stack.Count) return false;
                }
                return true;
            }, out failure);
            if (outcome == CustodyOutcome.Rejected) return BuildDraw.Refused(Uncertain ?? failure);
            current.Drawn = true;
            if (outcome == CustodyOutcome.Applied)
                foreach (MaterialStack stack in current.Reservation.Stacks) drawn.Add(stack.Item, stack.Count);
        }

        return BuildDraw.Moved(drawn, shortBy);
    }

    public bool IsReserved(CostedPiece piece)
    {
        World();
        Holding? holding = Find(piece);
        return holding != null && holding.Drawn && !holding.Settled && SamePiece(holding.Piece, piece);
    }

    public bool Commit(CostedPiece piece, Func<bool> place, out MaterialTally spent, out string failure)
    {
        World();
        var paid = new MaterialTally();
        spent = paid;
        if (Uncertain != null) { failure = Uncertain; return false; }
        Holding? holding = Find(piece);
        if (holding == null || !holding.Drawn || !SamePiece(holding.Piece, piece))
        {
            failure = "that piece has no matching reserved cost";
            return false;
        }

        if (!TryWorker(out IInventoryPort? worker, out failure)) return false;
        string detail = string.Empty;
        CustodyOutcome outcome = _custody.CommitBuild(holding.Reservation, () =>
        {
            if (!Ready(worker!)) return false;
            foreach (MaterialStack stack in holding.Reservation.Stacks)
                if (Counted(worker!, stack.Item) < stack.Count) return false;
            // The caller verifies the standing piece inside this callback. Both
            // halves are inside CommitStarted/CommitFinished, without yielding.
            if (!place()) return false;
            foreach (MaterialStack stack in holding.Reservation.Stacks)
            {
                int removed = Removed(worker!, stack.Item, stack.Count, out detail);
                paid.Add(stack.Item, removed);
                if (removed != stack.Count || detail.Length != 0) return false;
            }
            return true;
        }, out failure);
        if (outcome == CustodyOutcome.Rejected)
        {
            if (detail.Length != 0) failure += " " + detail;
            return false;
        }
        holding.Settled = true;
        return true;
    }

    public MaterialTally PutBack(out string failure)
    {
        World();
        var back = new MaterialTally();
        failure = Uncertain ?? string.Empty;
        if (Uncertain != null || !TryWorker(out IInventoryPort? worker, out failure)) return back;
        foreach (Holding holding in _holdings)
        {
            if (!holding.Drawn || holding.Settled) continue;
            if (!TryChest(holding.Source, out IInventoryPort? chest, out failure)) return back;
            var cost = new MaterialTally();
            foreach (MaterialStack stack in holding.Reservation.Stacks) cost.Add(stack.Item, stack.Count);
            if (!Fits(worker!, chest!, cost))
            {
                failure = "the recorded source cannot take the whole reservation now; it is still in his own inventory";
                return back;
            }
            CustodyOutcome outcome = _custody.RefundBuild(holding.Reservation, () =>
            {
                foreach (MaterialStack stack in holding.Reservation.Stacks)
                    if (Move(worker!, chest!, stack.Item, stack.Count) != stack.Count) return false;
                return true;
            }, out failure);
            if (outcome == CustodyOutcome.Rejected) return back;
            holding.Settled = true;
            if (outcome == CustodyOutcome.Applied)
                foreach (MaterialStack stack in holding.Reservation.Stacks) back.Add(stack.Item, stack.Count);
        }
        return back;
    }

    private Holding? Find(CostedPiece piece)
    {
        for (int index = _holdings.Count - 1; index >= 0; index--)
            if (_holdings[index].Piece.Key == piece.Key) return _holdings[index];
        return null;
    }

    private static bool SamePiece(CostedPiece left, CostedPiece right)
    {
        if (left.Placement.At.X != right.Placement.At.X || left.Placement.At.Y != right.Placement.At.Y ||
            left.Placement.At.Z != right.Placement.At.Z || left.Placement.Yaw != right.Placement.Yaw ||
            left.Placement.Piece.Prefab != right.Placement.Piece.Prefab) return false;
        MaterialTally a = new MaterialTally().Add(left.Recipe);
        MaterialTally b = new MaterialTally().Add(right.Recipe);
        return a.Missing(b).IsEmpty && b.Missing(a).IsEmpty;
    }

    private static bool Fits(IInventoryPort from, IInventoryPort to, MaterialTally cost)
    {
        if (!Ready(from) || !Ready(to)) return false;
        foreach (PieceCost line in cost.Lines)
            if (Counted(from, line.Item) < line.Amount || Room(to, new MaterialItem(line.Item, 1, 0), line.Amount) < line.Amount)
                return false;
        return true;
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
                " failed part way (" + SafeFailure.Brief(exception) +
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

    private bool TryChest(DeliveryTarget target, out IInventoryPort? port, out string refusal)
    {
        port = null;
        if (target.WorldLoadEpoch != _custody.WorldLoadEpoch)
        {
            refusal = "the recorded supply chest could not be opened in this world load";
            return false;
        }
        if (!_custody.TryResolveContainer(target, out port, out CollectionAttentionReason reason) || port == null)
        {
            refusal = "the recorded supply chest could not be opened (" + CollectionSentences.Describe(reason) + ")";
            return false;
        }

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
        // Zero is evidence, not a substitute for a failed read. In particular,
        // a failed post-removal count must never look like successful payment.
        int count = port.Count(new MaterialItem(kind, 1, 0));
        if (count < 0) throw new InvalidOperationException("an inventory returned a negative material count");
        return count;
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

    /// <summary>Takes units out of one inventory, measured, and says whether the
    /// removal could be made durable.
    ///
    /// <b>A throw here is not nothing, and treating it as nothing mints
    /// material.</b> <c>EngineInventoryPort.Remove</c> mutates the inventory and
    /// <i>then</i> calls <c>VerifyPersisted</c>, and
    /// <c>WorkerInventoryPort.VerifyPersisted</c> throws precisely when the body's
    /// own ZDO write failed. So the items are gone from the in-memory inventory,
    /// the measured delta looks like a clean payment, and the next world load
    /// hands them straight back - with the piece already standing. This used to
    /// swallow that exception and report success, which is the one movement in
    /// this file that did not go through <see cref="Move"/> and therefore did not
    /// fail closed like every other one.</summary>
    /// <param name="failure">Empty when the removal is durable; otherwise the
    /// latched reason, and nothing more is moved.</param>
    private int Removed(IInventoryPort port, string kind, int count, out string failure)
    {
        int before = Counted(port, kind);
        failure = string.Empty;
        try
        {
            port.Remove(new MaterialItem(kind, 1, 0), count);
        }
        catch (Exception exception)
        {
            Uncertain = "taking " + count + " " + kind + " out of " + Describe(port) +
                " could not be made durable (" + SafeFailure.Brief(exception) +
                "). The piece may be standing and the material may come back on the next load, so " +
                "nothing more is moved until a person has looked at what he is carrying.";
            _log("Build order: " + Uncertain);
            failure = Uncertain;
        }

        // Measured either way: what the call returned is never trusted.
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
