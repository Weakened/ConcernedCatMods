using TheConcernedCat.ConcernedTeamster.Domain.Collection;
using TheConcernedCat.Workers;

namespace ConcernedTeamster.Tests;

/// <summary>The gate on an explicitly ordered pick (#381): what the call site
/// asks before it reaches the game at all.
///
/// The runtime that asks binds Unity, so every one of these clauses would
/// otherwise be unexercisable. The one that matters most is the first: with the
/// switch off, nothing else is even consulted.</summary>
public sealed class CollectionOrderGateTests
{
    private const string Stone = "Pickable_Stone(Clone)";
    private const string Branch = "Pickable_Branch(Clone)";

    private static CollectionOrderRequest Ready(
        bool featureEnabled = true,
        bool worldIsUp = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool seamAvailable = true,
        bool workerPresent = true,
        bool workerRecordUnwritable = false,
        bool pickInFlight = false,
        bool pointedAtASource = true,
        string sourceObjectName = Stone,
        string yieldItemPrefabName = "Stone",
        int yieldUnits = 1,
        float reachMetres = 2.2f,
        float distanceMetres = 1f) =>
        new CollectionOrderRequest(
            featureEnabled,
            worldIsUp,
            authority,
            seamAvailable,
            workerPresent,
            workerRecordUnwritable,
            pickInFlight,
            pointedAtASource,
            sourceObjectName,
            yieldItemPrefabName,
            yieldUnits,
            reachMetres,
            distanceMetres);

    [Fact]
    public void ADefaultedRequest_IsRefusedBecauseNobodyOptedIn()
    {
        Assert.Equal(
            CollectionOrderRefusal.FeatureOff,
            CollectionOrderGate.Evaluate(default));
    }

    [Fact]
    public void WithTheSwitchOff_NothingElseIsConsulted()
    {
        // Everything else is perfect. The answer is still that the player has
        // not opted in, which is what "a player who has not opted in gets none
        // of it" has to mean at the gate.
        Assert.Equal(
            CollectionOrderRefusal.FeatureOff,
            CollectionOrderGate.Evaluate(Ready(featureEnabled: false)));
    }

    [Fact]
    public void EveryWorkAuthorityAnswerButGranted_Refuses()
    {
        // Enumerated rather than listed, so a verdict added later is covered
        // without anybody remembering to add a row here.
        foreach (WorkAuthorityVerdict verdict in System.Enum.GetValues<WorkAuthorityVerdict>())
        {
            CollectionOrderRefusal refusal = CollectionOrderGate.Evaluate(Ready(authority: verdict));
            Assert.Equal(
                verdict == WorkAuthorityVerdict.Granted
                    ? CollectionOrderRefusal.None
                    : CollectionOrderRefusal.WorkRefused,
                refusal);
        }
    }

    [Fact]
    public void WhileTheWorldIsGoingAway_NoOrderStarts()
    {
        // The narrow mint a review found: the game is shutting down, its
        // singletons still answer so the authority rule still grants, but the
        // lifecycle has already dropped the record of what was picked. An order
        // accepted here would be an order with no record standing behind it.
        Assert.Equal(
            CollectionOrderRefusal.WorldIsGoingAway,
            CollectionOrderGate.Evaluate(Ready(worldIsUp: false)));
    }

    [Fact]
    public void AnUnverifiedGameBuild_Refuses()
    {
        Assert.Equal(
            CollectionOrderRefusal.SeamUnavailable,
            CollectionOrderGate.Evaluate(Ready(seamAvailable: false)));
    }

    [Fact]
    public void NoWorkerBody_Refuses()
    {
        Assert.Equal(
            CollectionOrderRefusal.NoWorker,
            CollectionOrderGate.Evaluate(Ready(workerPresent: false)));
    }

    // -- the sentence a player actually reads when a write has failed --
    //
    // The defect: a persistence failure makes BoundBody answer null, the runtime
    // read that as "no worker", and the player was told "Gunnar is not here.
    // Bring him into the world first." about a Gunnar standing in front of them.
    // Nothing named the failed write, nothing said he was holding something, and
    // the only other true sentence reachable was ct_haul retire's unreadable
    // refusal - whose only escape is `retire force`, which destroys the stone. A
    // false sentence pointing at the destructive door is worse than a refusal.

