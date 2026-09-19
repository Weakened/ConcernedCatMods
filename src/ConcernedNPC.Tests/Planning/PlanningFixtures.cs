using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>A container, as a test writes one. Permission is settable after the
/// fact because that is the interesting case: the chest that was fine when the
/// plan chose it and is not fine when the NPC opens it.</summary>
internal sealed class PlanningStockContainer : INpcContainer
{
    internal PlanningStockContainer(string key, NpcWorldEpoch epoch, float x, float z)
    {
        Key = key;
        Epoch = epoch;
        Position = new NpcPoint(x, 0f, z);
        Allowed = NpcContainerUse.Both;
        Refusal = NpcContainerRefusal.None;
    }

    public string Key { get; }

    public NpcWorldEpoch Epoch { get; }

    public NpcPoint Position { get; set; }

    public string Describe => Key;

    internal NpcContainerUse Allowed { get; set; }

    internal NpcContainerRefusal Refusal { get; set; }

    public NpcContainerAccess Access => new NpcContainerAccess(Allowed, Refusal);
}

/// <summary>A horizontal circle, which is every work area that ships today. The
/// library is not allowed to assume it, so the tests use one and the library
/// never asks for a radius.</summary>
internal sealed class FakeArea : INpcWorkArea
{
    internal FakeArea(float radius = 100f, int revision = 7)
    {
        BoundingRadiusMetres = radius;
        Revision = revision;
    }

    public NpcPoint BoundingCentre => default;

    public float BoundingRadiusMetres { get; }

    public int Revision { get; }

    public string Describe => "the test area";

    public bool Contains(NpcPoint point) =>
        point.IsFinite && point.HorizontalDistanceTo(BoundingCentre) <= BoundingRadiusMetres;
}

/// <summary>Ground that answers whatever a test says, and standable otherwise.
/// </summary>
internal sealed class FakeProbe : INpcAreaProbe
{
    private readonly Dictionary<string, AreaSample> _answers = new Dictionary<string, AreaSample>();

    internal int Probes { get; private set; }

    internal AreaSampleVerdict Otherwise { get; set; } = AreaSampleVerdict.Standable;

    internal FakeProbe Say(NpcPoint at, AreaSampleVerdict verdict, AreaRejection rejection = AreaRejection.None)
    {
        _answers[Name(at)] = new AreaSample(verdict, at, rejection);
        return this;
    }

    /// <summary>Something in the way, with an edge: a rock, a post, a corner of
    /// a building. Round rather than a band across the world, because a band is
    /// a thing no lateral step escapes and this planner says plainly that it
    /// does not find the way round a building - only past what is in front of
    /// it.</summary>
    internal NpcPoint? Obstacle { get; set; }

    internal float ObstacleRadius { get; set; } = 1.5f;

    public AreaSample Probe(NpcPoint point)
    {
        Probes++;
        if (_answers.TryGetValue(Name(point), out AreaSample answer))
        {
            return answer;
        }

        if (Obstacle.HasValue && point.HorizontalDistanceTo(Obstacle.Value) <= ObstacleRadius)
        {
            return new AreaSample(AreaSampleVerdict.Rejected, point, AreaRejection.Occupied);
        }

        return new AreaSample(Otherwise, point, AreaRejection.None);
    }

    private static string Name(NpcPoint point) =>
        point.X.ToString("0.###") + "/" + point.Y.ToString("0.###") + "/" + point.Z.ToString("0.###");
}

/// <summary>The role's completion condition, as a test writes one. Everything is
/// actionable unless a test says otherwise, and a test may change its mind
/// between ticks - which is the whole point of revalidating before every
/// stop.</summary>
internal sealed class PlanningStopObserver : IStopObserver
{
    private readonly Dictionary<string, StopStatus> _answers = new Dictionary<string, StopStatus>();

    internal int Looks { get; private set; }

    internal bool Throws { get; set; }

    internal PlanningStopObserver Say(string key, StopStatus status)
    {
        _answers[key] = status;
        return this;
    }

    public StopStatus Observe(in RouteStop stop)
    {
        Looks++;
        if (Throws)
        {
            throw new System.InvalidOperationException("a role read its own completion condition badly");
        }

        return _answers.TryGetValue(stop.Key, out StopStatus status) ? status : StopStatus.Actionable;
    }
}

