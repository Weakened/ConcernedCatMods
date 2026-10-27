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
    private readonly NpcJobDriver _driver;

    internal GunnarTourPump(NpcJobDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    internal NpcJobDriver Driver => _driver;

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
                return new TourAdvance(
                    TourProgress.Do,
                    new TourStep(step.Target.Key, _driver.Rounds, step.Tour, new CollectionPoint(at.X, at.Y, at.Z)),
                    true,
                    string.Empty);
            case NpcJobProgress.Finished:
                return new TourAdvance(TourProgress.Finished, default, false, string.Empty);
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
