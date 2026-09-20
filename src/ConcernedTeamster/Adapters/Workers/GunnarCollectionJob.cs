using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Jobs;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gunnar's collection, as the shared NPC runtime's job driver sees it
/// (#381, on #371's adoption surface).
///
/// <b>This product hands in providers and consumes steps. It does not
/// sequence.</b> Which target is next, how a job is split into trips, when a
/// round has to be planned again, whether the whole job is covered and when to
/// stop are all the driver's - and the last of those is the one that matters,
/// because "every step came off" and "the job is finished" are different
/// questions and the driver is the thing that asks both. Everything below is the
/// other half: what is worth taking, what the chests hold, what one trip holds,
/// and what became of a step once he got there.
///
/// <b>What is still this product's, deliberately.</b> Eligibility, because only
/// Teamster knows what Gunnar collects; carry capacity, because it is read from
/// the game's own carry-weight semantics; and the material accounting, because
/// the driver knows nothing about what work yields and conservation has to be
/// somebody's.</summary>
internal sealed class GunnarCollectionJob : INpcJobRole
{
    private readonly Func<CollectionSurvey> _survey;
    private readonly Func<IReadOnlyList<SourceStock>> _sources;
    private readonly Dictionary<string, CollectionCandidate> _byKey =
        new Dictionary<string, CollectionCandidate>(StringComparer.Ordinal);

    public GunnarCollectionJob(Func<CollectionSurvey> survey, Func<IReadOnlyList<SourceStock>> sources)
    {
        _survey = survey ?? throw new ArgumentNullException(nameof(survey));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    /// <summary>How the last look went, so a runtime can tell a player "the area
    /// is clear" apart from "part of it is not loaded". The driver has its own
    /// scan report; this is the one word Gunnar's status line is keyed on.
    /// </summary>
    public SurveyOutcome LastLook { get; private set; }

    /// <summary>Everything that might be worth taking, already judged by
    /// Teamster's own rules and already filtered to what the place allows. The
    /// driver filters these again by the work area and asks
    /// <see cref="Observe"/> about each; it never re-ranks them and never
    /// invents one.</summary>
    public IReadOnlyList<JobTarget> Candidates(NpcWorldEpoch world)
    {
        CollectionSurvey survey = _survey();
        LastLook = survey.Outcome;
        _byKey.Clear();

        var targets = new List<JobTarget>();
        if (survey.Outcome == SurveyOutcome.AreaUnavailable || survey.Outcome == SurveyOutcome.Unspecified)
        {
            // An area that could not be read offers nothing, and says nothing
            // about whether there is anything there. The driver's own verdict
            // for an inconclusive round is what keeps that distinction; an empty
            // list plus a Finished look would be a lie.
            return targets;
        }

        for (int index = 0; index < survey.Candidates.Count; index++)
        {
            CollectionCandidate candidate = survey.Candidates[index];
            if (!candidate.IsTakeable)
            {
                continue;
            }

            _byKey[candidate.Key] = candidate;
            targets.Add(new JobTarget(
                candidate.Key,
                world,
                new NpcPoint(candidate.Where.X, candidate.Where.Y, candidate.Where.Z),
                action: string.Empty,
                priority: 0,
                // Taking a loose thing consumes nothing, so a target's manifest
                // is empty and no provisioning step is written for it. That is
                // the whole difference between collecting and building, and it
                // is expressed as data rather than as a second pipeline.
                needs: default));
        }

        return targets;
    }

    /// <summary>The containers this job may draw from. Empty for an automatic
    /// collection: nothing he picks up costs anything, so there is nothing to
    /// provision, and a job with no sources must not be refused for it.
    /// </summary>
    public IReadOnlyList<SourceStock> Sources(NpcWorldEpoch world) => _sources();

    /// <summary>Null, and it is a decision with a reason rather than an omission.
    ///
    /// Every candidate here is a world object Gunnar's own survey has just read
    /// out of the loaded scene, so its place is proved rather than proposed, and
    /// a ground probe would buy nothing and cost a query per candidate. A role
    /// that proposed places - a builder choosing where a wall goes - would need
    /// one.</summary>
    public INpcAreaProbe? Probe => null;

    /// <summary>Null, with the cost stated rather than discovered.
    ///
    /// Gunnar's material accounting lives in <see cref="CollectionAccount"/>,
    /// which the shared runtime cannot see, so it has nothing to tell the
    /// planner about what another job has already set aside. Today that is
    /// harmless because Gunnar runs one job at a time and holds the only
    /// identity that could run a second. It stops being harmless the moment a
    /// second worker in this process plans against the same chests, and the fix
    /// is to supply this - not to hope.</summary>
    public INpcSourceAvailability? Availability => null;

    /// <summary>Whether one stop is still worth walking to. Re-asked at the
    /// moment he arrives as well as when the round was planned, because a job is
    /// long enough for the player to have taken the stone themselves.</summary>
    public StopStatus Observe(in RouteStop stop)
    {
        if (!_byKey.TryGetValue(stop.Key, out CollectionCandidate candidate))
        {
            // Not in the last survey. Unreadable rather than Gone: "I did not
            // see it this time" is not "it is not there", and only one of those
            // may end a target for good.
            return StopStatus.Unreadable;
        }

        return candidate.IsTakeable
            ? StopStatus.Actionable
            : candidate.IsExhausted ? StopStatus.AlreadyDone : StopStatus.Refused;
    }

    /// <summary>What one planned step is about, so the runtime that carries it
    /// out knows which object to walk to and what taking it yields.</summary>
    public bool TryResolve(string key, out CollectionCandidate candidate) =>
        _byKey.TryGetValue(key ?? string.Empty, out candidate!);

    /// <summary>How much goes in one trip, in Gunnar's own units, from the
    /// game's real carry-weight semantics plus whatever room the assigned cart
    /// has.
    ///
    /// <b>Units, not kilograms, because that is what the planner takes.</b> The
    /// conversion is this product's and has to be: only Teamster knows what one
    /// unit of what he is collecting weighs, and a shared runtime that learned
    /// that would have learned what stone is.</summary>
    public static NpcCarryCapacity TripCapacity(CarryBudget carry, CartCapacity cart, float unitKilograms)
    {
        int onFoot = carry.HowManyFit(unitKilograms, int.MaxValue);
        long total = (long)onFoot + cart.Units;
        return new NpcCarryCapacity(total > int.MaxValue ? int.MaxValue : (int)total);
    }
}
