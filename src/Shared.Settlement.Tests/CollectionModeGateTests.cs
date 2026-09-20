using System.Collections.Generic;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;

namespace Shared.Settlement.Tests;

/// <summary>The one gate that decides whether a collection order may start: the
/// loop must hold the worker's actor mode, and only a <b>grant</b> is a hold.
///
/// <b>Why this needed its own file.</b> Until Concerned Foreman adopted the
/// Concerned NPC library there was exactly one implementation of the mode hold —
/// <c>ActorModeOwner</c> — and it answered either a grant or
/// <c>RefusedBusy</c>. The intake asked "was that RefusedBusy?", which was
/// indistinguishable from "was that a grant?" while that was true and is a defect
/// now that it is not: the library's arbiter answers <c>Unspecified</c> for an
/// identity it does not track, which happens whenever registration was refused at
/// load. "Not RefusedBusy" would have read that as permission, and the order would
/// have run holding nothing.
///
/// <b>What actually goes wrong then, if this gate is loose.</b> Every
/// <c>WalkTo</c> is silently refused, because the motion port obeys only the
/// holder — so Thorstein stands still with an order in flight and no reason a
/// player can see. Worse, nothing holds his body: <c>MayRetireBody</c> is true, so
/// <c>cf_worker despawn</c> would happily retire the body an active order's
/// material is sitting in.</summary>
public sealed class CollectionModeGateTests
{
    /// <summary>A hold that answers exactly what Concerned NPC's arbiter answers
    /// for an identity it does not track: <c>Unspecified</c> to everything, and
    /// the closed answer to every reader.
    ///
    /// Deliberately NOT a stand-in for a busy worker. <c>RefusedBusy</c> is
    /// already covered elsewhere; the point here is the outcome that is neither a
    /// grant nor the one refusal the old code looked for.</summary>
    private sealed class UntrackedHold : IActorModeHold
    {
        public WorkerKey Worker => WorkerKey.Thorstein;

        public ActorMode Mode => ActorMode.Unspecified;

        public string? JobId => null;

        public bool MayRelocateHome => false;

        public bool MayRetireBody => false;

        public int Entered { get; private set; }

        public bool IsHeldBy(string? jobId) => false;

        public ActorModeOutcome Enter(ActorMode mode, string jobId)
        {
            Entered++;
            return ActorModeOutcome.Unspecified;
        }

        public ActorModeOutcome Release(string jobId) => ActorModeOutcome.Unspecified;
    }

    private static void AddStones(CollectionRig rig, int count)
    {
        for (int index = 0; index < count; index++)
        {
            rig.AddSource(CollectedResource.Stone, 4f + (index % 10), -10f + ((index / 10) * 3f));
        }
    }

    [Fact]
    public void AnOrderIsRefusedWhenTheModeHoldAnswersAnythingButAGrant()
    {
        var hold = new UntrackedHold();
        var rig = new CollectionRig(modes: hold);
        AddStones(rig, 10);

        // Everything else about this order is acceptable: the same order on the
        // default hold is accepted by ASoloOrderCollectsMixedResourcesAndDelivers.
        // The only thing wrong with it is that nothing would hold the worker.
        Assert.Equal(CollectionIntakeRefusal.WorkerBusy, rig.Accept(rig.Order(stone: 3, wood: 0)));
        Assert.Equal(1, hold.Entered);

        // And it did not half-start: no order, nothing recorded, and a hundred
        // ticks later he has not been sent anywhere.
        Assert.False(rig.Loop.HasActiveOrder);
        Assert.Empty(rig.Custody.Accepted);
        rig.Tick(100);
        Assert.Equal(0, rig.Motion.WalksIssued);
    }

    [Fact]
    public void ABusyWorkerIsStillRefused()
    {
        // The pre-adoption refusal, kept: hardening the gate must not have
        // narrowed it to only the new case.
        var rig = new CollectionRig();
        AddStones(rig, 3);
        rig.Modes.Enter(ActorMode.Working, "haul-9");

        Assert.Equal(CollectionIntakeRefusal.WorkerBusy, rig.Accept(rig.Order(stone: 3, wood: 0)));
        Assert.Equal("haul-9", rig.Modes.JobId);
    }

    [Fact]
    public void AGrantIsEnteredAndAlreadyInModeAndNothingElse()
    {
        // The gate's whole vocabulary, stated once. Entered and AlreadyInMode are
        // grants; Unspecified, RefusedBusy, Released and NotHeld are not, and a
        // reader who believes otherwise writes the defect above.
        Assert.True(ActorModeGrants.IsGranted(ActorModeOutcome.Entered));
        Assert.True(ActorModeGrants.IsGranted(ActorModeOutcome.AlreadyInMode));

        var refusals = new List<ActorModeOutcome>
        {
            ActorModeOutcome.Unspecified,
            ActorModeOutcome.RefusedBusy,
            ActorModeOutcome.Released,
            ActorModeOutcome.NotHeld,
        };
        foreach (ActorModeOutcome refusal in refusals)
        {
            Assert.False(ActorModeGrants.IsGranted(refusal), refusal + " is not a grant");
        }
    }

    [Fact]
    public void ReEnteringTheSameModeForTheSameJobIsAGrantAndKeepsTheOrder()
    {
        // AlreadyInMode has to count as a grant, or an order re-offered by a
        // player's second click would be refused as busy by its own hold.
        var rig = new CollectionRig();
        AddStones(rig, 3);
        rig.Modes.Enter(ActorMode.Surveying, "collect-1");

        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 3, wood: 0, id: "collect-1")));
        Assert.True(rig.Loop.HasActiveOrder);
    }
}
