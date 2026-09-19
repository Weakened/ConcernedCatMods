using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Routing;

/// <summary>What to do about one stop, having looked at it again.</summary>
internal enum StopDisposition
{
    /// <summary>Nobody asked.</summary>
    Unspecified = 0,

    /// <summary>Go there.</summary>
    Go = 1,

    /// <summary>Skip it and carry on with the round. <b>Keep the material</b>:
    /// what was picked up for this stop stays carried, and the reconciliation at
    /// the end of the round decides where it goes. Putting it back the moment a
    /// stop disappears is a walk to a chest for nothing.</summary>
    Skip = 2,

    /// <summary>Skip it this round and offer it again next round. The stop is
    /// still there and something about it could not be settled now - it moved,
    /// or it could not be read.</summary>
    Defer = 3,
}

/// <summary>How a round is going.</summary>
internal enum RouteProgress
{
    /// <summary>Nobody asked.</summary>
    Unspecified = 0,

    /// <summary>Walk to the stop this answer carries.</summary>
    Go = 1,

    /// <summary>Every stop has been dealt with, one way or another. The round is
    /// over and reconciliation decides what is left.</summary>
    Finished = 2,

    /// <summary>The route is meaningfully invalid and a new one is wanted.
    /// <b>Rare on purpose</b> - see <see cref="RouteExecution"/> for the three
    /// things that cause it and the long list of things that do not.</summary>
    Replan = 3,
}

/// <summary>What to do next, and why.</summary>
internal readonly struct RouteAdvance
{
    internal RouteAdvance(RouteProgress progress, RouteStop stop, bool hasStop, string reason)
    {
        Progress = progress;
        Stop = stop;
        HasStop = hasStop;
        Reason = reason ?? string.Empty;
    }

    internal RouteProgress Progress { get; }

    /// <summary>Where to go. Meaningless unless <see cref="HasStop"/>.</summary>
    internal RouteStop Stop { get; }

    internal bool HasStop { get; }

    /// <summary>Why, for a player sentence. Empty when there is nothing to
    /// explain.</summary>
    internal string Reason { get; }
}

/// <summary>How much a round is allowed to change its mind.</summary>
internal readonly struct RouteExecutionLimits
{
    internal RouteExecutionLimits(int mostReplans)
    {
        MostReplans = mostReplans;
    }

    /// <summary>How many times one round may ask for a new route before it gives
    /// up and finishes with what it has. <b>The whole defence against an endless
    /// replanning loop</b>: a world can always present a state where the next
    /// plan is as invalid as the last one, and an NPC that answered by planning
    /// again would stand still, thinking, forever.</summary>
    internal int MostReplans { get; }

    /// <summary>Twice. Once for the ordinary case - the round was planned, the
    /// world moved, plan again - and once more for the unlucky one. A third is
    /// a loop.</summary>
    internal static RouteExecutionLimits Default => new RouteExecutionLimits(2);
}

/// <summary>Walking a round, revalidating every stop before going to it.
///
/// <b>The rule this type exists to enforce: one target changing does not restart
/// the plan.</b> The loops this package replaces re-decide everything every
/// tick, from whatever is in front of them, and the result is an NPC that walks
/// to the chest eight times. Here the round is planned once and then walked, and
/// a stop that turns out not to be worth doing is <i>skipped</i> - the material
/// stays carried, the next stop is next, and nothing is recomputed.
///
/// <b>What causes a replan, in full.</b> Three things and no others:
///
/// <i>Every remaining stop was skipped and nothing was serviced.</i> The round
/// walked out of stops without doing anything, so the route it was given is
/// worth nothing and a new one is wanted.
///
/// <i>The caller says the plan is stale</i> - through
/// <see cref="RequestReplan"/>, which is what an area revision change or a world
/// reload looks like from up here. This type never reads either: staleness is
/// <c>JobPlan.IsStale</c>'s answer, asked by whoever holds the plan.
///
/// <i>Nothing else.</i> In particular <b>new targets never cause one</b>.
/// <see cref="NoteNewTargets"/> counts them and does nothing else, and they are
/// offered to the next round. An NPC that replanned on every new target in a
/// forest where trees keep loading in would never take a step, and that is not a
/// hypothetical - it is what a tick-by-tick loop does when the player walks
/// towards it.
///
/// And even a replan is capped at <see cref="RouteExecutionLimits.MostReplans"/>,
/// after which the round finishes with what it has. A cap is not a fudge here:
/// without one, "the route is meaningfully invalid" is a condition the world can
/// hold true forever.
///
/// <b>A throwing observer is unreadable, not fatal.</b> A role's completion
/// condition that throws is a broken role; letting it out of here would make it
/// a broken NPC in an unrelated product, because the driver above does not
/// catch.</summary>
internal sealed class RouteExecution
{
    private readonly RouteStop[] _stops;
    private readonly RouteExecutionLimits _limits;
    private readonly List<RouteStop> _skipped = new List<RouteStop>();
    private readonly List<RouteStop> _deferred = new List<RouteStop>();
    private readonly List<RouteStop> _serviced = new List<RouteStop>();
    private int _at;
    private bool _standing;

    internal RouteExecution(in StopSequence sequence, RouteExecutionLimits limits)
    {
        IReadOnlyList<RouteStop> stops = sequence.Stops;
        _stops = new RouteStop[stops.Count];
        for (int index = 0; index < stops.Count; index++)
        {
            _stops[index] = stops[index];
        }

        _limits = limits.MostReplans < 0 ? RouteExecutionLimits.Default : limits;
    }

