using System;
using TheConcernedCat.ConcernedNPC.Jobs;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The shared runtime's job driver, as the game-free
/// <see cref="CollectionTour"/> sees it (#381).
///
/// <b>The whole mapping, one line per value.</b> This is the only place the
/// driver's vocabulary meets the tour's, so a test of the tour plus a reading of
/// this file is the whole argument. The load a stop belongs to is the driver's
/// own <see cref="PlannedStep.Tour"/>, stamped with the round that planned it,
/// because every round numbers its loads from zero again.
///
/// <b>A collect step is refused, not carried out.</b> A collection job offers no
/// chests to draw from, so the planner never writes one; if it ever did, taking
/// from a chest is not something this tour may do, and the job stops saying so.
/// </summary>
internal sealed class GunnarTourPump : ITourPump
{
    /// <summary>How many fresh looks one tour may take at an area whose look was
    /// cut short at the candidate ceiling, before it stops and says so.</summary>
    internal const int MostLooks = 16;

    private readonly Func<NpcJobDriver> _start;
    private readonly Func<SurveyOutcome> _lastLook;
    private NpcJobDriver _driver;
    private int _generation;

    /// <param name="start">Starts a fresh job over the same area, chest and
    /// capacity - the next look.</param>
    /// <param name="lastLook">How the most recent survey went.</param>
    internal GunnarTourPump(Func<NpcJobDriver> start, Func<SurveyOutcome> lastLook)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _lastLook = lastLook ?? throw new ArgumentNullException(nameof(lastLook));
        _driver = _start();
    }

    internal NpcJobDriver Driver => _driver;

    /// <summary>The driver's "finished" is only this tour's finished when the
    /// look it planned from was complete. A look cut short at the ceiling means
    /// there is more: a fresh job looks again. A look that met unloaded ground,
    /// or could not read the area, cannot say the area is done, so the tour
    /// stops and says so rather than reporting an empty area that is not one.
    /// </summary>
    private TourAdvance Finished()
    {
        switch (_lastLook())
        {
            case SurveyOutcome.Finished:
                return new TourAdvance(TourProgress.Finished, default, false, string.Empty);
            case SurveyOutcome.Truncated when _generation + 1 < MostLooks:
                _generation++;
                _driver = _start();
                return _driver.Progress == NpcJobProgress.Stopped
                    ? new TourAdvance(TourProgress.Stopped, default, false, _driver.Reason)
                    : new TourAdvance(TourProgress.Waiting, default, false, "looking at the rest of the area");
            case SurveyOutcome.Truncated:
                return new TourAdvance(TourProgress.Stopped, default, false,
                    "the area held more than one tour may look at; start him again to carry on");
            case SurveyOutcome.NotLoaded:
                return new TourAdvance(TourProgress.Stopped, default, false,
                    "part of the area is not loaded, so whether anything is left there could not be established; " +
                    "stand nearer its middle and start him again");
            default:
                return new TourAdvance(TourProgress.Stopped, default, false,
                    "the area could not be read, so it is not reported as done");
        }
    }

    public TourAdvance Next(CollectionPoint standingAt)
    {
        NpcJobAdvance advance = _driver.Next(new NpcPoint(standingAt.X, standingAt.Y, standingAt.Z));
        switch (advance.Progress)
        {
            case NpcJobProgress.Do when advance.HasStep:
                PlannedStep step = advance.Step;
                if (step.IsCollect)
                {
                    _driver.Abandon();
                    return new TourAdvance(TourProgress.Stopped, default, false,
                        "the plan asked him to take something out of a chest, which a collection tour never does");
                }

                NpcPoint at = step.Step.At;
                // A load is the same load only within one round of one look:
                // both restart their numbering, so both go into the round.
                int round = (_generation * 10000) + _driver.Rounds;
                return new TourAdvance(
                    TourProgress.Do,
                    new TourStep(step.Target.Key, round, step.Tour, new CollectionPoint(at.X, at.Y, at.Z)),
                    true,
                    string.Empty);
            case NpcJobProgress.Finished:
                return Finished();
            case NpcJobProgress.Waiting:
                return new TourAdvance(TourProgress.Waiting, default, false, advance.Reason);
            case NpcJobProgress.Stopped:
                return new TourAdvance(TourProgress.Stopped, default, false, advance.Reason);
            default:
                return new TourAdvance(TourProgress.Unspecified, default, false, advance.Reason);
        }
    }

    public void Done() => _driver.Done();

    public void Skipped() => _driver.Skipped();

    public void Failed() => _driver.Failed();

    public void Abandon() => _driver.Abandon();
}
