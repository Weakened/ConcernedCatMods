using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.ConcernedTeamster.Domain.Brake;

namespace ConcernedTeamster.Tests;

/// <summary>CT-027: drives the CT-026 authority policy and brake lifecycle
/// through the exact multi-step sequences the real topologies produce —
/// player-hosted and dedicated-server authority handoff mid-haul. This is
/// the automatable half of the scenario matrix: it proves the LOGIC that
/// runs on each client is correct regardless of topology (the topology only
/// changes which client reports local authority, which these scenarios vary
/// directly). The in-game observation of the same rows on real servers is
/// recorded as pending in TEST_PLAN.md and HUMAN_ATTENTION.md — never PASS.
///
/// Each scenario is written from ONE client's point of view: `facts` carry
/// that client's live authority, so "authority handoff" is modeled as the
/// client's IsLocalAuthority flipping between ticks, which is exactly what
/// the adapter reports when the game moves ZDO ownership.</summary>
public class MultiplayerScenarioTests
{
    private const string Cart = "10:2001";

    private static BrakeFacts Facts(bool localAuthority, bool attached = false, float distance = 1.5f)
    {
        return new BrakeFacts(
            capabilityOk: true, inWorld: true, cartExists: true,
            isLocalAuthority: localAuthority, isAttached: attached, distanceMeters: distance);
    }

    // -- Scenario: authority handoff mid-haul never leaves a stale brake --

    [Fact]
    public void Handoff_OwnerEngagedThenAuthorityLeaves_BrakeReleasesSameTick()
    {
        // Client A owns the parked cart and engages the brake.
        var brake = new BrakeLifecycle();
        Assert.Equal(BrakeAction.Engage, brake.EvaluateToggle(Cart, Facts(localAuthority: true), out _));
        brake.MarkEngaged(Cart);
        Assert.True(brake.IsEngaged);

        // A teammate takes the cart: ownership moves away from A. On A's next
        // tick IsLocalAuthority is false, and the brake must release — no
        // stale hold persists past the client's authority.
        Assert.Equal(BrakeAction.Release, brake.EvaluateTick(Facts(localAuthority: false), out string reason));
        brake.MarkReleased();

        Assert.False(brake.IsEngaged);
        Assert.Equal("cart authority moved to another client", reason);
    }

    [Fact]
    public void Handoff_ReceivingClientCannotEngageWhileRemote()
    {
        // From client B's POV before the handoff completes: B does not yet
        // own the cart, so the engage is refused — a mutating action never
        // executes without authority. (At the brake layer BrakeFacts only
        // ever carries Local or Unknown; the Remote authority value itself
        // is exercised against the policy in NoMutationWithoutAuthority.)
        var brake = new BrakeLifecycle();
        Assert.Equal(BrakeAction.None, brake.EvaluateToggle(Cart, Facts(localAuthority: false), out string reason));
        Assert.Equal("this client does not control the cart", reason);
        Assert.False(brake.IsEngaged);

        // Once ownership actually arrives, B may engage.
        Assert.Equal(BrakeAction.Engage, brake.EvaluateToggle(Cart, Facts(localAuthority: true), out _));
    }

    [Fact]
    public void Handoff_OwnerKeepsBrakeAcrossContinuedOwningTicks()
    {
        // The "keeps it while owning" direction: once engaged, the brake
        // must SURVIVE ticks where this client still owns the cart — a
        // spurious release under continued ownership would be a defect this
        // scenario is here to catch (the flaps test only proves the safety
        // direction, that it never holds without authority).
        var brake = new BrakeLifecycle();
        Assert.Equal(BrakeAction.Engage, brake.EvaluateToggle(Cart, Facts(localAuthority: true), out _));
        brake.MarkEngaged(Cart);

        for (int tick = 0; tick < 5; tick++)
        {
            Assert.Equal(BrakeAction.None, brake.EvaluateTick(Facts(localAuthority: true), out _));
            Assert.True(brake.IsEngaged);
        }

        // And it still releases the moment ownership finally leaves.
        Assert.Equal(BrakeAction.Release, brake.EvaluateTick(Facts(localAuthority: false), out _));
    }

