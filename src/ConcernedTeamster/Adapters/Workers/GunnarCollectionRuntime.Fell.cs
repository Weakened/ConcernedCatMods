using System;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Felling;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary><c>ct_collect fell</c>: Gunnar's half of `DECISIONS.md` D19 (#381
/// item 5). One hit on the eligible small tree nearest him, inside the area the
/// player set, with an axe he carries; on a fell, the Wood his own hit dropped
/// that he can carry, taken through the collection port's pinned pickup.
///
/// <b>Thorstein's scope, as the lead default pending owner confirmation.</b> The
/// owner granted felling on #381 without setting Gunnar's scope; this is D19's
/// built scope, unchanged, under a switch of Gunnar's own.
///
/// <b>One hit per command, and nothing else.</b> No tour fells, no survey plans a
/// tree, nothing here walks him and nothing repeats on its own: each hit is one
/// explicit command. Felling inside a tour is not built.
///
/// <b>Every gate is re-asked in the frame of the hit.</b> This file asks the
/// switches, the world, the work-authority rule, the probe, his body and its
/// record, what else is in flight, an open tour plan and the area
/// (<see cref="FellOrderGate"/>); the felling port then re-measures every D19
/// site rule (<see cref="SmallTreeFelling.CheckSite"/>) in the same call that
/// lands the hit.</summary>
internal sealed partial class GunnarCollectionRuntime
{
    private readonly GunnarFellingPort _felling = new GunnarFellingPort();

    /// <summary>Teamster's master switch, the collection switch and the
    /// felling's own switch, all three. An unreadable setting is not an opted-in
    /// one.</summary>
    private bool FellOptedIn()
    {
        try
        {
            return OptedIn() && _settings.GunnarFellsSmallTrees.Value;
        }
        catch
        {
            return false;
        }
    }

    private WorkAuthorityVerdict FellAuthority()
    {
        return WorkAuthorityPolicy.Evaluate(GunnarWorkAuthority.ReadWorldFacts(FellOptedIn()));
    }

    /// <summary>One hit, if every gate allows it.</summary>
    private string Fell()
    {
        Humanoid? worker = _worker();
        NpcWorldEpoch world = CurrentEpoch();
        bool areaThisWorld = _area != null && !world.IsUnknown && world.Matches(_areaEpoch);

        var request = new FellRequest(
            collectionEnabled: OptedIn(),
            fellingEnabled: FellOptedIn(),
            worldIsUp: _lifecycle.WorldIsUp,
            authority: FellAuthority(),
            seamAvailable: _seamAvailable(),
            workerPresent: worker != null && !worker.IsDead(),
            workerRecordUnwritable: _recordUnwritable(),
            workerRecordUnreadable: _recordUnreadable(),
            transferInFlight: _ordered || TourRunning || _port.Phase == PickPhase.Gathering,
            interruptedTourOpen: _tourPlan.BlocksOrders,
            areaChosen: areaThisWorld);

        FellRefusal refusal = FellOrderGate.Evaluate(request);
        if (refusal != FellRefusal.None)
        {
            return FellOrderGate.Describe(refusal, request.Authority);
        }

        if (!_lease.TryAcquire(_identity()))
        {
            return "Gunnar is busy with another job, or his job authority is unavailable. Finish or cancel that job first.";
        }

        GunnarFellReport report;
        try
        {
            report = _felling.TryFellOne(
                worker, _area, areaThisWorld, FellOptedIn, FellAuthority, _limits.PickupReachMetres, _port, WoodRoom,
                message => _log?.LogInfo(message));
        }
        finally
        {
            _lease.Release();
        }

        if (report.WoodTaken > 0)
        {
            // Recorded like a pick: what measurably arrived in him, under the
            // step that caused it.
            _account.Record(NextStep("fell"), FelledWoodTake.WoodPrefab, report.WoodTaken, StopResult.Took());
        }

        return report.Message;
    }

    /// <summary>How many units of Wood still fit under his carry limit, or
    /// <c>null</c> when that could not be read (and then nothing is taken).
    /// </summary>
    private int? WoodRoom(Humanoid worker)
    {
        float? unit = UnitKilogramsOf(FelledWoodTake.WoodPrefab);
        return unit.HasValue ? UnitsThatFit(worker, unit.Value, int.MaxValue) : null;
    }
}
