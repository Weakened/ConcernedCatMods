using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.Settlement.Collection;
using TheConcernedCat.Settlement.Collection.Planning;
using TheConcernedCat.Settlement.Custody;

namespace Shared.Settlement.Tests;

/// <summary>#379: the collection loop's write-ahead witness. Every movement of
/// material - a pick, a felling hit, a deposit - is announced before it happens
/// and concluded after; a refused announcement means it does not happen; a
/// refused start means no order starts or resumes.</summary>
public sealed class CollectionWitnessTests
{
    /// <summary>Records what the loop told it, and what the world had done at
    /// that moment.</summary>
    private sealed class RecordingWitness : ICollectionWorkWitness
    {
        private readonly Func<int> _movements;

        public RecordingWitness(Func<int> movements) => _movements = movements;

        public List<string> Log { get; } = new List<string>();

        public bool RefuseStart { get; set; }

        public int RefuseIntentFrom { get; set; } = int.MaxValue;

        public int Intents { get; private set; }

        public int Concludes { get; private set; }

        public bool Pending { get; private set; }

        public bool Start(CollectionOrderDefinition order, out CollectionAttentionReason reason, out string why)
        {
            Log.Add("start");
            reason = RefuseStart ? CollectionAttentionReason.TransferUncertain : CollectionAttentionReason.Unspecified;
            why = RefuseStart ? "a movement was in flight when the process died" : string.Empty;
            return !RefuseStart;
        }

        public bool Intend(string note, out CollectionAttentionReason reason, out string why)
        {
            Assert.False(Pending, "a second movement was announced on top of one with no outcome");
            if (Intents >= RefuseIntentFrom)
            {
                reason = CollectionAttentionReason.JournalReadOnly;
                why = "the plan could not be written";
                Log.Add("refused@" + _movements());
                return false;
            }

            Intents++;
            Pending = true;
            Log.Add("intend@" + _movements());
            reason = CollectionAttentionReason.Unspecified;
            why = string.Empty;
            return true;
        }

        public bool Conclude(bool established, string note, out string why)
        {
            Assert.True(Pending, "a movement was concluded that nobody announced");
            Concludes++;
            Pending = false;
            Log.Add((established ? "conclude@" : "uncertain@") + _movements());
            why = string.Empty;
            return true;
        }

        public void Suspend(string note) => Log.Add("suspend");

        public void Ended(bool finished, string note) => Log.Add(finished ? "settled" : "ended");
    }

    private static (CollectionRig Rig, RecordingWitness Witness) Rig(bool felling = false)
    {
        CollectionRig? rig = null;
        var witness = new RecordingWitness(() => rig!.Pickup.Picks + rig.Custody.Executed.Count + rig.Pickup.Strikes.Count);
        rig = new CollectionRig(
            withFelling: felling,
            scopeSource: felling ? WorkScopeSource.HarvestDesignation : WorkScopeSource.DefaultCampCircle,
            witness: witness);
        return (rig, witness);
    }

    [Fact]
    public void EveryPickAndDepositIsAnnouncedBeforeItHappensAndConcludedAfter()
    {
        (CollectionRig rig, RecordingWitness witness) = Rig();
        for (int index = 0; index < 4; index++)
        {
            rig.AddSource(CollectedResource.Stone, 4f + index, 4f);
        }

        Assert.Equal(CollectionIntakeRefusal.Unspecified, rig.Accept(rig.Order(stone: 4, wood: 0)));
        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));

        Assert.Equal("start", witness.Log[0]);
        Assert.Equal("settled", witness.Log[witness.Log.Count - 1]);
        Assert.Equal(witness.Intents, witness.Concludes);
        Assert.Equal(rig.Pickup.Picks + rig.Custody.Executed.Count(intent => intent.To.Place == CustodyPlace.Destination), witness.Intents);

        // Each announcement is written with the world exactly as it was, and its
        // conclusion with exactly one movement more.
        for (int index = 0; index < witness.Log.Count; index++)
        {
            if (!witness.Log[index].StartsWith("intend@", StringComparison.Ordinal))
            {
                continue;
            }

            int before = int.Parse(witness.Log[index].Substring(7));
            string next = witness.Log[index + 1];
            Assert.StartsWith("conclude@", next);
            Assert.True(int.Parse(next.Substring(9)) > before, "a movement was concluded without having happened: " + next);
        }
    }

    [Fact]
    public void EveryFellingHitIsAnnouncedAndConcluded()
    {
        (CollectionRig rig, RecordingWitness witness) = Rig(felling: true);
        rig.AddTree(2f, 2f, hits: 3, wood: 2);
        rig.Accept(rig.Order(stone: 0, wood: 2));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Completed));
        Assert.Equal(3, rig.Pickup.Strikes.Count);
        Assert.Equal(witness.Intents, witness.Concludes);
        Assert.Equal(3 + rig.Custody.Executed.Count(intent => intent.To.Place == CustodyPlace.Destination), witness.Intents);
    }

    [Fact]
    public void ARefusedAnnouncementMeansTheMovementNeverHappens()
    {
        (CollectionRig rig, RecordingWitness witness) = Rig();
        rig.AddSource(CollectedResource.Stone, 4f, 4f);
        rig.AddSource(CollectedResource.Stone, 5f, 4f);
        witness.RefuseIntentFrom = 1;
        rig.Accept(rig.Order(stone: 2, wood: 0));

        Assert.True(rig.RunUntil(() => rig.Loop.State == CollectionOrderState.Paused));
        Assert.Equal(1, rig.Pickup.Picks);
        Assert.Equal(CollectionAttentionReason.JournalReadOnly, rig.Loop.Reason);
        rig.Tick(200);
        Assert.Equal(1, rig.Pickup.Picks);
        Assert.Empty(rig.Custody.Executed.Where(intent => intent.To.Place == CustodyPlace.Destination));
    }

    [Fact]
    public void ARefusedStartAcceptsNothingAndRecordsNothing()
    {
        (CollectionRig rig, RecordingWitness witness) = Rig();
        rig.AddSource(CollectedResource.Stone, 4f, 4f);
        witness.RefuseStart = true;

        Assert.Equal(CollectionIntakeRefusal.WorkPlanBlocked, rig.Accept(rig.Order(stone: 1, wood: 0)));
        Assert.Empty(rig.Custody.Accepted);
        Assert.Null(rig.Modes.JobId);
        rig.Tick(100);
        Assert.Equal(0, rig.Pickup.Picks);
    }

    [Fact]
    public void AResumeThePlanRefusesStaysStoppedAndMovesNothing()
    {
        (CollectionRig rig, RecordingWitness witness) = Rig();
        for (int index = 0; index < 3; index++)
        {
            rig.AddSource(CollectedResource.Stone, 4f + index, 4f);
        }

        rig.Accept(rig.Order(stone: 3, wood: 0));
        Assert.True(rig.RunUntil(() => rig.Pickup.Picks == 1));
        Assert.Equal(ControlOutcome.Done, rig.Loop.Pause(rig.Now).Outcome);
        Assert.Contains("suspend", witness.Log);

        witness.RefuseStart = true;
        Assert.Equal(ControlOutcome.Refused, rig.Loop.Resume(rig.Now).Outcome);
        Assert.Equal(CollectionOrderState.NeedsAttention, rig.Loop.State);
        int picks = rig.Pickup.Picks;
        rig.Tick(200);
        Assert.Equal(picks, rig.Pickup.Picks);
    }
}