    /// <summary>How many stops the round started with.</summary>
    internal int Planned => _stops.Length;

    /// <summary>The stops that were reached and done.</summary>
    internal IReadOnlyList<RouteStop> Serviced => _serviced;

    /// <summary>The stops that were not worth going to when they were looked at
    /// again.</summary>
    internal IReadOnlyList<RouteStop> Skipped => _skipped;

    /// <summary>The stops left for the next round - they moved, or could not be
    /// read. <b>Not the same as skipped</b>: a deferred stop is still wanted.
    /// </summary>
    internal IReadOnlyList<RouteStop> Deferred => _deferred;

    /// <summary>How many times this round has asked for a new route.</summary>
    internal int Replans { get; private set; }

    /// <summary>How many new targets turned up while the round was being walked.
    /// They are for the next round; nothing here acts on them.</summary>
    internal int PendingNewTargets { get; private set; }

    /// <summary>Whether <see cref="Next"/> is waiting for the current stop to be
    /// dealt with.</summary>
    internal bool IsStandingAtAStop => _standing;

    /// <summary>What to do next. Walks past every stop that is not worth going
    /// to and answers with the first that is.</summary>
    internal RouteAdvance Next(IStopObserver? observer)
    {
        if (_standing)
        {
            // The caller has been told to go somewhere and has neither arrived
            // nor abandoned it. Answering anything else here would be a second
            // destination for one body.
            return new RouteAdvance(RouteProgress.Go, _stops[_at], true, string.Empty);
        }

        while (_at < _stops.Length)
        {
            RouteStop stop = _stops[_at];
            StopDisposition disposition = Decide(Look(observer, stop));
            if (disposition == StopDisposition.Go)
            {
                _standing = true;
                return new RouteAdvance(RouteProgress.Go, stop, true, string.Empty);
            }

            if (disposition == StopDisposition.Defer)
            {
                _deferred.Add(stop);
            }
            else
            {
                _skipped.Add(stop);
            }

            _at++;
        }

        // Nothing serviced and something given up on for good: the route he was
        // handed is worth nothing and a new one is wanted. Stops merely deferred
        // do not count - they are still wanted, and a new route over the same
        // stops nobody could read would be the same route.
        if (_serviced.Count == 0 && _skipped.Count > 0 && Replans < _limits.MostReplans)
        {
            Replans++;
            return new RouteAdvance(
                RouteProgress.Replan,
                default,
                false,
                "every stop on the round turned out not to be worth going to, so the route is worth nothing");
        }

        return new RouteAdvance(RouteProgress.Finished, default, false, string.Empty);
    }

    /// <summary>The current stop was reached and done. The round moves on.
    /// </summary>
    internal void Arrived()
    {
        if (!_standing)
        {
            return;
        }

        _serviced.Add(_stops[_at]);
        _standing = false;
        _at++;
    }

    /// <summary>The current stop was walked to and could not be done - the walk
    /// failed, or it turned out to be refused on arrival. Skipped like any
    /// other, and <b>never a replan</b>: one stop going wrong is what a round
    /// expects.</summary>
    internal void Abandoned()
    {
        if (!_standing)
        {
            return;
        }

        _skipped.Add(_stops[_at]);
        _standing = false;
        _at++;
    }

    /// <summary>New targets turned up. Counted, and nothing else: they are
    /// offered to the next round. This method exists so that "new targets do not
    /// cause a replan" is something the code says rather than something the code
    /// happens not to do.</summary>
    internal void NoteNewTargets(int count)
    {
        if (count > 0)
        {
            PendingNewTargets += count;
        }
    }

    /// <summary>The caller has decided the plan is stale - the work area moved,
    /// the world reloaded - and wants a new route. Capped like any other replan,
    /// and refused once the cap is reached, at which point the round finishes
    /// with what it has.</summary>
    internal bool RequestReplan()
    {
        if (Replans >= _limits.MostReplans)
        {
            return false;
        }

        Replans++;
        _standing = false;
        return true;
    }

    /// <summary>The one place a role's answer becomes an action.
    ///
    /// Already done and gone are the same decision - skip, keep the material -
    /// and are kept apart only because the sentence differs. Moved and unreadable
    /// are deferred rather than skipped, because both stops are still wanted:
    /// one at a place that is now wrong, one at a place nobody could see. Refused
    /// and unreachable are skipped, because neither is going to change by
    /// standing here.</summary>
    internal static StopDisposition Decide(StopStatus status)
    {
        switch (status)
        {
            case StopStatus.Actionable:
                return StopDisposition.Go;
            case StopStatus.AlreadyDone:
            case StopStatus.Gone:
            case StopStatus.Unreachable:
            case StopStatus.Refused:
                return StopDisposition.Skip;
            case StopStatus.Moved:
            case StopStatus.Unreadable:
                return StopDisposition.Defer;
            default:
                // An observer that answered with a value this package does not
                // know is not a reason to walk anywhere.
                return StopDisposition.Defer;
        }
    }

    private static StopStatus Look(IStopObserver? observer, in RouteStop stop)
    {
        if (observer == null)
        {
            return StopStatus.Unreadable;
        }

        try
        {
            return observer.Observe(stop);
        }
        catch (Exception)
        {
            // A role's broken completion condition must not become a broken NPC
            // in another product. Unreadable is the honest answer: nobody
            // learned anything about this stop.
            return StopStatus.Unreadable;
        }
    }
}
