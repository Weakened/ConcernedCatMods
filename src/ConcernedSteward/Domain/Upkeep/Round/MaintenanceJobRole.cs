using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Jobs;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedSteward.Domain.Npc;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>The marked settlement, as Concerned NPC sees a work area.
///
/// <b>The role supplies the shape and the library never invents one.</b> There
/// is a circle implementation inside the library and it is internal, which is
/// right: a work area is a statement about what a player marked, and this
/// product already has that statement in a <c>Designation</c> it wrote down. So
/// the shape comes from here, and <see cref="Contains"/> is the designation's
/// own containment test rather than a second one that could disagree with the
/// commands the player typed.
///
/// <b>The revision is derived, not counted.</b> A job in flight compares it
/// against the value it started with, so it has to give the same answer for the
/// same circle in a later session - a counter would make every reload look like
/// a moved area.</summary>
internal sealed class SettlementWorkArea : INpcWorkArea
{
    private readonly Designation _settlement;

    internal SettlementWorkArea(Designation settlement)
    {
        _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
    }

    public bool Contains(NpcPoint point) =>
        _settlement.Contains(new SitePoint(point.X, point.Y, point.Z));

    public NpcPoint BoundingCentre => Point(_settlement.Centre);

    public float BoundingRadiusMetres => _settlement.Radius;

    public int Revision
    {
        get
        {
            // FNV-1a over the centre and the radius, so two sessions reading the
            // same designation compute the same number and a job that survived a
            // reload is not told its area moved.
            unchecked
            {
                int hash = (int)2166136261;
                hash = Mix(hash, _settlement.Centre.X);
                hash = Mix(hash, _settlement.Centre.Y);
                hash = Mix(hash, _settlement.Centre.Z);
                hash = Mix(hash, _settlement.Radius);
                return hash;
            }
        }
    }

    public string Describe => "the marked settlement";

    internal static NpcPoint Point(SitePoint point) => new NpcPoint(point.X, point.Y, point.Z);

    internal static SitePoint Site(NpcPoint point) => new SitePoint(point.X, point.Y, point.Z);

    private static int Mix(int hash, float value)
    {
        unchecked
        {
            foreach (byte piece in BitConverter.GetBytes(value))
            {
                hash = (hash ^ piece) * 16777619;
            }

            return hash;
        }
    }
}

/// <summary>One approved chest, as Concerned NPC sees a container.
///
/// <b>Permission is re-read on every call and never cached.</b> That is the
/// port's own contract and it is not ceremony: the player can walk into the
/// chest, a ward can go up, and ownership can migrate between the tick that
/// chose this chest and the tick that opens it.</summary>
internal sealed class SupplyContainer : INpcContainer
{
    private readonly Func<SupplySighting> _sighting;
    private readonly NpcWorldEpoch _world;

    internal SupplyContainer(NpcWorldEpoch world, Func<SupplySighting> sighting)
    {
        _world = world;
        _sighting = sighting ?? throw new ArgumentNullException(nameof(sighting));
    }

    public string Key => Now().Key;

    public NpcWorldEpoch Epoch => _world;

    public NpcPoint Position => SettlementWorkArea.Point(Now().Position);

    public string Describe => Now().Describe;

    public NpcContainerAccess Access
    {
        get
        {
            SupplySighting now = Now();
            if (now.Key.Length == 0)
            {
                return new NpcContainerAccess(NpcContainerUse.Off, NpcContainerRefusal.Gone);
            }

            NpcContainerUse allowed = NpcContainerUse.Off;
            if (now.CanTake)
            {
                allowed |= NpcContainerUse.Take;
            }

            if (now.CanDeposit)
            {
                allowed |= NpcContainerUse.Deposit;
            }

            return new NpcContainerAccess(
                allowed,
                allowed == NpcContainerUse.Off
                    ? NpcContainerRefusal.NotEnabled
                    : NpcContainerRefusal.None);
        }
    }

    private SupplySighting Now()
    {
        try
        {
            return _sighting();
        }
        catch (Exception)
        {
            // A chest that cannot be read is a chest that refuses, which is what
            // a defaulted sighting already says.
            return default;
        }
    }
}

