using System;
using System.Collections.Generic;

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
/// <b>Named steps, not counts.</b> Every movement carries the name of the step
/// that caused it, because a retry after a reload has to be able to re-state
/// what it already did and be told so. Re-stating the same name with the same
/// payload is <see cref="CargoOutcome.AlreadyRecorded"/> and changes nothing;
/// re-using a name for something else is refused outright, because a name that
/// means two things is how forty stone comes out of a chest and is recorded
/// nowhere.
///
/// <b>Nothing here is written to disk.</b> No row tag, no schema, no path, no
/// file. This is in-session accounting for one job in one world load.</summary>
internal sealed class CargoLedger
{
    private readonly Dictionary<string, Movement> _steps = new Dictionary<string, Movement>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _acquired = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<Slot, int> _places = new Dictionary<Slot, int>();

    /// <summary>How many units of everything this job has ever taken out of the
    /// world.</summary>
    public int Acquired
    {
        get
        {
            int total = 0;
            foreach (int units in _acquired.Values)
            {
                total += units;
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
            foreach (int units in _places.Values)
            {
                total += units;
            }

            return total;
        }
    }

    /// <summary>Nothing was invented and nothing evaporated. Asked after every
    /// step in the tests, not only at the end.</summary>
    public bool IsConserved => Acquired == TotalEverywhere;

    /// <summary>How many units of one item are in one place.</summary>
    public int At(string itemPrefab, CargoPlace place) =>
        _places.TryGetValue(new Slot(itemPrefab ?? string.Empty, place), out int units) ? units : 0;

    /// <summary>How many units of one item this job ever took.</summary>
    public int AcquiredOf(string itemPrefab) =>
        _acquired.TryGetValue(itemPrefab ?? string.Empty, out int units) ? units : 0;

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

        var movement = new Movement(itemPrefab, units, CargoPlace.Unspecified, CargoPlace.Carried);
        if (_steps.TryGetValue(step, out Movement existing))
        {
            return existing.Equals(movement) ? CargoOutcome.AlreadyRecorded : CargoOutcome.NameReused;
        }

        _steps.Add(step, movement);
        _acquired.TryGetValue(itemPrefab, out int already);
        _acquired[itemPrefab] = already + units;
        Add(itemPrefab, CargoPlace.Carried, units);
        return CargoOutcome.Recorded;
    }

    /// <summary>Records material moving from one place to another. Never
    /// changes the total.</summary>
    public CargoOutcome Move(string step, string itemPrefab, int units, CargoPlace from, CargoPlace to)
    {
        if (!Wellformed(step, itemPrefab, units) ||
            from == CargoPlace.Unspecified || to == CargoPlace.Unspecified || from == to)
        {
            return CargoOutcome.Malformed;
        }

        var movement = new Movement(itemPrefab, units, from, to);
        if (_steps.TryGetValue(step, out Movement existing))
        {
            return existing.Equals(movement) ? CargoOutcome.AlreadyRecorded : CargoOutcome.NameReused;
        }

        if (At(itemPrefab, from) < units)
        {
            // Refused rather than clamped. A move of more than is there is a
            // disagreement about reality, and guessing which side is right is
            // how material is invented.
            return CargoOutcome.NotThatMuchThere;
        }

        _steps.Add(step, movement);
        Add(itemPrefab, from, -units);
        Add(itemPrefab, to, units);
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

        if (_steps.TryGetValue(step, out Movement seen))
        {
            // Answered by what the step WAS, not by what it would record now -
            // because the first call emptied the cart, so a payload comparison
            // would find the second call's "nothing" against the first call's
            // real contents and read a retry as a reused name.
            return seen.IsCartSpill ? CargoOutcome.AlreadyRecorded : CargoOutcome.NameReused;
        }

        var inCart = new List<KeyValuePair<string, int>>();
        foreach (KeyValuePair<Slot, int> entry in _places)
        {
            if (entry.Key.Place == CargoPlace.InCart && entry.Value > 0)
            {
                inCart.Add(new KeyValuePair<string, int>(entry.Key.Item, entry.Value));
            }
        }

        if (inCart.Count == 0)
        {
            // An empty cart is still a destroyed cart, and the step is still
            // recorded so a retry is answered rather than re-run.
            _steps.Add(step, new Movement(string.Empty, 0, CargoPlace.InCart, CargoPlace.SpilledFromCart));
            return CargoOutcome.Recorded;
        }

        inCart.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        _steps.Add(step, new Movement(Join(inCart), Sum(inCart), CargoPlace.InCart, CargoPlace.SpilledFromCart));
        for (int index = 0; index < inCart.Count; index++)
        {
            Add(inCart[index].Key, CargoPlace.InCart, -inCart[index].Value);
            Add(inCart[index].Key, CargoPlace.SpilledFromCart, inCart[index].Value);
        }

        return CargoOutcome.Recorded;
    }

