using System;
using TheConcernedCat.Workers;
using TheConcernedCat.Workers.Traversal;

namespace Shared.Settlement.Tests;

/// <summary>The worker traversal seam and ladder navigation links (CF-LAD-005,
/// `docs/mods/concerned-foreman/LADDERS.md` decision L6).
///
/// Nothing here touches the game, and nothing here is about a player's input.
/// The two facts these tests exist to hold down are that <b>no worker climbs
/// anything by default</b> and that <b>no exit ever places a body onto a link
/// that has gone</b>.</summary>
public class TraversalSeamTests
{
    private static readonly WorkerKey Thorstein = WorkerKey.Thorstein;
    private static readonly WorkerKey Stranger = new WorkerKey("foreman", "stranger");

    private static TraversalLimits Limits() => TraversalLimits.Default.Validate();

    private static TraversalCapabilities Capable(bool npcClimbing = true)
    {
        var capabilities = new TraversalCapabilities(npcClimbing);
        capabilities.Grant(Thorstein, TraversalCapability.Ladders);
        return capabilities;
    }

    /// <summary>A ladder run standing at (x, z), its foot at <paramref name="footY"/>
    /// and its top step-off half a metre in from the edge, the way a real one is.
    /// </summary>
    private static TraversalLink Link(
        string id,
        float x = 0f,
        float z = 0f,
        float footY = 0f,
        float height = 4f,
        TraversalLimits? limits = null)
    {
        Assert.True(TraversalLink.TryCreate(
            id,
            TraversalLinkKind.Ladder,
            new WorkPoint(x, footY, z),
            new WorkPoint(x + 0.6f, footY + height, z),
            TraversalCapability.Ladders,
            limits ?? Limits(),
            out TraversalLink link));
        return link;
    }

    private static TraversalActor Worker(
        WorkerKey key,
        TraversalCapability claim = TraversalCapability.Ladders) => TraversalActor.OfWorker(key, claim);

    // ------------------------------------------------------------- capability

    [Fact]
    public void NoWorkerClimbsAnythingWhileNpcClimbingIsOff()
    {
        // The default: the setting is false, and it is false for a worker that
        // was explicitly granted the capability. This is the test that has to
        // fail loudly if anyone ever makes the grant sufficient on its own.
        var capabilities = Capable(npcClimbing: false);

        Assert.False(capabilities.Can(Worker(Thorstein), TraversalCapability.Ladders));
        Assert.Equal(TraversalCapability.None, capabilities.Allowed(Worker(Thorstein)));
        Assert.Contains("Ladders/NpcClimbing", capabilities.Explain(Worker(Thorstein), TraversalCapability.Ladders));
    }

    [Fact]
    public void NpcClimbingIsOffUnlessSomebodyAsksForIt()
    {
        Assert.False(new TraversalCapabilities().NpcClimbingEnabled);
    }

    [Fact]
    public void AWorkerNobodyGrantedIsRefusedEvenWithTheSettingOn()
    {
        var capabilities = Capable();

        Assert.True(capabilities.Can(Worker(Thorstein), TraversalCapability.Ladders));
        Assert.False(capabilities.Can(Worker(Stranger), TraversalCapability.Ladders));
        Assert.Contains("stranger", capabilities.Explain(Worker(Stranger), TraversalCapability.Ladders));
    }

    [Fact]
    public void TheNpcSwitchIsAboutNpcsAndNeverAboutThePlayer()
    {
        var capabilities = Capable(npcClimbing: false);

        Assert.True(capabilities.Can(TraversalActor.LocalPlayer(), TraversalCapability.Ladders));
    }

    [Fact]
    public void AGrantAddsAndARevokeTakesAway()
    {
        var capabilities = new TraversalCapabilities(npcClimbingEnabled: true);
        Assert.Equal(0, capabilities.GrantCount);

        capabilities.Grant(Thorstein, TraversalCapability.Ladders);
        capabilities.Grant(Thorstein, TraversalCapability.Ladders);
        Assert.Equal(1, capabilities.GrantCount);
        Assert.True(capabilities.Can(Worker(Thorstein), TraversalCapability.Ladders));

        capabilities.Revoke(Thorstein, TraversalCapability.Ladders);
        Assert.Equal(0, capabilities.GrantCount);
        Assert.False(capabilities.Can(Worker(Thorstein), TraversalCapability.Ladders));

        // Revoking what was never granted is not an error: a runtime shutting
        // down must not have to remember what it handed out.
        capabilities.Revoke(Stranger, TraversalCapability.Ladders);
        Assert.Throws<ArgumentException>(() => capabilities.Grant(default, TraversalCapability.Ladders));
    }