    [Fact]
    public void Handoff_RapidOwnershipFlaps_NeverEngagesWithoutAuthority()
    {
        // Contested cart: ownership flaps between ticks. The brake may hold
        // only across ticks where this client actually has authority, and
        // must never be engaged during a non-authority tick.
        var brake = new BrakeLifecycle();
        bool[] authoritySequence = { true, false, true, false, false, true };
        bool engaged = false;

        foreach (bool local in authoritySequence)
        {
            if (!engaged && local)
            {
                if (brake.EvaluateToggle(Cart, Facts(localAuthority: true), out _) == BrakeAction.Engage)
                {
                    brake.MarkEngaged(Cart);
                    engaged = true;
                }
            }
            else if (engaged)
            {
                if (brake.EvaluateTick(Facts(localAuthority: local), out _) == BrakeAction.Release)
                {
                    brake.MarkReleased();
                    engaged = false;
                }
            }

            // Invariant checked every tick: an engaged brake implies this
            // client currently holds authority.
            if (brake.IsEngaged)
            {
                Assert.True(local, "brake was engaged on a tick without local authority");
                Assert.True(CartAuthorityPolicy.MayMutate(
                    TeamsterFeature.ParkingBrake,
                    local ? CartAuthority.Local : CartAuthority.Unknown));
            }
        }
    }

    // -- Scenario: no mutating action executes without authority (any topology) --

    [Theory]
    [InlineData(CartAuthority.Remote)]
    [InlineData(CartAuthority.Unknown)]
    public void NoMutationWithoutAuthority_AcrossFeaturesAndAmbiguousStates(CartAuthority authority)
    {
        foreach (TeamsterFeature feature in CartAuthorityPolicy.AllFeatures)
        {
            Assert.False(CartAuthorityPolicy.MayMutate(feature, authority));
        }
    }

    // -- Scenario: observation labeling per topology --

    [Fact]
    public void Observer_RemoteCartOwnerFreshReadingsAreLabeled()
    {
        // A player observing a cart owned by a teammate: owner-fresh readouts
        // (mass, grade, pull state) are labeled remote so the observer never
        // reads a stale value as current truth.
        Assert.True(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.CartStatusPanel, CartAuthority.Remote));
        Assert.True(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.CartTelemetry, CartAuthority.Remote));

        // The same player after taking ownership: readings are now local and
        // fresh — no remote label.
        Assert.False(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.CartStatusPanel, CartAuthority.Local));
        Assert.False(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.CartTelemetry, CartAuthority.Local));
    }

    // -- Scenario: the brake decision is a pure function of facts --

    [Fact]
    public void BrakeDecision_IsPureFunctionOfFacts_TopologyNotAnInput()
    {
        // Why this underwrites the "dedicated == player-hosted" row: neither
        // the brake lifecycle nor the policy takes ANY topology input (no
        // server-role flag, no ZNet query) — the decision is a pure function
        // of BrakeFacts. So a dedicated-server client and a player-hosted
        // client that report the same authority necessarily decide the same.
        // This test pins that purity (same facts → same decision, no hidden
        // shared state); the architectural half — that Teamster ships no
        // server component and sends nothing — is enforced by the validator's
        // CT-026 no-network/no-ownership audit, which the matrix row also
        // cites. Determinism alone is not "topology equivalence"; the two
        // together are.
        BrakeFacts owned = Facts(localAuthority: true);
        Assert.Equal(
            new BrakeLifecycle().EvaluateToggle(Cart, owned, out _),
            new BrakeLifecycle().EvaluateToggle(Cart, owned, out _));

        // Same instance, repeated identical facts → identical answer (no
        // internal drift), then the authority-gated transition still fires.
        var brake = new BrakeLifecycle();
        Assert.Equal(BrakeAction.Engage, brake.EvaluateToggle(Cart, owned, out _));
        brake.MarkEngaged(Cart);
        Assert.Equal(BrakeAction.None, brake.EvaluateTick(owned, out _));
        Assert.Equal(BrakeAction.Release, brake.EvaluateTick(Facts(localAuthority: false), out _));
    }
}