/// <summary>A reservation book that behaves exactly as the seam documents:
/// one job per subject, the job half decides conflicts, a stale epoch resolves
/// nothing, releasing what you do not hold is an answer.
///
/// It is a test double rather than the shipped book on purpose - the book itself
/// belongs to the custody leaf - and it counts its calls, because "refunds
/// exactly once" is a statement about how many times the book was asked, not
/// about what it answered.</summary>
internal sealed class FakeBook<TSubject> : IReservationBook<TSubject>
    where TSubject : INpcEpochScoped
{
    private readonly Dictionary<TSubject, ReservationId> _held = new Dictionary<TSubject, ReservationId>();

    internal FakeBook(NpcWorldEpoch epoch)
    {
        Epoch = epoch;
    }

    public NpcWorldEpoch Epoch { get; }

    public int Count => _held.Count;

    /// <summary>How many times anybody asked for everything back. The number
    /// "exactly once" is about.</summary>
    internal int ReleaseAllCalls { get; private set; }

    public ReservationOutcome Reserve(TSubject subject, ReservationId reservation)
    {
        if (reservation.IsEmpty || Epoch.IsUnknown || !Epoch.Matches(subject.Epoch))
        {
            return reservation.IsEmpty ? ReservationOutcome.Unspecified : ReservationOutcome.StaleEpoch;
        }

        if (_held.TryGetValue(subject, out ReservationId holder))
        {
            return holder.SameJobAs(reservation)
                ? ReservationOutcome.AlreadySatisfied
                : ReservationOutcome.HeldByAnother;
        }

        _held[subject] = reservation;
        return ReservationOutcome.Reserved;
    }

    public ReservationOutcome Release(TSubject subject, ReservationId reservation)
    {
        if (!_held.TryGetValue(subject, out ReservationId holder) || !holder.SameJobAs(reservation))
        {
            return ReservationOutcome.NotHeld;
        }

        _held.Remove(subject);
        return ReservationOutcome.Released;
    }

    public int ReleaseAllFor(string jobId)
    {
        ReleaseAllCalls++;
        var going = new List<TSubject>();
        foreach (KeyValuePair<TSubject, ReservationId> row in _held)
        {
            if (string.Equals(row.Value.JobId, jobId, System.StringComparison.Ordinal))
            {
                going.Add(row.Key);
            }
        }

        foreach (TSubject subject in going)
        {
            _held.Remove(subject);
        }

        return going.Count;
    }

    public bool IsHeldBy(TSubject subject, ReservationId reservation) =>
        _held.TryGetValue(subject, out ReservationId holder) && holder.SameJobAs(reservation);

    public bool TryGetHolder(TSubject subject, out ReservationId reservation) =>
        _held.TryGetValue(subject, out reservation);
}

/// <summary>The scaffolding every pipeline test needs, so a test says what is
/// different about it and nothing else.</summary>
internal static class Jobs
{
    internal static NpcWorldEpoch World { get; } = NpcWorldEpoch.Mint();

    internal static JobStepActions Actions => new JobStepActions("take", "do");

    internal static JobManifest Needs(params (string Item, int Units)[] lines)
    {
        var manifest = new List<JobManifestLine>();
        foreach ((string item, int units) in lines)
        {
            manifest.Add(new JobManifestLine(item, units));
        }

        return new JobManifest(manifest);
    }

    internal static JobTarget Target(
        string key, float x, float z, int priority = 0, params (string Item, int Units)[] needs) =>
        new JobTarget(key, World, new NpcPoint(x, 0f, z), string.Empty, priority, Needs(needs));

    internal static SourceStock Stock(PlanningStockContainer container, params (string Item, int Units)[] lines)
    {
        var stock = new List<StockLine>();
        foreach ((string item, int units) in lines)
        {
            stock.Add(new StockLine(item, units));
        }

        return new SourceStock(container, stock);
    }

    /// <summary>A request, for a test that has nothing to say about who is
    /// asking.
    ///
    /// <c>carrying</c> defaults to empty <b>here and nowhere else</b>:
    /// <see cref="JobPlanRequest"/> itself has no default, because a role that
    /// stayed silent about it would silently claim an empty pair of hands and
    /// fetch a second load of what is on its back. A fixture saying "nothing is
    /// different about this test" is the one place that claim is safe, and it is
    /// made once, here, in the open.</summary>
    internal static JobPlanRequest Request(
        INpcWorkArea? area,
        NpcPoint from,
        JobManifest wanted = default,
        string jobId = "job",
        JobManifest carrying = default) =>
        new JobPlanRequest(new NpcIdentity("product", "worker"), jobId, area, World, wanted, from, carrying);

    internal static NpcPoint At(float x, float z) => new NpcPoint(x, 0f, z);
}
