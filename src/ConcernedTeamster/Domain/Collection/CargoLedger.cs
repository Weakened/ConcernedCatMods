using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Where a unit of material this job gathered physically is. Every
/// value is a <b>place</b>, never a subtraction: material is moved from one
/// place to another and the total never changes, which is what makes the
/// conservation check something anybody can ask rather than something the code
/// promises.</summary>
internal enum CargoPlace
{
    /// <summary>Nobody said. Never a real place; reading it is a bug.</summary>
    Unspecified = 0,

    /// <summary>On Gunnar's back.</summary>
    Carried = 1,

    /// <summary>In the assigned cart.</summary>
    InCart = 2,

    /// <summary>Put into the destination container. Terminal.</summary>
    Delivered = 3,

    /// <summary>On the ground where the cart was destroyed, because the game
    /// spilled it exactly as it spills any destroyed cart's contents. Terminal,
    /// and it is <b>not</b> a loss to be made good: the material still exists,
    /// it is simply not his any more. Nothing replaces it.</summary>
    SpilledFromCart = 4,

    /// <summary>Gone, and known to be gone - he died, or a container refused and
    /// the material could not be put anywhere. Terminal, and still counted, so
    /// the totals stay honest.</summary>
    Lost = 5,

    /// <summary>On the ground where he died, put there by the game's own drop
    /// (`DECISIONS.md` D16) and measured leaving him. Terminal, and like a
    /// spilled cart it is not a loss to be made good: the material exists, it
    /// is simply not his any more. Nothing replaces it.</summary>
    DroppedOnDeath = 6,

    /// <summary>He was holding it when he died and it is not proven to be on the
    /// ground: the drop was refused, declined, threw, or measured an amount it
    /// cannot reconcile. D16's "held-and-unresolved". Terminal and counted, so the
    /// totals stay honest; nothing is re-acquired or re-credited for it.</summary>
    UnresolvedAtDeath = 7,
}

/// <summary>What one attempt to record a movement did.</summary>
internal enum CargoOutcome
{
    Unspecified = 0,

    /// <summary>Recorded.</summary>
    Recorded = 1,

    /// <summary>This exact step, under this exact name, with this exact payload,
    /// was already recorded. A retry after an interruption, answered rather than
    /// applied twice.</summary>
    AlreadyRecorded = 2,

    /// <summary>A step name already used for something else. Refused, and the
    /// first payload is kept: a name means one thing.</summary>
    NameReused = 3,

    /// <summary>More was asked to move than that place holds.</summary>
    NotThatMuchThere = 4,

    /// <summary>The request was malformed - no name, no item, a count that is
    /// not positive, a place nobody named.</summary>
    Malformed = 5,
}

/// <summary>Every unit Gunnar's collection touches, and where it is.
///
/// <b>Why it exists.</b> #381 requires that if the cart is destroyed the haul
/// stops safely, the accounting is preserved and <i>no replacement cargo
/// appears</i>. The only way to make that checkable rather than believed is to
/// never subtract: a unit is acquired once, and thereafter it is in exactly one
/// place. Then "was anything invented" is one comparison -
/// <see cref="Acquired"/> against <see cref="TotalEverywhere"/> - and a test can
/// ask it after every single step rather than at the end.
///
/// <b>The invariant is Concerned NPC's, not this file's</b> (#376). This type
/// used to keep its own copy of it: its own places, its own payload-checked
/// names, its own refusal to clamp. Every unit now lives in the library's
/// <see cref="NpcCustodyLedger"/>, which keeps the same rule for every role, and
/// this type is the translation between Gunnar's words and the library's. What
/// is left here is only what is Gunnar's: which <see cref="CargoPlace"/> is
/// which library place, the cart-spill verb, and the rule below about names.
///
/// <b>Named steps, not counts.</b> Every movement carries the name of the step
/// that caused it, because a retry after a reload has to be able to re-state
/// what it already did and be told so. Re-stating the same name with the same
/// payload is <see cref="CargoOutcome.AlreadyRecorded"/> and changes nothing;
/// re-using a name for something else is refused outright, because a name that
/// means two things is how forty stone comes out of a chest and is recorded
/// nowhere. The library keeps acquisitions and movements in separate name
/// spaces; Gunnar's names have always been one, so a name used for a take
/// cannot then be used for a move, or for a cart spill, and the reverse. The
/// payload under a name is the library's to compare.
///
/// <b>Stricter than before in one way, deliberately.</b> Material that has been
/// delivered, lost, spilled or dropped is never moved again as his: the library
/// refuses to move material out of a terminal place, and that refusal is
/// answered <see cref="CargoOutcome.Malformed"/>. Nothing in the runtime ever
/// asked for such a move.
///
/// <b>Nothing here is written to disk.</b> No row tag, no schema, no path, no
/// file. This is in-session accounting for one job in one world load, which is
/// why every place is named in the library's unknown load: the ledger never
/// outlives the session that wrote it, and the library refuses only a mixture.
/// </summary>
internal sealed class CargoLedger
{
    /// <summary>The one job this account keeps custody for. A constant,
    /// because the ledger lives exactly as long as the account does and nothing
    /// else writes to it.</summary>
    private const string Job = "gunnar-collection";

