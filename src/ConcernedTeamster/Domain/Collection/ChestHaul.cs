using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What a chest-to-chest haul asks the adapter to do next.</summary>
internal enum HaulOrder
{
    /// <summary>Nothing: the haul is over.</summary>
    None = 0,

    /// <summary>Walk towards <see cref="HaulInstruction.Target"/>.</summary>
    Walk = 1,

    /// <summary>Take <see cref="HaulInstruction.Units"/> of
    /// <see cref="HaulInstruction.Item"/> out of the source, through the pinned
    /// take port; report through <see cref="ChestHaul.TakeEnded"/>.</summary>
    Take = 2,

    /// <summary>Put <see cref="HaulInstruction.Units"/> of
    /// <see cref="HaulInstruction.Item"/> into the destination, through the
    /// pinned deposit port; report through <see cref="ChestHaul.UnloadEnded"/>.
    /// </summary>
    Unload = 3,

    /// <summary>Count what he carries of the cargo; report through
    /// <see cref="ChestHaul.Reconciled"/>.</summary>
    Reconcile = 4,

    /// <summary>Stand still.</summary>
    Hold = 5,
}

internal readonly struct HaulInstruction
{
    public HaulInstruction(HaulOrder order, CollectionPoint target, string item, int units)
    {
        Order = order;
        Target = target;
        Item = item ?? string.Empty;
        Units = units;
    }

    public HaulOrder Order { get; }

    public CollectionPoint Target { get; }

    public string Item { get; }

    public int Units { get; }

    public static HaulInstruction Nothing => new HaulInstruction(HaulOrder.None, default, string.Empty, 0);

    public static HaulInstruction Hold => new HaulInstruction(HaulOrder.Hold, default, string.Empty, 0);
}

internal enum ChestHaulPhase
{
    Unspecified = 0,

    /// <summary>Walking to the source chest.</summary>
    ToSource = 1,

    /// <summary>At the source, taking this tour's loads one item at a time.
    /// </summary>
    Provisioning = 2,

    /// <summary>Walking to the destination chest with the tour's loads.</summary>
    ToDestination = 3,

    /// <summary>At the destination, putting the tour's loads in one item at a
    /// time.</summary>
    Unloading = 4,

    /// <summary>Counting what he carries against what the haul measured.</summary>
    Reconciling = 5,

    /// <summary>Over: every planned tour ran and everything he took went in.
    /// </summary>
    Finished = 6,

    /// <summary>Over, and not finished. <see cref="ChestHaul.Reason"/> says
    /// why; what he still holds is in him and is never assumed to be anywhere
    /// else.</summary>
    Stopped = 7,
}

/// <summary>How one take or unload leg ended, from its port's measurement.
/// </summary>
internal enum HaulLegEnd
{
    Unspecified = 0,

    /// <summary>The port measured units moving (all, or some).</summary>
    Moved = 1,

    /// <summary>Nothing moved: refused before anything happened.</summary>
    Refused = 2,

    /// <summary>Whether it moved could not be established. Nothing more is
    /// attempted; a person decides.</summary>
    Uncertain = 3,
}

/// <summary>A manual chest-to-chest haul (#381 item 4): for each planned tour,
/// <b>provision</b> at the source, <b>travel</b> to the destination,
/// <b>unload</b>, <b>reconcile</b>, and back for the next.
///
/// <b>What it owns.</b> The order of the steps and the arithmetic of custody:
/// what was measured into him at the source, what was measured into the
/// destination, and what must therefore still be on him. It never assumes a
/// move happened; every figure it holds was reported by a port's measurement,
/// and the reconcile compares that against a fresh count of what he carries.
///
/// <b>What stops it.</b> An uncertain leg (nothing more is attempted); an
/// unload that left anything in him (a full or refusing destination); a
/// reconcile that does not agree, or could not count; a tour that took nothing;
/// either chest out of reach after a bounded walk. In every case what he holds
/// stays in him and the reason says so. There is no retry, no compensation and
/// no other chest.
///
/// <b>No portals, no teleports.</b> Every movement it asks for is a walk, given
/// up after <see cref="CollectionTourLimits.StallSeconds"/> without progress.
/// </summary>
internal sealed class ChestHaul
{
    private readonly HaulManifest _manifest;
    private readonly CollectionPoint _source;
    private readonly CollectionPoint _destination;
    private readonly CollectionTourLimits _limits;
    private readonly Dictionary<string, int> _taken = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _delivered = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<HaulLoad> _tourTaken = new List<HaulLoad>();