    [Fact]
    public void ABodyWhoseRecordCouldNotBeWritten_IsNotReportedAsAbsent()
    {
        // Both flags are what the runtime really passes in that state: BoundBody
        // answers null, so workerPresent is false AND the record flag is true.
        // The record flag has to win, or the player gets the falsehood.
        Assert.Equal(
            CollectionOrderRefusal.WorkerRecordUnwritable,
            CollectionOrderGate.Evaluate(
                Ready(workerPresent: false, workerRecordUnwritable: true)));
    }

    [Fact]
    public void ThatRefusalOutranksNoWorker_EvenWithTheBodyReportedPresent()
    {
        Assert.Equal(
            CollectionOrderRefusal.WorkerRecordUnwritable,
            CollectionOrderGate.Evaluate(Ready(workerRecordUnwritable: true)));
    }

    [Fact]
    public void ARoomThatRefusesWorkIsStillReportedFirst()
    {
        // Order matters in the other direction too: the player's own switch and
        // the room come before anything about Gunnar, so a failed write does not
        // mask "you are not the host".
        Assert.Equal(
            CollectionOrderRefusal.FeatureOff,
            CollectionOrderGate.Evaluate(
                Ready(featureEnabled: false, workerRecordUnwritable: true)));
        Assert.Equal(
            CollectionOrderRefusal.WorkRefused,
            CollectionOrderGate.Evaluate(
                Ready(authority: WorkAuthorityVerdict.NotHost, workerRecordUnwritable: true)));
    }

    [Fact]
    public void TheSentenceSaysHeIsHereHoldingSomethingAndThatAReloadRestoresHim()
    {
        // The three things the old sentence got wrong or left out. Asserted as
        // content rather than as an exact string so the wording can be improved
        // without the test becoming a transcription.
        string said = CollectionOrderGate.Describe(CollectionOrderRefusal.WorkerRecordUnwritable);
        Assert.Contains("is here", said);
        Assert.Contains("holding", said);
        Assert.Contains("could not write", said);
        Assert.Contains("Reload the world", said);

        // And it must not tell the player Gunnar is absent, which is the whole
        // defect, nor route them to the door that destroys what he holds.
        Assert.DoesNotContain("not here", said);
        Assert.DoesNotContain("force", said);
    }

    [Fact]
    public void EveryRefusalStillHasASentenceOfItsOwn()
    {
        // A new refusal with no case falls to "nobody recorded", which is the
        // shape of the defect being fixed: a reason the player cannot act on.
        foreach (CollectionOrderRefusal refusal in
                 System.Enum.GetValues(typeof(CollectionOrderRefusal)))
        {
            string said = CollectionOrderGate.Describe(refusal);
            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("nobody recorded", said);
        }
    }

    [Fact]
    public void APickAlreadyInFlight_Refuses()
    {
        Assert.Equal(
            CollectionOrderRefusal.AlreadyWorking,
            CollectionOrderGate.Evaluate(Ready(pickInFlight: true)));
    }

    [Fact]
    public void PointingAtNothing_Refuses()
    {
        Assert.Equal(
            CollectionOrderRefusal.NothingPointedAt,
            CollectionOrderGate.Evaluate(Ready(pointedAtASource: false)));
    }

    [Theory]
    [InlineData(Stone, "Stone")]
    [InlineData(Branch, "Wood")]
    [InlineData("Pickable_Stone", "Stone")]
    public void TheTwoKindsTheOwnerAuthorized_AreAccepted(string source, string yield)
    {
        Assert.Equal(
            CollectionOrderRefusal.None,
            CollectionOrderGate.Evaluate(Ready(sourceObjectName: source, yieldItemPrefabName: yield)));
    }