    [Fact]
    public void TheGrantIsACeilingAndNotAFloor()
    {
        // A runtime that has temporarily dropped a capability - both hands full -
        // does not have it handed back by the table.
        var capabilities = Capable();

        Assert.Equal(
            TraversalCapability.None,
            capabilities.Allowed(Worker(Thorstein, TraversalCapability.None)));
    }

    [Fact]
    public void NothingCanBeAskedOfAnEmptyCapability()
    {
        Assert.False(Capable().Can(TraversalActor.LocalPlayer(), TraversalCapability.None));
        Assert.Throws<ArgumentException>(() => TraversalActor.OfWorker(default));
    }

    // ------------------------------------------------------------------ links

    [Fact]
    public void ALinkNeedsAnIdAKindACapabilityAndRealHeight()
    {
        TraversalLimits limits = Limits();
        WorkPoint foot = new WorkPoint(0f, 0f, 0f);
        WorkPoint head = new WorkPoint(0f, 4f, 0f);

        Assert.False(TraversalLink.TryCreate(
            null, TraversalLinkKind.Ladder, foot, head, TraversalCapability.Ladders, limits, out _));
        Assert.False(TraversalLink.TryCreate(
            "a", TraversalLinkKind.Unspecified, foot, head, TraversalCapability.Ladders, limits, out _));
        Assert.False(TraversalLink.TryCreate(
            "a", TraversalLinkKind.Ladder, foot, head, TraversalCapability.None, limits, out _));
        Assert.False(TraversalLink.TryCreate(
            "a", TraversalLinkKind.Ladder, new WorkPoint(float.NaN, 0f, 0f), head,
            TraversalCapability.Ladders, limits, out _));

        // A step is not a link: below the shortest climbable height a body walks
        // up it, and so does a worker.
        Assert.False(TraversalLink.TryCreate(
            "a", TraversalLinkKind.Ladder, foot, new WorkPoint(0f, 0.5f, 0f),
            TraversalCapability.Ladders, limits, out _));

        // And neither is a hole: a "link" that goes down from its bottom end is
        // a measurement that went wrong.
        Assert.False(TraversalLink.TryCreate(
            "a", TraversalLinkKind.Ladder, head, foot, TraversalCapability.Ladders, limits, out _));

        Assert.True(default(TraversalLink).IsEmpty);
        Assert.Equal("<no link>", default(TraversalLink).ToString());
    }

    [Fact]
    public void APathThatEndsAtOneEndOfALinkContinuesFromTheOther()
    {
        TraversalLink link = Link("l");

        Assert.Equal(link.Bottom, link.EntryFor(TraversalDirection.Up));
        Assert.Equal(link.Top, link.ExitFor(TraversalDirection.Up));
        Assert.Equal(link.Top, link.EntryFor(TraversalDirection.Down));
        Assert.Equal(link.Bottom, link.ExitFor(TraversalDirection.Down));
        Assert.Equal(4f, link.Height, 3);
    }

    [Fact]
    public void StandingAtAnEndDecidesTheDirectionAndStandingNowhereDecidesNothing()
    {
        TraversalLimits limits = Limits();
        TraversalLink link = Link("l", limits: limits);

        Assert.Equal(TraversalDirection.Up, link.DirectionFrom(link.Bottom, limits));
        Assert.Equal(TraversalDirection.Down, link.DirectionFrom(link.Top, limits));

        // Directly under the top endpoint but on the ground: that is the bottom,
        // because height is part of being at an end.
        Assert.Equal(
            TraversalDirection.Up,
            link.DirectionFrom(new WorkPoint(link.Top.X, link.Bottom.Y, link.Top.Z), limits));

        Assert.Equal(
            TraversalDirection.Unspecified,
            link.DirectionFrom(new WorkPoint(20f, 0f, 0f), limits));
        Assert.Equal(
            TraversalDirection.Unspecified,
            link.DirectionFrom(new WorkPoint(float.NaN, 0f, 0f), limits));
    }