    private static readonly NpcCustodyLocation OnHisBack = Place(NpcCustodyPlace.Carried, "back");
    private static readonly NpcCustodyLocation InTheCart = Place(NpcCustodyPlace.Stored, "assigned-cart");
    private static readonly NpcCustodyLocation AtTheDestination = Place(NpcCustodyPlace.Delivered, "destination");
    private static readonly NpcCustodyLocation SpiltFromTheCart = Place(NpcCustodyPlace.Released, "cart-spill");
    private static readonly NpcCustodyLocation GoneForGood = Place(NpcCustodyPlace.Lost, "lost");
    private static readonly NpcCustodyLocation DroppedWhereHeDied = Place(NpcCustodyPlace.Released, "death-drop");
    private static readonly NpcCustodyLocation HeldWhenHeDied = Place(NpcCustodyPlace.Lost, "unresolved-at-death");

    private readonly NpcCustodyLedger _custody = new NpcCustodyLedger();
    private readonly Dictionary<string, Named> _names = new Dictionary<string, Named>(StringComparer.Ordinal);
    private readonly List<string> _items = new List<string>();
    private int _nextStep;

    public CargoLedger()
    {
        _custody.OpenJob(Job);
    }

    private enum Kind
    {
        Take = 1,
        Move = 2,
        CartSpill = 3,
    }

    /// <summary>The library ledger every unit is kept in. Read-only use: a test
    /// asks it whether the invariant holds in the library's own terms.</summary>
    internal NpcCustodyLedger Custody => _custody;

    /// <summary>How many units of everything this job has ever taken out of the
    /// world.</summary>
    public int Acquired
    {
        get
        {
            int total = 0;
            for (int index = 0; index < _items.Count; index++)
            {
                total += _custody.Acquired(Job, NpcMaterial.Of(_items[index]));
            }

            return total;
        }
    }

    /// <summary>How many units are accounted for across every place.</summary>
    public int TotalEverywhere
    {
        get
        {
            int total = 0;
            for (int index = 0; index < _items.Count; index++)
            {
                total += _custody.TotalEverywhere(Job, NpcMaterial.Of(_items[index]));
            }

            return total;
        }
    }

