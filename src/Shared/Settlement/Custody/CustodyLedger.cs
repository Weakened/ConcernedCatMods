using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Identity;

namespace TheConcernedCat.Settlement.Custody;

/// <summary>A quantity of one material. Deliberately a value: nothing can hold
/// a reference to "the wood" and mutate it behind the ledger's back.</summary>
internal readonly struct MaterialStack : IEquatable<MaterialStack>
{
    public MaterialStack(string item, int count)
    {
        if (string.IsNullOrEmpty(item))
        {
            throw new ArgumentException("A material stack needs an item name.", nameof(item));
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), "A material stack holds at least one item. An empty stack is an " +
                "absent stack, and representing it twice invites double-counting.");
        }

        Item = item;
        Count = count;
    }

    public string Item { get; }
    public int Count { get; }

    public bool Equals(MaterialStack other)
    {
        return string.Equals(Item, other.Item, StringComparison.Ordinal) && Count == other.Count;
    }

    public override bool Equals(object? obj) => obj is MaterialStack other && Equals(other);

    public override int GetHashCode()
    {
        return ((Item == null ? 0 : StringComparer.Ordinal.GetHashCode(Item)) * 397) ^ Count;
    }

    public override string ToString()
    {
        return Item + " x" + Count.ToString(CultureInfo.InvariantCulture);
    }
}

internal enum ReservationState
{
    /// <summary>Taken from the container, not yet spent. The only state a
    /// refund is owed from.</summary>
    Held = 0,

    /// <summary>Spent on a placed piece. Terminal.</summary>
    Committed = 1,

    /// <summary>Returned to the container. Terminal.</summary>
    Refunded = 2,

    /// <summary>A commit was started and its outcome is unknown. Terminal for
    /// automatic handling: neither committed nor refundable, because this build
    /// will not guess which of the two happened.</summary>
    Uncertain = 3,
}

/// <summary>Material taken from one designated container for one request.</summary>
internal sealed class Reservation
{
    private readonly List<MaterialStack> _stacks = new List<MaterialStack>();

    public Reservation(RequestId request, OrderId order, string container, IEnumerable<MaterialStack> stacks)
    {
        if (request.IsEmpty)
        {
            throw new ArgumentException("A reservation needs a request id.", nameof(request));
        }

        if (order.IsEmpty)
        {
            throw new ArgumentException("A reservation needs an owning order.", nameof(order));
        }

        if (string.IsNullOrEmpty(container))
        {
            throw new ArgumentException(
                "A reservation needs the designated container it came from.", nameof(container));
        }

        Request = request;
        Order = order;
        Container = container;

        foreach (MaterialStack stack in stacks ?? throw new ArgumentNullException(nameof(stacks)))
        {
            _stacks.Add(stack);
        }

        if (_stacks.Count == 0)
        {
            throw new ArgumentException(
                "A reservation with nothing in it is not a reservation.", nameof(stacks));
        }
    }

    public RequestId Request { get; }
    public OrderId Order { get; }

    /// <summary>Which container this came out of. Refunds go back to exactly
    /// this one — never to whatever is nearest at the time.</summary>
    public string Container { get; }

    public IReadOnlyList<MaterialStack> Stacks => _stacks;

    public ReservationState State { get; private set; } = ReservationState.Held;

    public bool IsSettled => State != ReservationState.Held;

    internal bool TrySettle(ReservationState settled)
    {
        if (settled == ReservationState.Held)
        {
            return false;
        }

        if (State == settled)
        {
            // Idempotent: the same settlement twice is one settlement.
            return false;
        }

        if (State != ReservationState.Held)
        {
            // Already settled a different way. Changing it would be exactly
            // the double-spend this whole type exists to prevent.
            return false;
        }

        State = settled;
        return true;
    }

    public override string ToString()
    {
        return Request.Value + " " + State + " (" + string.Join(", ", Describe()) + ")";
    }

    private string[] Describe()
    {
        var parts = new string[_stacks.Count];
        for (int index = 0; index < _stacks.Count; index++)
        {
            parts[index] = _stacks[index].ToString();
        }

        return parts;
    }
}

internal enum CustodyOutcome
{
    /// <summary>The ledger changed.</summary>
    Applied = 0,

    /// <summary>Already in this state. Nothing changed, nothing is wrong —
    /// this is what a retried request looks like.</summary>
    AlreadySatisfied = 1,

    /// <summary>Refused: unknown request, or a settled reservation being
    /// settled a second, different way.</summary>
    Rejected = 2,
}