    [Fact]
    public void ALinkCostsMoreThanItsHeightBecauseGettingOnAndOffIsNotFree()
    {
        TraversalLimits limits = Limits();
        TraversalLink link = Link("l", height: 4f, limits: limits);

        Assert.Equal(4f / limits.TraversalSpeedMetresPerSecond, link.SecondsToTraverse(limits), 3);
        Assert.True(link.EquivalentWalkMetres(limits) > link.Height);
        Assert.Equal(
            limits.LinkOverheadMetres + (4f * limits.CostPerMetre),
            link.EquivalentWalkMetres(limits),
            3);

        // Half way up is half way between the two step-off points, and no
        // further: the estimate is clamped, because a fraction that came from a
        // divide is not to be trusted.
        WorkPoint middle = link.EstimatedPositionAt(TraversalDirection.Up, 0.5f);
        Assert.Equal(2f, middle.Y, 3);
        Assert.Equal(link.Top, link.EstimatedPositionAt(TraversalDirection.Up, 9f));
        Assert.Equal(link.Bottom, link.EstimatedPositionAt(TraversalDirection.Up, float.NaN));
    }

    [Fact]
    public void ATraversalLimitOutsideItsDesignedRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TraversalLimits { MaxWorkerHeightMetres = 0.1f }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TraversalLimits { TraversalSpeedMetresPerSecond = 0f }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TraversalLimits { ApproachRadiusMetres = 0f }.Validate());
    }

    // ---------------------------------------------------------------- network

    [Fact]
    public void PublishingTwiceUnderOneIdIsANewLinkAndANewGeneration()
    {
        var network = new TraversalLinkNetwork();
        int first = network.Publish(Link("l"));
        int second = network.Publish(Link("l", height: 6f));

        Assert.True(first > 0);
        Assert.True(second > first);
        Assert.Equal(1, network.Count);
        Assert.True(network.TryGet("l", out TraversalLink link, out int generation));
        Assert.Equal(second, generation);
        Assert.Equal(6f, link.Height, 3);

        Assert.Equal(0, network.Publish(default));
    }

    [Fact]
    public void RetiringSomethingThatIsNotThereIsNotAnError()
    {
        var network = new TraversalLinkNetwork();
        network.Publish(Link("l"));

        Assert.True(network.Retire("l"));
        Assert.False(network.Retire("l"));
        Assert.False(network.Retire(null));
        Assert.False(network.TryGet("l", out _, out _));

        network.Publish(Link("a"));
        network.Publish(Link("b", x: 10f));
        network.Clear();
        Assert.Equal(0, network.Count);
    }

    [Fact]
    public void ADestinationOnTheSameLevelIsAWalkAndNoLinkIsInvolved()
    {
        var network = new TraversalLinkNetwork();
        network.Publish(Link("l"));

        TraversalRoute route = network.PlanRoute(
            Worker(Thorstein), Capable(), new WorkPoint(0f, 0f, 0f), new WorkPoint(20f, 0.2f, 0f), Limits());

        Assert.Equal(TraversalRouteKind.Walk, route.Kind);
        Assert.Equal(20f, route.EstimatedMetres, 3);
    }

    [Fact]
    public void AHigherDestinationRoutesThroughTheLinkAndContinuesFromItsFarEnd()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l", x: 5f, limits: limits);
        network.Publish(link);

        TraversalRoute route = network.PlanRoute(
            Worker(Thorstein), Capable(), new WorkPoint(0f, 0f, 0f), new WorkPoint(9f, 4f, 0f), limits);

        Assert.Equal(TraversalRouteKind.ThroughLink, route.Kind);
        Assert.Equal(TraversalDirection.Up, route.Direction);
        Assert.Equal(link.Bottom, route.EntryPoint);
        Assert.Equal(link.Top, route.ExitPoint);
        Assert.Equal("l", route.Link.Id);
        Assert.True(route.EstimatedMetres > 0f);
    }

    [Fact]
    public void ADestinationBelowRoutesDownTheSameLink()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l", x: 5f, limits: limits);
        network.Publish(link);

        TraversalRoute route = network.PlanRoute(
            Worker(Thorstein), Capable(), new WorkPoint(6f, 4f, 0f), new WorkPoint(0f, 0f, 0f), limits);

        Assert.Equal(TraversalRouteKind.ThroughLink, route.Kind);
        Assert.Equal(TraversalDirection.Down, route.Direction);
        Assert.Equal(link.Top, route.EntryPoint);
        Assert.Equal(link.Bottom, route.ExitPoint);
    }

    [Fact]
    public void TheCheapestLinkWinsAndATieIsBrokenTheSameWayEveryRun()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        network.Publish(Link("far", x: 30f, limits: limits));
        network.Publish(Link("near", x: 2f, limits: limits));

        TraversalRoute route = network.PlanRoute(
            Worker(Thorstein), Capable(), new WorkPoint(0f, 0f, 0f), new WorkPoint(2f, 4f, 0f), limits);
        Assert.Equal("near", route.Link.Id);

        // Two links at the same cost: the answer is the ordinal-lower id, so it
        // does not depend on the order the world happened to load pieces in.
        var mirrored = new TraversalLinkNetwork();
        mirrored.Publish(Link("b", z: 5f, limits: limits));
        mirrored.Publish(Link("a", z: -5f, limits: limits));
        TraversalRoute tie = mirrored.PlanRoute(
            Worker(Thorstein), Capable(), new WorkPoint(0f, 0f, 0f), new WorkPoint(0.6f, 4f, 0f), limits);
        Assert.Equal("a", tie.Link.Id);
    }

    [Fact]
    public void AWorkerThatMayNotClimbGetsNoRouteAtAll()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        network.Publish(Link("l", limits: limits));

        var from = new WorkPoint(0f, 0f, 0f);
        var to = new WorkPoint(0.6f, 4f, 0f);

        // The setting is off.
        Assert.Equal(
            TraversalRouteKind.NoRoute,
            network.PlanRoute(Worker(Thorstein), Capable(npcClimbing: false), from, to, limits).Kind);

        // The worker was never granted it.
        Assert.Equal(
            TraversalRouteKind.NoRoute,
            network.PlanRoute(Worker(Stranger), Capable(), from, to, limits).Kind);

        // And the player, who is subject to neither, gets the link.
        Assert.Equal(
            TraversalRouteKind.ThroughLink,
            network.PlanRoute(TraversalActor.LocalPlayer(), Capable(npcClimbing: false), from, to, limits).Kind);
    }

    [Fact]
    public void ALinkTallerThanAWorkerMayCommitToIsNotInItsRoute()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        float tooTall = limits.MaxWorkerHeightMetres + 1f;
        network.Publish(Link("tall", height: tooTall, limits: limits));

        var from = new WorkPoint(0f, 0f, 0f);
        var to = new WorkPoint(0.6f, tooTall, 0f);

        Assert.Equal(
            TraversalRouteKind.NoRoute,
            network.PlanRoute(Worker(Thorstein), Capable(), from, to, limits).Kind);

        // A person climbing their own tower is their own business.
        Assert.Equal(
            TraversalRouteKind.ThroughLink,
            network.PlanRoute(TraversalActor.LocalPlayer(), Capable(), from, to, limits).Kind);
    }

    [Fact]
    public void ALinkBeyondThePlanningHorizonIsNotWorthWalkingTo()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        network.Publish(Link("miles", x: limits.MaxSearchMetres + 10f, limits: limits));

        Assert.Equal(
            TraversalRouteKind.NoRoute,
            network.PlanRoute(
                Worker(Thorstein),
                Capable(),
                new WorkPoint(0f, 0f, 0f),
                new WorkPoint(limits.MaxSearchMetres + 10.6f, 4f, 0f),
                limits).Kind);
    }

    [Fact]
    public void ALinkThatWouldOnlyBeHalfTheJourneyIsNotUsedAsIfItWereAllOfIt()
    {
        // The ladder reaches 4 m and the destination is at 9 m. Two ladders and
        // a walk between them would do it; chaining links is the filed follow-up,
        // and guessing is not.
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        network.Publish(Link("lower", limits: limits));

        Assert.Equal(
            TraversalRouteKind.NoRoute,
            network.PlanRoute(
                Worker(Thorstein), Capable(), new WorkPoint(0f, 0f, 0f), new WorkPoint(0.6f, 9f, 0f), limits).Kind);
    }

    [Fact]
    public void ANonsensePositionGetsNoRouteRatherThanAGuess()
    {
        var network = new TraversalLinkNetwork();
        network.Publish(Link("l"));

        Assert.Equal(
            TraversalRouteKind.NoRoute,
            network.PlanRoute(
                Worker(Thorstein),
                Capable(),
                new WorkPoint(float.NaN, 0f, 0f),
                new WorkPoint(0f, 4f, 0f),
                Limits()).Kind);
    }

    // ------------------------------------------------------------------- seam

    private static TraversalRequest AtFootOf(
        TraversalLink link,
        WorkerKey? worker = null,
        bool featureEnabled = true,
        WorkAuthorityVerdict authority = WorkAuthorityVerdict.Granted,
        bool actorReady = true,
        bool alreadyTraversing = false,
        TraversalDirection direction = TraversalDirection.Unspecified) =>
        new TraversalRequest(
            worker.HasValue ? Worker(worker.Value) : TraversalActor.LocalPlayer(),
            link.Id,
            link.Bottom,
            featureEnabled,
            authority,
            actorReady,
            alreadyTraversing,
            direction);

    [Fact]
    public void EveryRefusalHasItsOwnReason()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());

        Assert.Equal(
            TraversalRefusal.Disabled,
            traversal.CanTraverse(AtFootOf(link, Thorstein, featureEnabled: false)).Refusal);
        Assert.Equal(
            TraversalRefusal.AlreadyTraversing,
            traversal.CanTraverse(AtFootOf(link, Thorstein, alreadyTraversing: true)).Refusal);
        Assert.Equal(
            TraversalRefusal.ActorBusy,
            traversal.CanTraverse(AtFootOf(link, Thorstein, actorReady: false)).Refusal);

        Assert.Throws<ArgumentOutOfRangeException>(() => TraversalVerdict.Refuse(TraversalRefusal.Unspecified));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TraversalVerdict.Allow(link, TraversalDirection.Unspecified));
    }

    [Fact]
    public void AWorkerWithoutAuthorityDoesNotClimbHoweverCapableItIs()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());

        foreach (WorkAuthorityVerdict verdict in new[]
                 {
                     WorkAuthorityVerdict.Unspecified,
                     WorkAuthorityVerdict.RuntimeDisabled,
                     WorkAuthorityVerdict.NotHost,
                     WorkAuthorityVerdict.DedicatedServer,
                     WorkAuthorityVerdict.OtherPeersConnected,
                 })
        {
            TraversalVerdict answer = traversal.CanTraverse(AtFootOf(link, Thorstein, authority: verdict));
            Assert.False(answer.Allowed);
            Assert.Equal(TraversalRefusal.NoAuthority, answer.Refusal);
        }

        // The player is not subject to it: gating a person's own ladder on being
        // the host would break climbing for every client.
        Assert.True(
            traversal.CanTraverse(AtFootOf(link, authority: WorkAuthorityVerdict.NotHost)).Allowed);
    }

    [Fact]
    public void AWorkerThatMayNotClimbIsRefusedAtTheSeamToo()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);

        var offByDefault = new LinkTraversal(network, Capable(npcClimbing: false), Limits());
        Assert.Equal(
            TraversalRefusal.NotCapable,
            offByDefault.CanTraverse(AtFootOf(link, Thorstein)).Refusal);

        var granted = new LinkTraversal(network, Capable(), Limits());
        Assert.Equal(
            TraversalRefusal.NotCapable,
            granted.CanTraverse(AtFootOf(link, Stranger)).Refusal);
        Assert.True(granted.CanTraverse(AtFootOf(link, Thorstein)).Allowed);
    }

    [Fact]
    public void ALinkWhoseLadderIsGoneRefusesEntryRatherThanStartingOne()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());

        Assert.True(traversal.CanTraverse(AtFootOf(link, Thorstein)).Allowed);

        network.Retire(link.Id);

        TraversalVerdict verdict = traversal.CanTraverse(AtFootOf(link, Thorstein));
        Assert.False(verdict.Allowed);
        Assert.Equal(TraversalRefusal.LinkGone, verdict.Refusal);

        TraversalEntry entry = traversal.Enter(AtFootOf(link, Thorstein));
        Assert.False(entry.Started);
        Assert.Equal(TraversalRefusal.LinkGone, entry.Verdict.Refusal);
        Assert.False(entry.Handle.IsValid);
        Assert.Equal(0, traversal.ActiveCount);
    }

    [Fact]
    public void ATooTallLinkIsRefusedForAWorkerAndAllowedForThePlayer()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("tall", height: limits.MaxWorkerHeightMetres + 5f, limits: limits);
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), limits);

        Assert.Equal(TraversalRefusal.TooTall, traversal.CanTraverse(AtFootOf(link, Thorstein)).Refusal);
        Assert.True(traversal.CanTraverse(AtFootOf(link)).Allowed);
    }

    [Fact]
    public void StandingNowhereNearItAndAskingToGoTheWrongWayAreBothRefused()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());

        var faraway = new TraversalRequest(
            Worker(Thorstein),
            link.Id,
            new WorkPoint(40f, 0f, 0f),
            featureEnabled: true,
            WorkAuthorityVerdict.Granted,
            actorReady: true,
            alreadyTraversing: false);
        Assert.Equal(TraversalRefusal.NotAtAnEndpoint, traversal.CanTraverse(faraway).Refusal);

        // Standing at the foot and asking to go down is a caller bug, and
        // answering it would drag a body through the ladder.
        Assert.Equal(
            TraversalRefusal.NotAtAnEndpoint,
            traversal.CanTraverse(AtFootOf(link, Thorstein, direction: TraversalDirection.Down)).Refusal);

        Assert.True(
            traversal.CanTraverse(AtFootOf(link, Thorstein, direction: TraversalDirection.Up)).Allowed);
    }

    [Fact]
    public void AClimbAdvancesHoldsReversesAndArrives()
    {
        TraversalLimits limits = Limits();
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l", height: 4f, limits: limits);
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), limits);

        TraversalEntry entry = traversal.Enter(AtFootOf(link, Thorstein));
        Assert.True(entry.Started);
        Assert.Equal(TraversalDirection.Up, entry.Verdict.Direction);
        Assert.Equal(link.Top, entry.Verdict.Destination);
        Assert.Equal(1, traversal.ActiveCount);

        TraversalHandle handle = entry.Handle;

        TraversalProgress climbing = traversal.Traverse(handle, TraversalIntent.Advance, 1f);
        Assert.Equal(TraversalPhase.Advancing, climbing.Phase);
        Assert.Equal(limits.TraversalSpeedMetresPerSecond, climbing.MetresFromEntry, 3);
        Assert.False(climbing.MustExit);

        TraversalProgress resting = traversal.Traverse(handle, TraversalIntent.Hold, 1f);
        Assert.Equal(TraversalPhase.Holding, resting.Phase);
        Assert.Equal(climbing.MetresFromEntry, resting.MetresFromEntry, 3);

        TraversalProgress backDown = traversal.Traverse(handle, TraversalIntent.Reverse, 0.5f);
        Assert.Equal(TraversalPhase.Reversing, backDown.Phase);
        Assert.True(backDown.MetresFromEntry < climbing.MetresFromEntry);

        // A step with no time in it moves nothing, whatever the intent.
        TraversalProgress still = traversal.Traverse(handle, TraversalIntent.Advance, 0f);
        Assert.Equal(TraversalPhase.Holding, still.Phase);
        Assert.Equal(backDown.MetresFromEntry, still.MetresFromEntry, 3);
        Assert.Equal(
            backDown.MetresFromEntry,
            traversal.Traverse(handle, TraversalIntent.Advance, float.NaN).MetresFromEntry,
            3);

        // However long the frame, the climb stops at the top rather than
        // carrying anyone past it.
        TraversalProgress arrived = traversal.Traverse(handle, TraversalIntent.Advance, 100f);
        Assert.Equal(TraversalPhase.ReachedFarEnd, arrived.Phase);
        Assert.True(arrived.MustExit);
        Assert.Equal(1f, arrived.Fraction, 3);
        Assert.Equal(link.Top, arrived.EstimatedPosition);

        TraversalRelease release = traversal.Exit(handle, TraversalExitKind.Arrived);
        Assert.Equal(TraversalExitKind.Arrived, release.Kind);
        Assert.True(release.PlaceActor);
        Assert.Equal(link.Top, release.Landing);
        Assert.True(release.Restoration.IsComplete);
        Assert.Equal(0, traversal.ActiveCount);
    }

    [Fact]
    public void TurningBackStepsOffWhereItStarted()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());
        TraversalHandle handle = traversal.Enter(AtFootOf(link, Thorstein)).Handle;

        traversal.Traverse(handle, TraversalIntent.Advance, 1f);
        TraversalProgress back = traversal.Traverse(handle, TraversalIntent.Reverse, 100f);
        Assert.Equal(TraversalPhase.ReturnedToStart, back.Phase);
        Assert.True(back.MustExit);

        TraversalRelease release = traversal.Exit(handle, TraversalExitKind.TurnedBack);
        Assert.True(release.PlaceActor);
        Assert.Equal(link.Bottom, release.Landing);
    }

    [Fact]
    public void ALadderThatGoesWhileSomebodyIsOnItNeverPlacesTheBody()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());
        TraversalHandle handle = traversal.Enter(AtFootOf(link, Thorstein)).Handle;
        traversal.Traverse(handle, TraversalIntent.Advance, 1f);

        network.Retire(link.Id);

        // Even asked for the arrival it was half way to, the exit refuses to
        // move a body onto something that is not there.
        TraversalRelease release = traversal.Exit(handle, TraversalExitKind.Arrived);
        Assert.Equal(TraversalExitKind.Interrupted, release.Kind);
        Assert.False(release.PlaceActor);
        Assert.True(release.Restoration.IsComplete);
        Assert.Equal(0, traversal.ActiveCount);
    }

    [Fact]
    public void ALadderThatGoesMidClimbLosesTheTraversalOnTheVeryNextStep()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());
        TraversalHandle handle = traversal.Enter(AtFootOf(link, Thorstein)).Handle;
        traversal.Traverse(handle, TraversalIntent.Advance, 1f);

        network.Retire(link.Id);

        TraversalProgress lost = traversal.Traverse(handle, TraversalIntent.Advance, 1f);
        Assert.Equal(TraversalPhase.Lost, lost.Phase);
        Assert.True(lost.MustExit);
        Assert.Equal(0, traversal.ActiveCount);

        Assert.False(traversal.Exit(handle, TraversalExitKind.Arrived).PlaceActor);
    }

    [Fact]
    public void ALadderRebuiltUnderTheSameIdIsNotTheOneSomebodyIsOn()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());
        TraversalHandle handle = traversal.Enter(AtFootOf(link, Thorstein)).Handle;

        network.Publish(Link("l", height: 7f));

        Assert.Equal(TraversalPhase.Lost, traversal.Traverse(handle, TraversalIntent.Advance, 1f).Phase);
    }

    [Fact]
    public void AHandleNobodyIsHoldingReleasesNothing()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());
        TraversalHandle handle = traversal.Enter(AtFootOf(link, Thorstein)).Handle;

        Assert.True(traversal.Exit(handle, TraversalExitKind.Arrived).PlaceActor);
        Assert.False(traversal.Exit(handle, TraversalExitKind.Arrived).PlaceActor);
        Assert.False(traversal.Exit(default, TraversalExitKind.Arrived).PlaceActor);

        Assert.Equal(TraversalPhase.Lost, traversal.Traverse(default, TraversalIntent.Advance, 1f).Phase);
    }

    [Fact]
    public void EveryExitHandsTheWholeBodyBack()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());

        foreach (TraversalExitKind kind in new[]
                 {
                     TraversalExitKind.Arrived,
                     TraversalExitKind.TurnedBack,
                     TraversalExitKind.LetGo,
                     TraversalExitKind.Interrupted,
                     TraversalExitKind.Unspecified,
                 })
        {
            TraversalHandle handle = traversal.Enter(AtFootOf(link, Thorstein)).Handle;
            Assert.True(traversal.Exit(handle, kind).Restoration.IsComplete);
            Assert.Equal(0, traversal.ActiveCount);
        }

        // A let-go never has a landing, and a step-off always does. Neither can
        // be constructed the other way round.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TraversalRelease.Step(TraversalExitKind.LetGo, default));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TraversalRelease.Release(TraversalExitKind.Arrived));
    }

    [Fact]
    public void ARuntimeThatStopsLetsEverybodyGo()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink one = Link("a");
        TraversalLink two = Link("b", x: 20f);
        network.Publish(one);
        network.Publish(two);
        var traversal = new LinkTraversal(network, Capable(), Limits());

        traversal.Enter(AtFootOf(one, Thorstein));
        traversal.Enter(AtFootOf(two, Stranger, authority: WorkAuthorityVerdict.Granted));

        // The stranger was refused, so only one body is on anything.
        Assert.Equal(1, traversal.ActiveCount);
        Assert.Equal(1, traversal.ReleaseEveryone());
        Assert.Equal(0, traversal.ActiveCount);
    }

    [Fact]
    public void TwoActorsOnOneLinkAreTwoTraversals()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var capabilities = Capable();
        capabilities.Grant(Stranger, TraversalCapability.Ladders);
        var traversal = new LinkTraversal(network, capabilities, Limits());

        TraversalHandle first = traversal.Enter(AtFootOf(link, Thorstein)).Handle;
        TraversalHandle second = traversal.Enter(AtFootOf(link, Stranger)).Handle;

        Assert.NotEqual(first, second);
        Assert.Equal(2, traversal.ActiveCount);

        traversal.Traverse(first, TraversalIntent.Advance, 1f);
        Assert.Equal(0f, traversal.Traverse(second, TraversalIntent.Hold, 1f).MetresFromEntry, 3);
    }

    [Fact]
    public void TheOrdinaryRequestsAreTheOnesTheRuntimeWillActuallyMake()
    {
        var network = new TraversalLinkNetwork();
        TraversalLink link = Link("l");
        network.Publish(link);
        var traversal = new LinkTraversal(network, Capable(), Limits());

        Assert.True(traversal.CanTraverse(TraversalRequest.ForPlayer(link.Id, link.Bottom)).Allowed);
        Assert.True(traversal.CanTraverse(TraversalRequest.ForWorker(Thorstein, link.Id, link.Bottom)).Allowed);

        // The defaults of the convenience constructors are the safe ones: a
        // player with the feature off, and a worker whose authority was never
        // established, are both refused.
        Assert.Equal(
            TraversalRefusal.Disabled,
            traversal.CanTraverse(TraversalRequest.ForPlayer(link.Id, link.Bottom, featureEnabled: false)).Refusal);
        Assert.Equal(
            TraversalRefusal.NoAuthority,
            traversal.CanTraverse(TraversalRequest.ForWorker(
                Thorstein,
                link.Id,
                link.Bottom,
                authority: WorkAuthorityVerdict.Unspecified)).Refusal);
    }

    [Fact]
    public void ASeamNeedsItsPartsAndSaysSoRatherThanGuessing()
    {
        Assert.Throws<ArgumentNullException>(() => new LinkTraversal(null!, Capable()));
        Assert.Throws<ArgumentNullException>(() => new LinkTraversal(new TraversalLinkNetwork(), null!));
        Assert.Throws<ArgumentNullException>(
            () => new TraversalLinkNetwork().PlanRoute(
                Worker(Thorstein), null!, default, new WorkPoint(0f, 4f, 0f), Limits()));
        Assert.Throws<ArgumentNullException>(
            () => new TraversalLinkNetwork().PlanRoute(
                Worker(Thorstein), Capable(), default, new WorkPoint(0f, 4f, 0f), null!));
    }
}
