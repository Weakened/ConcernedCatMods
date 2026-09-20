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

        /// <summary>The point of the whole stand-in: nothing can establish whose
        /// mode this is, which is a different fact from "he is busy" and from
        /// "his body is gone".</summary>
        public bool IsIdentityKnown => false;

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

    /// <summary>A hold that knows perfectly well whose mode it is and still
    /// refuses with an outcome that is neither a grant nor <c>RefusedBusy</c>.
    ///
    /// <b>Not reachable today, and that is the point of it.</b> It isolates the
    /// grant gate from the labelling: the gate must refuse on "not a grant" alone,
    /// for reasons nobody has invented yet, independently of whether the identity
    /// is known. A gate that only caught the unknown-identity case would be the
    /// same defect one step along.</summary>
    private sealed class RefusingKnownHold : IActorModeHold
    {
        public WorkerKey Worker => WorkerKey.Thorstein;

        public bool IsIdentityKnown => true;

        public ActorMode Mode => ActorMode.Resting;

        public string? JobId => null;

        public bool MayRelocateHome => true;

        public bool MayRetireBody => true;

        public int Entered { get; private set; }

        public bool IsHeldBy(string? jobId) => false;

        public ActorModeOutcome Enter(ActorMode mode, string jobId)
        {
            Entered++;
            return ActorModeOutcome.Unspecified;
        }

        public ActorModeOutcome Release(string jobId) => ActorModeOutcome.NotHeld;
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
        var hold = new RefusingKnownHold();
        var rig = new CollectionRig(modes: hold);
        AddStones(rig, 10);

        // Everything else about this order is acceptable: the same order on the
        // default hold is accepted by ASoloOrderCollectsMixedResourcesAndDelivers.
        // The only thing wrong with it is that nothing would hold the worker.
        //
        // Refused as WorkerBusy here because the identity IS known - the hold just
        // would not give it up, for a reason this build has no name for. The label
        // is not what this test is about; that it refuses at all is.
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
    public void AnUnknownIdentityIsDiagnosedAsItselfAndNotAsABusyWorker()
    {
        // The label, not just the refusal. "Thorstein is busy with another job"
        // sends a player looking for a job that does not exist and waiting for it
        // to end, which it never will: nothing is busy, and only restarting with
        // Concerned NPC installed changes anything. The workerBusy intake fact is
        // correctly false here, which is exactly why the label has to be decided
        // by asking the hold rather than by assuming the refusal came from it.
        var rig = new CollectionRig(modes: new UntrackedHold());
        AddStones(rig, 10);

        Assert.Equal(
            CollectionIntakeRefusal.WorkerIdentityUnknown, rig.Accept(rig.Order(stone: 3, wood: 0)));

        // And the sentence a player reads names the real cause and says waiting
        // will not help.
        string sentence = CollectionIntake.Describe(CollectionIntakeRefusal.WorkerIdentityUnknown);
        Assert.Contains("Concerned NPC", sentence);
        Assert.DoesNotContain("busy with another job", sentence);
    }

    [Fact]
    public void AnAdoptedOrderThatCannotTakeHoldIsNotReportedAsAMissingBody()
    {
        // His body is present and fine. What is missing is the shared runtime's
        // record of who he is, so every WalkTo is refused by a motion port that
        // obeys only the holder - and the old path called that WorkerBodyLost,
        // which would send a player hunting for a body standing in front of them.
        var rig = new CollectionRig(modes: new UntrackedHold());
        AddStones(rig, 10);

        // A non-terminal order the record kept, whose area and chest belong to
        // THIS world load, so nothing about a rebind is in the way and the only
        // thing standing between him and a walk is the hold.
        CollectionOrderDefinition recorded = rig.Order(stone: 3, wood: 0);
        rig.Custody.RecoverableOrder = recorded;
        rig.Custody.RecoveredState = CollectionOrderState.Paused;
        rig.Custody.Accepted.Add(recorded);
        rig.Pickup.Order = recorded;

        Assert.True(rig.Loop.AdoptRecovered(rig.Now));
        Assert.False(rig.Loop.NeedsRebind);
        Assert.True(rig.Motion.IsPresent);

        rig.Loop.Resume(rig.Now);
        rig.Tick(200);

        Assert.Equal(CollectionAttentionReason.WorkerIdentityUnknown, rig.Loop.Reason);
        Assert.NotEqual(CollectionAttentionReason.WorkerBodyLost, rig.Loop.Reason);

        string sentence = CollectionSentences.Describe(CollectionAttentionReason.WorkerIdentityUnknown);
        Assert.Contains("Concerned NPC", sentence);
        Assert.DoesNotContain("his body is missing", sentence);
    }

    [Fact]
    public void ABusyWorkerIsStillRefusedAsBusy()
    {
        // The pre-adoption refusal, kept and still labelled as itself: adding the
        // new diagnosis must not have relabelled the old one. An ActorModeOwner
        // always knows whose mode it is, so IsIdentityKnown is true here and the
        // refusal stays WorkerBusy.
        var rig = new CollectionRig();
        AddStones(rig, 3);
        rig.Modes.Enter(ActorMode.Working, "haul-9");
        Assert.True(rig.Hold.IsIdentityKnown);

        Assert.Equal(CollectionIntakeRefusal.WorkerBusy, rig.Accept(rig.Order(stone: 3, wood: 0)));
        Assert.Equal("haul-9", rig.Modes.JobId);
    }

    [Fact]
    public void AGenuinelyMissingBodyIsStillReportedAsAMissingBody()
    {
        // The other half of the same guard: the new reason must not have taken
        // over the case it was carved out of. Identity known, body gone.
        var rig = new CollectionRig();
        AddStones(rig, 10);
        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 3, wood: 0)));

        rig.Motion.IsPresent = false;
        rig.Tick(200);

        Assert.True(rig.Hold.IsIdentityKnown);
        Assert.Equal(CollectionAttentionReason.WorkerBodyLost, rig.Loop.Reason);
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