/// <summary>Who is holding what, on behalf of which request.
///
/// This is the ledger. The worker's inventory is <b>not</b> — it is ZDO-backed
/// and can be lost to a death, a despawn or a zone unload, and a ledger that
/// loses material on events which are not cancellations is not a ledger. What
/// the worker visibly carries is evidence that this ledger is doing something;
/// if the two ever disagree, this one is right.
///
/// Every operation is keyed by <see cref="RequestId"/> and every one of them is
/// idempotent, so the honest answer to "did that already happen?" is always
/// available after a crash. The conservation invariant the whole settlement
/// rests on is one line:
///
/// <code>container + held reservations + committed cost = constant</code>
///
/// and it is checked adversarially at every transition, not asserted.</summary>
internal sealed class CustodyLedger
{
    private readonly Dictionary<string, Reservation> _reservations =
        new Dictionary<string, Reservation>(StringComparer.Ordinal);
    private readonly List<string> _order = new List<string>();

    public IReadOnlyList<Reservation> Reservations
    {
        get
        {
            var ordered = new List<Reservation>(_order.Count);
            foreach (string key in _order)
            {
                ordered.Add(_reservations[key]);
            }

            return ordered;
        }
    }

    public bool TryGet(RequestId request, out Reservation reservation)
    {
        return _reservations.TryGetValue(request.Value, out reservation!);
    }

    /// <summary>Records material taken out of a container.
    ///
    /// A duplicate request id reports <see cref="CustodyOutcome.AlreadySatisfied"/>
    /// and takes nothing, which is precisely what makes a retried reserve safe:
    /// the adapter that calls this may not know whether its previous attempt
    /// reached disk, and it does not have to.</summary>
    public CustodyOutcome Reserve(Reservation reservation)
    {
        if (reservation == null)
        {
            throw new ArgumentNullException(nameof(reservation));
        }

        if (_reservations.ContainsKey(reservation.Request.Value))
        {
            return CustodyOutcome.AlreadySatisfied;
        }

        _reservations.Add(reservation.Request.Value, reservation);
        _order.Add(reservation.Request.Value);
        return CustodyOutcome.Applied;
    }

    /// <summary>Spends a reservation on a placed piece.</summary>
    public CustodyOutcome Commit(RequestId request)
    {
        return Settle(request, ReservationState.Committed);
    }

    /// <summary>Returns unspent material to the container it came from.</summary>
    public CustodyOutcome Refund(RequestId request)
    {
        return Settle(request, ReservationState.Refunded);
    }

    /// <summary>Marks a reservation whose commit outcome is unknown.
    ///
    /// Neither committed nor refundable: this build does not know whether the
    /// material became a wall or is still owed back, and inventing an answer
    /// would either conjure material or lose it. The order is flagged for
    /// repair and a person decides.</summary>
    public CustodyOutcome MarkUncertain(RequestId request)
    {
        return Settle(request, ReservationState.Uncertain);
    }

    private CustodyOutcome Settle(RequestId request, ReservationState settled)
    {
        if (!_reservations.TryGetValue(request.Value, out Reservation? reservation))
        {
            return CustodyOutcome.Rejected;
        }

        if (reservation!.State == settled)
        {
            return CustodyOutcome.AlreadySatisfied;
        }

        return reservation.TrySettle(settled)
            ? CustodyOutcome.Applied
            : CustodyOutcome.Rejected;
    }

    /// <summary>Everything still owed back to a container if this order were
    /// cancelled right now.</summary>
    public IReadOnlyList<Reservation> HeldFor(OrderId order)
    {
        var held = new List<Reservation>();
        foreach (string key in _order)
        {
            Reservation reservation = _reservations[key];
            if (reservation.Order.Equals(order) && reservation.State == ReservationState.Held)
            {
                held.Add(reservation);
            }
        }

        return held;
    }

    /// <summary>True when any reservation is in an unknown state, so the
    /// settlement must stop and say so rather than continue.</summary>
    public bool HasUncertainCustody
    {
        get
        {
            foreach (string key in _order)
            {
                if (_reservations[key].State == ReservationState.Uncertain)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Total counted per item across reservations in a given state.
    /// The conservation tests compare these sums before and after forced
    /// failures.</summary>
    public IReadOnlyDictionary<string, int> Totals(ReservationState state)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in _order)
        {
            Reservation reservation = _reservations[key];
            if (reservation.State != state)
            {
                continue;
            }

            foreach (MaterialStack stack in reservation.Stacks)
            {
                totals.TryGetValue(stack.Item, out int running);
                totals[stack.Item] = running + stack.Count;
            }
        }

        return totals;
    }
}