    /// <summary>What he is still holding, per item, in a deterministic order.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, int>> Holding(CargoPlace place)
    {
        var held = new List<KeyValuePair<string, int>>();
        foreach (KeyValuePair<Slot, int> entry in _places)
        {
            if (entry.Key.Place == place && entry.Value > 0)
            {
                held.Add(new KeyValuePair<string, int>(entry.Key.Item, entry.Value));
            }
        }

        held.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        return held;
    }

    private void Add(string item, CargoPlace place, int delta)
    {
        var slot = new Slot(item, place);
        _places.TryGetValue(slot, out int units);
        int next = units + delta;
        if (next <= 0)
        {
            _places.Remove(slot);
            return;
        }

        _places[slot] = next;
    }

    private static bool Wellformed(string step, string itemPrefab, int units) =>
        !string.IsNullOrEmpty(step) && !string.IsNullOrEmpty(itemPrefab) && units > 0;

    private static string Join(IReadOnlyList<KeyValuePair<string, int>> entries)
    {
        var parts = new string[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            parts[index] = entries[index].Key + "=" + entries[index].Value;
        }

        return string.Join(";", parts);
    }

    private static int Sum(IReadOnlyList<KeyValuePair<string, int>> entries)
    {
        int total = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            total += entries[index].Value;
        }

        return total;
    }

    private readonly struct Slot : IEquatable<Slot>
    {
        internal Slot(string item, CargoPlace place)
        {
            Item = item;
            Place = place;
        }

        internal string Item { get; }

        internal CargoPlace Place { get; }

        public bool Equals(Slot other) =>
            Place == other.Place && string.Equals(Item, other.Item, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Slot other && Equals(other);

        public override int GetHashCode() =>
            (StringComparer.Ordinal.GetHashCode(Item ?? string.Empty) * 397) ^ (int)Place;
    }

    /// <summary>What one named step did. Equality covers every field, because a
    /// name that is answered as "already done" on the strength of a partial
    /// match is how a retry records nothing and the totals still add up.
    /// </summary>
    private readonly struct Movement : IEquatable<Movement>
    {
        internal Movement(string item, int units, CargoPlace from, CargoPlace to)
        {
            Item = item;
            Units = units;
            From = from;
            To = to;
        }

        private string Item { get; }

        private int Units { get; }

        private CargoPlace From { get; }

        private CargoPlace To { get; }

        /// <summary>Whether this step was a cart spilling its contents. The one
        /// place a step's <i>kind</i> matters as well as its payload, because a
        /// second destruction of the same cart has nothing left to move and its
        /// payload would therefore not match its own first call.</summary>
        internal bool IsCartSpill => From == CargoPlace.InCart && To == CargoPlace.SpilledFromCart;

        public bool Equals(Movement other) =>
            Units == other.Units && From == other.From && To == other.To &&
            string.Equals(Item, other.Item, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Movement other && Equals(other);

        public override int GetHashCode() =>
            (((StringComparer.Ordinal.GetHashCode(Item ?? string.Empty) * 397) ^ Units) * 397 ^ (int)From) * 397 ^ (int)To;
    }
}