/// <summary>What the Steward supplies to one maintenance round, and nothing
/// else.
///
/// <b>This is the whole of "the role decides what, the library decides
/// how".</b> Which lights want fuel, how badly, what each takes, which chests
/// may be drawn from and what they hold, and whether a light is still worth
/// walking to — all of that is this product's, and all of it is decided in
/// <see cref="MaintenanceRound"/> and <see cref="LightRevalidation"/>, which are
/// testable without a game. The order those answers are used in — total, refuse,
/// batch, provision, route, walk, revalidate, reconcile — is
/// <c>NpcJobDriver</c>'s, and nothing here can change it.
///
/// <b>What is deliberately null.</b> <see cref="Probe"/> is null because the
/// lights are world objects the adapter has just read: their places are proved,
/// and probing the ground under a hearth would buy nothing.
/// <see cref="Availability"/> is null because the Steward keeps his own material
/// accounting in <c>FuelCustody</c> and the record, which the library cannot see
/// — and the cost of that is stated where the seam states it: two jobs planned a
/// tick apart would plan the same wood. There is one Steward and one job, so
/// there is no second job to collide with; the day there is, this is the line
/// that has to change.</summary>
internal sealed class MaintenanceJobRole : INpcJobRole
{
    private readonly Func<RoundPlan> _round;
    private readonly Func<FuelTargetKey, FuelTargetObservation?> _look;
    private readonly Func<Designation?> _settlement;
    private readonly Func<string> _epoch;
    private readonly MaintenanceThresholds _thresholds;
    private readonly NpcWorldEpoch _world;

    /// <param name="world">The world load, as the registry minted it. A role
    /// must never mint its own.</param>
    /// <param name="round">The round as this product worked it out. Re-read on
    /// every call, because the driver takes a fresh snapshot each round.</param>
    /// <param name="look">Re-reads one light, for the revalidation before each
    /// stop. Null when it is not there any more.</param>
    /// <param name="settlement">The marked area as it stands now.</param>
    /// <param name="epoch">This product's own identity epoch, which is what a
    /// fire key is scoped to.</param>
    /// <param name="thresholds">The same thresholds the round was planned with;
    /// revalidating against different ones would service a light the round did
    /// not plan for.</param>
    internal MaintenanceJobRole(
        NpcWorldEpoch world,
        Func<RoundPlan> round,
        Func<FuelTargetKey, FuelTargetObservation?> look,
        Func<Designation?> settlement,
        Func<string> epoch,
        MaintenanceThresholds thresholds)
    {
        _world = world;
        _round = round ?? throw new ArgumentNullException(nameof(round));
        _look = look ?? throw new ArgumentNullException(nameof(look));
        _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        _epoch = epoch ?? throw new ArgumentNullException(nameof(epoch));
        _thresholds = thresholds;
    }

    /// <inheritdoc />
    public INpcAreaProbe? Probe => null;

    /// <inheritdoc />
    public INpcSourceAvailability? Availability => null;