    /// <summary>Nothing was invented and nothing evaporated, for every item
    /// separately - the library's question, asked of each. Asked after every
    /// step in the tests, not only at the end.</summary>
    public bool IsConserved
    {
        get
        {
            for (int index = 0; index < _items.Count; index++)
            {
                if (!_custody.IsConserved(Job, NpcMaterial.Of(_items[index])))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>How many units of one item are in one place.</summary>
    public int At(string itemPrefab, CargoPlace place)
    {
        if (string.IsNullOrEmpty(itemPrefab) || !TryLocate(place, out NpcCustodyLocation location))
        {
            return 0;
        }

        return _custody.HoldingAt(Job, location, NpcMaterial.Of(itemPrefab));
    }

    /// <summary>How many units of one item this job ever took.</summary>
    public int AcquiredOf(string itemPrefab) =>
        string.IsNullOrEmpty(itemPrefab) ? 0 : _custody.Acquired(Job, NpcMaterial.Of(itemPrefab));

    /// <summary>Records that a take actually happened: material left the world
    /// and is now on his back. The only entry point that raises
    /// <see cref="Acquired"/>, so a step that did not take cannot increase it.
    /// </summary>
    public CargoOutcome Take(string step, string itemPrefab, int units)
    {
        if (!Wellformed(step, itemPrefab, units))
        {
            return CargoOutcome.Malformed;
        }

        if (_names.TryGetValue(step, out Named named))
        {
            return named.Kind == Kind.Take
                ? Answer(_custody.Acquire(named.Id, OnHisBack, NpcMaterial.Of(itemPrefab), units))
                : CargoOutcome.NameReused;
        }

        ReservationId id = Mint();
        NpcCustodyOutcome taken = _custody.Acquire(id, OnHisBack, NpcMaterial.Of(itemPrefab), units);
        if (taken != NpcCustodyOutcome.Applied)
        {
            // The job is opened in the constructor and the request is well
            // formed, so this is the library refusing for a reason of its own.
            // Nothing was recorded, so the name stays free.
            return CargoOutcome.Malformed;
        }

        _names.Add(step, new Named(Kind.Take, id));
        Remember(itemPrefab);
        return CargoOutcome.Recorded;
    }

    /// <summary>Records material moving from one place to another. Never
    /// changes the total.</summary>
    public CargoOutcome Move(string step, string itemPrefab, int units, CargoPlace from, CargoPlace to)
    {
        if (!Wellformed(step, itemPrefab, units) || from == to
            || !TryLocate(from, out NpcCustodyLocation source)
            || !TryLocate(to, out NpcCustodyLocation destination))
        {
            return CargoOutcome.Malformed;
        }

        NpcMaterial material = NpcMaterial.Of(itemPrefab);
        if (_names.TryGetValue(step, out Named named))
        {
            return named.Kind == Kind.Move
                ? Answer(_custody.RecordMeasuredMove(named.Id, source, destination, material, units))
                : CargoOutcome.NameReused;
        }

        if (_custody.HoldingAt(Job, source, material) < units)
        {
            // Refused rather than clamped. A move of more than is there is a
            // disagreement about reality, and guessing which side is right is
            // how material is invented. The library refuses it too; asking
            // first is only so the answer can say which refusal it was.
            return CargoOutcome.NotThatMuchThere;
        }

        ReservationId id = Mint();
        NpcCustodyOutcome moved = _custody.RecordMeasuredMove(id, source, destination, material, units);
        if (moved != NpcCustodyOutcome.Applied)
        {
            // A source the library will not move material out of again: it was
            // delivered, lost, spilled or dropped. Nothing was recorded.
            return CargoOutcome.Malformed;
        }

        _names.Add(step, new Named(Kind.Move, id));
        return CargoOutcome.Recorded;
    }

    /// <summary>The cart was destroyed. Everything it held is now on the ground
    /// where it stood, exactly as the game left it.
    ///
    /// <b>This is the whole of the answer.</b> Nothing is re-credited, nothing
    /// is re-acquired, nothing is re-planned to make the job whole, and the
    /// totals do not move. The job stops and says why; the player picks the
    /// material up, or does not.</summary>
    public CargoOutcome CartDestroyed(string step)
    {
        if (string.IsNullOrEmpty(step))
        {
            return CargoOutcome.Malformed;
        }

        if (_names.TryGetValue(step, out Named seen))
        {
            // Answered by what the step WAS, not by what it would record now -
            // because the first call emptied the cart, so a payload comparison
            // would find the second call's "nothing" against the first call's
            // real contents and read a retry as a reused name.
            return seen.Kind == Kind.CartSpill ? CargoOutcome.AlreadyRecorded : CargoOutcome.NameReused;
        }

        IReadOnlyList<KeyValuePair<string, int>> inCart = Holding(CargoPlace.InCart);

        // An empty cart is still a destroyed cart, and the step is still
        // recorded so a retry is answered rather than re-run. Recorded before
        // the units move, so that a refusal half way through still leaves the
        // name answering as the spill it was rather than free for a second one.
        _names.Add(step, new Named(Kind.CartSpill, default));
        CargoOutcome result = CargoOutcome.Recorded;
        for (int index = 0; index < inCart.Count; index++)
        {
            NpcCustodyOutcome spilt = _custody.RecordMeasuredMove(
                Mint(), InTheCart, SpiltFromTheCart, NpcMaterial.Of(inCart[index].Key), inCart[index].Value);
            if (spilt != NpcCustodyOutcome.Applied)
            {
                // Unreachable: each count is what the library holds in the cart
                // this instant. If it is ever reached, the units stay in the
                // cart where the record can still see them, and the answer is
                // not a success.
                result = CargoOutcome.Unspecified;
            }
        }

        return result;
    }

    /// <summary>What he is still holding, per item, in a deterministic order.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, int>> Holding(CargoPlace place)
    {
        var held = new List<KeyValuePair<string, int>>();
        if (!TryLocate(place, out NpcCustodyLocation location))
        {
            return held;
        }

        IReadOnlyList<NpcHolding> holdings = _custody.Holdings;
        for (int index = 0; index < holdings.Count; index++)
        {
            NpcHolding holding = holdings[index];
            if (holding.Count > 0
                && string.Equals(holding.JobId, Job, StringComparison.Ordinal)
                && holding.Location.Equals(location))
            {
                held.Add(new KeyValuePair<string, int>(holding.Material.ItemName, holding.Count));
            }
        }

        held.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        return held;
    }

    /// <summary>Gunnar's places, in the library's words. Each is a distinct
    /// location - the key is what tells a spilled cart from a death drop, both
    /// of which the library calls released - and every one of them is named in
    /// the unknown load, for the reason the type summary gives.</summary>
    private static bool TryLocate(CargoPlace place, out NpcCustodyLocation location)
    {
        switch (place)
        {
            case CargoPlace.Carried: location = OnHisBack; return true;
            case CargoPlace.InCart: location = InTheCart; return true;
            case CargoPlace.Delivered: location = AtTheDestination; return true;
            case CargoPlace.SpilledFromCart: location = SpiltFromTheCart; return true;
            case CargoPlace.Lost: location = GoneForGood; return true;
            case CargoPlace.DroppedOnDeath: location = DroppedWhereHeDied; return true;
            case CargoPlace.UnresolvedAtDeath: location = HeldWhenHeDied; return true;
            default: location = default; return false;
        }
    }

    private static NpcCustodyLocation Place(NpcCustodyPlace place, string key) =>
        new NpcCustodyLocation(place, key, NpcWorldEpoch.Unknown);

    /// <summary>The library's answer, in Gunnar's words, for a name he has
    /// already used for the same kind of thing.</summary>
    private static CargoOutcome Answer(NpcCustodyOutcome outcome)
    {
        switch (outcome)
        {
            case NpcCustodyOutcome.AlreadySatisfied: return CargoOutcome.AlreadyRecorded;
            case NpcCustodyOutcome.RejectedDifferentPayload: return CargoOutcome.NameReused;

            // Anything else under a name that was recorded is the library
            // refusing to treat it as done, and a refusal is never a success.
            default: return CargoOutcome.NameReused;
        }
    }

    private ReservationId Mint() => ReservationId.For(Job, _nextStep++);

    private void Remember(string itemPrefab)
    {
        for (int index = 0; index < _items.Count; index++)
        {
            if (string.Equals(_items[index], itemPrefab, StringComparison.Ordinal))
            {
                return;
            }
        }

        _items.Add(itemPrefab);
    }

    private static bool Wellformed(string step, string itemPrefab, int units) =>
        !string.IsNullOrEmpty(step) && !string.IsNullOrEmpty(itemPrefab) && units > 0;

    /// <summary>What one of Gunnar's names was used for, and the library name it
    /// was recorded under. A cart spill records one library movement per item,
    /// so it keeps no single id of its own.</summary>
    private readonly struct Named
    {
        internal Named(Kind kind, ReservationId id)
        {
            Kind = kind;
            Id = id;
        }

        internal Kind Kind { get; }

        internal ReservationId Id { get; }
    }
}