    private int _line;
    private bool _orderIssued;
    private string _tourRefusal = string.Empty;
    private bool _tourLeftSomething;
    private float _bestDistance = float.PositiveInfinity;
    private float _bestAt = float.NaN;

    public ChestHaul(HaulManifest manifest, CollectionPoint source, CollectionPoint destination, CollectionTourLimits limits)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        if (!manifest.IsPlanned)
        {
            throw new ArgumentException("a haul runs only a planned manifest", nameof(manifest));
        }

        _source = source;
        _destination = destination;
        Phase = ChestHaulPhase.ToSource;
    }

    public ChestHaulPhase Phase { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public bool IsOver => Phase == ChestHaulPhase.Finished || Phase == ChestHaulPhase.Stopped;

    public HaulManifest Manifest => _manifest;

    /// <summary>The tour in hand, from zero.</summary>
    public int Tour { get; private set; }

    /// <summary>Tours that ran to a reconcile that agreed.</summary>
    public int ToursCompleted { get; private set; }

    public int TakenOf(string item) => _taken.TryGetValue(item ?? string.Empty, out int units) ? units : 0;

    public int DeliveredOf(string item) => _delivered.TryGetValue(item ?? string.Empty, out int units) ? units : 0;

    /// <summary>What the haul measured into him and not out again, per cargo
    /// item, in loading order. Only items with something held are listed.</summary>
    public IReadOnlyList<HaulLoad> Holding
    {
        get
        {
            var held = new List<HaulLoad>();
            for (int index = 0; index < _manifest.Cargo.Count; index++)
            {
                string item = _manifest.Cargo[index].Item;
                int units = TakenOf(item) - DeliveredOf(item);
                if (units != 0)
                {
                    held.Add(new HaulLoad(item, units));
                }
            }

            return held;
        }
    }

    public int TotalDelivered
    {
        get
        {
            int total = 0;
            foreach (int units in _delivered.Values)
            {
                total += units;
            }

            return total;
        }
    }

    public HaulInstruction Tick(CollectionPoint standingAt, float now)
    {
        switch (Phase)
        {
            case ChestHaulPhase.ToSource:
                return Walk(standingAt, now, _source, ArrivedAtSource, "the source chest");
            case ChestHaulPhase.Provisioning:
                return NextTake();
            case ChestHaulPhase.ToDestination:
                return Walk(standingAt, now, _destination, ArrivedAtDestination, "the destination chest");
            case ChestHaulPhase.Unloading:
                return NextUnload();
            case ChestHaulPhase.Reconciling:
                return Issue(HaulOrder.Reconcile, string.Empty, 0);
            default:
                return HaulInstruction.Nothing;
        }
    }

    /// <summary>What the pinned take port measured for the take just ordered.
    /// <paramref name="units"/> is counted only for <see cref="HaulLegEnd.Moved"/>.
    /// </summary>
    public void TakeEnded(HaulLegEnd end, int units, string detail)
    {
        if (Phase != ChestHaulPhase.Provisioning || !_orderIssued)
        {
            return;
        }

        HaulLoad line = CurrentTour().Loads[_line];
        _orderIssued = false;
        _line++;
        if (end == HaulLegEnd.Moved && units > line.Units)
        {
            // More arrived than was asked for: the port's measurement and the
            // order disagree, which is not something to credit or balance.
            Stop("the source measured " + units.ToString(CultureInfo.InvariantCulture) + " " + line.Item +
                " reaching him where " + line.Units.ToString(CultureInfo.InvariantCulture) +
                " were asked for; nothing more was attempted - a person decides");
            return;
        }

        switch (end)
        {
            case HaulLegEnd.Moved when units > 0:
                Add(_taken, line.Item, units);
                _tourTaken.Add(new HaulLoad(line.Item, units));
                break;
            case HaulLegEnd.Uncertain:
                Stop("whether the " + line.Item + " was taken could not be established" + Suffix(detail) +
                    " Nothing more was attempted and nothing is put right automatically - a person decides");
                return;
            default:
                // Refused, or "moved" nothing: the chest keeps it. The next line
                // is still asked - a chest with no Wood may still have Stone.
                _tourRefusal = line.Item + ": " + (string.IsNullOrEmpty(detail) ? "nothing was taken" : detail);
                break;
        }

        if (_line < CurrentTour().Loads.Count)
        {
            return;
        }

        _line = 0;
        if (_tourTaken.Count == 0)
        {
            Stop("nothing could be taken at the source on tour " + Ordinal(Tour) +
                Suffix(_tourRefusal.Length == 0 ? string.Empty : _tourRefusal));
            return;
        }

        Phase = ChestHaulPhase.ToDestination;
        ResetWalk();
    }

    /// <summary>What the pinned deposit port measured for the unload just
    /// ordered.</summary>
    public void UnloadEnded(HaulLegEnd end, int units, string detail)
    {
        if (Phase != ChestHaulPhase.Unloading || !_orderIssued)
        {
            return;
        }

        HaulLoad line = _tourTaken[_line];
        _orderIssued = false;
        _line++;
        if (end == HaulLegEnd.Uncertain)
        {
            Stop("whether the " + line.Item + " went in could not be established" + Suffix(detail) +
                " Nothing more was attempted and nothing is put right automatically - a person decides");
            return;
        }

        int moved = end == HaulLegEnd.Moved && units > 0 ? units : 0;
        if (moved > line.Units)
        {
            // More arrived than this tour took of it: the port's measurement
            // and this record disagree, which is not something to balance.
            Stop("the destination measured " + moved.ToString(CultureInfo.InvariantCulture) + " " + line.Item +
                " arriving where he carried " + line.Units.ToString(CultureInfo.InvariantCulture) +
                " for this haul; a person decides");
            return;
        }

        if (moved > 0)
        {
            Add(_delivered, line.Item, moved);
        }

        if (moved < line.Units)
        {
            _tourLeftSomething = true;
            _tourRefusal = line.Item + ": " + (string.IsNullOrEmpty(detail) ? "the destination took no more" : detail);
        }

        if (_line >= _tourTaken.Count)
        {
            _line = 0;
            Phase = ChestHaulPhase.Reconciling;
            _orderIssued = false;
        }
    }

    /// <summary>A fresh count of what he carries of each cargo item, or
    /// <c>null</c> when it could not be read. It must equal
    /// <see cref="Holding"/>: he began the haul holding none of the cargo, so
    /// anything else means a move nobody measured.</summary>
    public void Reconciled(IReadOnlyList<KeyValuePair<string, int>>? carried)
    {
        if (Phase != ChestHaulPhase.Reconciling || !_orderIssued)
        {
            return;
        }

        _orderIssued = false;
        if (carried == null)
        {
            Stop("what he carries could not be counted after tour " + Ordinal(Tour) +
                ", so the haul stopped; a person decides");
            return;
        }

        string mismatch = Mismatch(carried);
        if (mismatch.Length != 0)
        {
            Stop("after tour " + Ordinal(Tour) + " he carries " + mismatch +
                ", which is not what the measured moves add up to; nothing is put right automatically - a person decides");
            return;
        }

        if (_tourLeftSomething)
        {
            Stop("the destination did not take everything on tour " + Ordinal(Tour) + Suffix(_tourRefusal) +
                " What it did not take stays in him");
            return;
        }

        ToursCompleted++;
        _tourTaken.Clear();
        _tourRefusal = string.Empty;
        if (Tour + 1 >= _manifest.Tours.Count)
        {
            Phase = ChestHaulPhase.Finished;
            Reason = "every planned tour ran";
            return;
        }

        Tour++;
        Phase = ChestHaulPhase.ToSource;
        ResetWalk();
    }

    public void Cancel(string why)
    {
        Stop(string.IsNullOrEmpty(why) ? "it was stopped" : why);
    }

    public string Describe()
    {
        var said = new StringBuilder();
        said.Append(TotalDelivered.ToString(CultureInfo.InvariantCulture)).Append(" of ")
            .Append(_manifest.UnitsPlanned.ToString(CultureInfo.InvariantCulture))
            .Append(" planned unit(s) delivered over ")
            .Append(ToursCompleted.ToString(CultureInfo.InvariantCulture)).Append(" of ")
            .Append(_manifest.Tours.Count.ToString(CultureInfo.InvariantCulture)).Append(" tour(s)");
        IReadOnlyList<HaulLoad> held = Holding;
        if (held.Count != 0)
        {
            said.Append("; measured into him by this haul and not out again: ").Append(Join(held));
        }

        if (Reason.Length != 0)
        {
            said.Append("; ").Append(Reason);
        }

        return said.ToString();
    }

    private HaulTourPlan CurrentTour() => _manifest.Tours[Tour];

    private HaulInstruction NextTake()
    {
        IReadOnlyList<HaulLoad> loads = CurrentTour().Loads;
        if (_line >= loads.Count)
        {
            return HaulInstruction.Hold;
        }

        return Issue(HaulOrder.Take, loads[_line].Item, loads[_line].Units);
    }

    private HaulInstruction NextUnload()
    {
        if (_line >= _tourTaken.Count)
        {
            return HaulInstruction.Hold;
        }

        return Issue(HaulOrder.Unload, _tourTaken[_line].Item, _tourTaken[_line].Units);
    }

    private HaulInstruction Issue(HaulOrder order, string item, int units)
    {
        if (_orderIssued)
        {
            return HaulInstruction.Hold;
        }

        _orderIssued = true;
        return new HaulInstruction(order, default, item, units);
    }

    private void ArrivedAtSource()
    {
        Phase = ChestHaulPhase.Provisioning;
        _line = 0;
        _orderIssued = false;
        _tourTaken.Clear();
        _tourRefusal = string.Empty;
        _tourLeftSomething = false;
    }

    private void ArrivedAtDestination()
    {
        Phase = ChestHaulPhase.Unloading;
        _line = 0;
        _orderIssued = false;
    }

    private HaulInstruction Walk(CollectionPoint standingAt, float now, CollectionPoint to, Action arrived, string where)
    {
        float distance = standingAt.FlatDistanceTo(to);
        if (float.IsNaN(distance))
        {
            Stop("where he stands, or " + where + ", could not be read");
            return HaulInstruction.Hold;
        }

        if (distance <= _limits.ReachMetres)
        {
            arrived();
            return HaulInstruction.Hold;
        }

        if (float.IsNaN(_bestAt) || distance < _bestDistance - _limits.ProgressMetres)
        {
            _bestDistance = distance;
            _bestAt = now;
        }
        else if (now - _bestAt >= _limits.StallSeconds)
        {
            Stop("NEEDS ATTENTION: he could not get to " + where + " (no progress for " +
                _limits.StallSeconds.ToString("0", CultureInfo.InvariantCulture) +
                " s), so he stopped with what he holds still on him");
            return HaulInstruction.Hold;
        }

        return new HaulInstruction(HaulOrder.Walk, to, string.Empty, 0);
    }

    private string Mismatch(IReadOnlyList<KeyValuePair<string, int>> carried)
    {
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < carried.Count; index++)
        {
            Add(counted, carried[index].Key, carried[index].Value);
        }

        var wrong = new List<HaulLoad>();
        for (int index = 0; index < _manifest.Cargo.Count; index++)
        {
            string item = _manifest.Cargo[index].Item;
            int expected = TakenOf(item) - DeliveredOf(item);
            int found = counted.TryGetValue(item, out int units) ? units : 0;
            if (found != expected)
            {
                wrong.Add(new HaulLoad(item, found));
            }
        }

        return wrong.Count == 0 ? string.Empty : Join(wrong);
    }

    private void Stop(string why)
    {
        if (!IsOver)
        {
            Phase = ChestHaulPhase.Stopped;
            Reason = why;
            _orderIssued = false;
        }
    }

    private void ResetWalk()
    {
        _bestDistance = float.PositiveInfinity;
        _bestAt = float.NaN;
    }

    private static void Add(Dictionary<string, int> into, string item, int units)
    {
        into[item] = (into.TryGetValue(item, out int had) ? had : 0) + units;
    }

    private static string Join(IReadOnlyList<HaulLoad> loads)
    {
        var said = new StringBuilder();
        for (int index = 0; index < loads.Count; index++)
        {
            said.Append(index == 0 ? string.Empty : ", ")
                .Append(loads[index].Units.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(loads[index].Item);
        }

        return said.ToString();
    }

    private static string Ordinal(int tour) => (tour + 1).ToString(CultureInfo.InvariantCulture);

    private static string Suffix(string detail) =>
        string.IsNullOrEmpty(detail) ? "." : " (" + detail + ").";
}