    /// <inheritdoc />
    public IReadOnlyList<JobTarget> Candidates(NpcWorldEpoch world)
    {
        var targets = new List<JobTarget>();
        if (!world.Matches(_world))
        {
            // A world load this role's keys do not belong to. Nothing resolves,
            // and offering a target anyway would be offering a name that points
            // at some other object.
            return targets;
        }

        RoundPlan round = _round();
        foreach (LightNeed stop in round.Stops)
        {
            targets.Add(new JobTarget(
                stop.Light.Key.Value,
                world,
                SettlementWorkArea.Point(stop.Light.Position),
                action: string.Empty,
                priority: stop.Priority,
                needs: new JobManifest(new[] { new JobManifestLine(stop.Item, stop.Units) })));
        }

        return targets;
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceStock> Sources(NpcWorldEpoch world)
    {
        var sources = new List<SourceStock>();
        if (!world.Matches(_world))
        {
            return sources;
        }

        RoundPlan round = _round();
        for (int index = 0; index < round.Sources.Count; index++)
        {
            SupplySighting sighting = round.Sources[index];
            if (sighting.Key.Length == 0)
            {
                continue;
            }

            var lines = new List<StockLine>();
            foreach (SupplyLine line in sighting.Lines)
            {
                lines.Add(new StockLine(line.Item, line.Units));
            }

            // Captured by value rather than by index: the round is re-read on
            // every call, and a container that reached back into a list by
            // position would answer about whichever chest happened to be there
            // next time.
            SupplySighting captured = sighting;
            sources.Add(new SourceStock(
                new SupplyContainer(world, () => captured), lines));
        }

        return sources;
    }

    /// <summary>Is this light still worth walking to, as of now.
    ///
    /// The one place this product's meaning enters the library, and it is asked
    /// again before every stop rather than once at the start — which is what
    /// makes a hearth the player filled while she was walking a skip rather than
    /// a wasted trip.</summary>
    public StopStatus Observe(in RouteStop stop)
    {
        RoundPlan round = _round();
        foreach (LightNeed planned in round.Stops)
        {
            if (!string.Equals(planned.Light.Key.Value, stop.Key, StringComparison.Ordinal))
            {
                continue;
            }

            FuelTargetObservation? seen;
            try
            {
                seen = _look(planned.Light.Key);
            }
            catch (Exception)
            {
                // Unknown, never "gone": a light nobody could read must not be
                // dropped as destroyed.
                return StopStatus.Unreadable;
            }

            return Map(LightRevalidation.Look(
                planned, seen, _settlement(), _epoch(), _thresholds));
        }

        // A stop this round did not plan. Never actionable: the library is
        // asking about something this product did not offer.
        return StopStatus.Unreadable;
    }

    /// <summary>The Steward's seven verdicts onto the library's seven statuses.
    ///
    /// One-to-one, deliberately, because the two vocabularies were written to be
    /// the same one. A mapping that collapsed any pair here would be the defect
    /// both enums exist to prevent: a round declaring itself finished because a
    /// zone had not streamed in.</summary>
    internal static StopStatus Map(LightVerdict verdict)
    {
        switch (verdict)
        {
            case LightVerdict.Actionable: return StopStatus.Actionable;
            case LightVerdict.AlreadyDone: return StopStatus.AlreadyDone;
            case LightVerdict.Gone: return StopStatus.Gone;
            case LightVerdict.Moved: return StopStatus.Moved;
            case LightVerdict.Unreachable: return StopStatus.Unreachable;
            case LightVerdict.Refused: return StopStatus.Refused;
            default: return StopStatus.Unreadable;
        }
    }

    /// <summary>The order one maintenance round is worked under.
    ///
    /// <b>Every number in it is this product's and none of them has a default
    /// that makes a claim.</b> The carry capacity is what she can take in one
    /// trip, in units of fuel, which only this product can convert; the two
    /// words are what a step is called in her language; and the ceiling is the
    /// round's own manifest, so a job that turns out to need more than the round
    /// was worked out for is refused rather than quietly spending more of a
    /// player's wood than the plan said.</summary>
    internal static NpcJobOrder OrderFor(
        in RoundPlan round,
        TheConcernedCat.ConcernedNPC.Roles.NpcIdentity identity,
        string jobId,
        INpcWorkArea area,
        NpcWorldEpoch world,
        int unitsPerTrip) =>
        new NpcJobOrder(
            identity,
            jobId,
            area,
            world,
            new NpcCarryCapacity(unitsPerTrip),
            new JobStepActions(collect: "fetch", service: "tend"),
            ceiling: Ceiling(round));

    /// <summary>Starts one maintenance round, from the one place the Steward is
    /// registered.
    ///
    /// <b>Why this exists rather than three call sites assembling an order.</b>
    /// A job now needs four things that must all come from the same registry: an
    /// identity that registry tracks, the world epoch that registry minted, the
    /// registry itself, and a job name no other driver is holding. Get any of
    /// them from somewhere else and the driver refuses before it plans — which
    /// is the right behaviour and an extremely quiet one, because a refusal
    /// looks exactly like a settlement with nothing to do.
    ///
    /// So there is one construction and both the runtime and the tests use it.
    /// That is not tidiness: the gap between how a product starts a job and how
    /// its tests start one is precisely where an adoption bug hides, and this
    /// leaf has already been bitten by it once.</summary>
    /// <param name="adoption">The Steward's registration. Supplies the identity,
    /// the world and the registry, all three from the same place.</param>
    /// <param name="jobId">What this job is called. One driver at a time may
    /// hold it: a second driver with a different name is refused as busy, and a
    /// second with the same name takes the mode it already holds.</param>
    internal static NpcJobDriver DriverFor(
        StewardNpcAdoption adoption,
        in RoundPlan round,
        string jobId,
        INpcWorkArea area,
        int unitsPerTrip,
        Func<RoundPlan> currentRound,
        Func<FuelTargetKey, FuelTargetObservation?> look,
        Func<Designation?> settlement,
        Func<string> epoch,
        MaintenanceThresholds thresholds)
    {
        if (adoption == null)
        {
            throw new ArgumentNullException(nameof(adoption));
        }

        NpcWorldEpoch world = adoption.World;
        return NpcJobDriver.For(
            OrderFor(round, adoption.Identity, jobId, area, world, unitsPerTrip),
            new MaintenanceJobRole(world, currentRound, look, settlement, epoch, thresholds),
            adoption.Registry);
    }

    private static JobManifest Ceiling(in RoundPlan round)
    {
        var lines = new List<JobManifestLine>();
        foreach (RoundManifestLine line in round.Manifest.Lines)
        {
            lines.Add(new JobManifestLine(line.Item, line.Units));
        }

        return new JobManifest(lines);
    }
}