    [Theory]
    // Food, a crop, a flower, a mineral vein, a player's dropped pile and a
    // decorated copy embedded in a generated location: none is on the allowlist,
    // and identity is asked before anything else about the thing.
    [InlineData("Pickable_Mushroom(Clone)")]
    [InlineData("Pickable_Thistle(Clone)")]
    [InlineData("Pickable_Dandelion(Clone)")]
    [InlineData("Pickable_SurtlingCoreStand(Clone)")]
    [InlineData("Pickable_Stone_1(Clone)")]
    [InlineData("pickable_stone(Clone)")]
    [InlineData("")]
    public void AnythingElse_IsNotOneHeCollects(string source)
    {
        Assert.Equal(
            CollectionOrderRefusal.NotOneHeCollects,
            CollectionOrderGate.Evaluate(Ready(sourceObjectName: source)));
    }

    [Theory]
    // The right kind of source whose yield has been changed: a different item, a
    // bigger amount, none at all. Fails closed rather than being picked and
    // counted wrong.
    [InlineData("Wood", 1)]
    [InlineData("Stone", 2)]
    [InlineData("Stone", 0)]
    [InlineData("", 1)]
    public void AYieldThatIsNotVanillas_Refuses(string yield, int units)
    {
        Assert.Equal(
            CollectionOrderRefusal.YieldIsNotVanilla,
            CollectionOrderGate.Evaluate(
                Ready(sourceObjectName: Stone, yieldItemPrefabName: yield, yieldUnits: units)));
    }

    [Fact]
    public void ABranchThatWouldGiveStone_Refuses()
    {
        Assert.Equal(
            CollectionOrderRefusal.YieldIsNotVanilla,
            CollectionOrderGate.Evaluate(
                Ready(sourceObjectName: Branch, yieldItemPrefabName: "Stone")));
    }

    [Fact]
    public void FurtherAwayThanHeCanReach_Refuses()
    {
        // Nothing in this slice walks him anywhere, so an order he cannot reach
        // is refused rather than turned into movement nobody authorized.
        Assert.Equal(
            CollectionOrderRefusal.OutOfReach,
            CollectionOrderGate.Evaluate(Ready(reachMetres: 2.2f, distanceMetres: 2.3f)));
        Assert.Equal(
            CollectionOrderRefusal.None,
            CollectionOrderGate.Evaluate(Ready(reachMetres: 2.2f, distanceMetres: 2.2f)));
    }

    [Theory]
    [InlineData(2.2f, float.NaN)]
    [InlineData(2.2f, float.PositiveInfinity)]
    [InlineData(float.NaN, 1f)]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 1f)]
    public void AnUnreadableDistanceOrReach_Refuses(float reach, float distance)
    {
        Assert.Equal(
            CollectionOrderRefusal.Unreadable,
            CollectionOrderGate.Evaluate(Ready(reachMetres: reach, distanceMetres: distance)));
    }

    [Fact]
    public void ShippedReach_IsTheProductsOwnLimit()
    {
        Assert.Equal(
            CollectionOrderRefusal.None,
            CollectionOrderGate.Evaluate(
                Ready(reachMetres: CollectionLimits.Default.PickupReachMetres, distanceMetres: 0f)));
    }

    [Fact]
    public void CollectionIsOffByDefault()
    {
        Assert.False(
            GunnarCollectionDefaults.CollectionEnabled,
            "installing Teamster for its telemetry must never enrol anyone in picking things up");
    }

    [Fact]
    public void EveryRefusal_HasASentenceThatIsNotTheBugFallback()
    {
        foreach (CollectionOrderRefusal refusal in System.Enum.GetValues<CollectionOrderRefusal>())
        {
            string sentence = CollectionOrderGate.Describe(refusal);
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("that is a bug", sentence);
        }
    }

    [Fact]
    public void AWorkRefusal_IsReportedInTheAuthorityRulesOwnWords()
    {
        Assert.Equal(
            WorkAuthorityPolicy.Describe(WorkAuthorityVerdict.OtherPeersConnected),
            CollectionOrderGate.Describe(
                CollectionOrderRefusal.WorkRefused, WorkAuthorityVerdict.OtherPeersConnected));
    }

    [Theory]
    [InlineData("Pickable_Stone(Clone)", "Pickable_Stone")]
    [InlineData("Pickable_Stone", "Pickable_Stone")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void PrefabNameOf_StripsOnlyTheCloneSuffix(string? given, string expected)
    {
        Assert.Equal(expected, CollectionOrderGate.PrefabNameOf(given));
    }
}
