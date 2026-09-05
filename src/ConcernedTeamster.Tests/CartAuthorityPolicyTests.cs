using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.ConcernedTeamster.Domain.Brake;

namespace ConcernedTeamster.Tests;

/// <summary>CT-026: the multiplayer authority policy must cover every shipped
/// feature, permit mutation only under live local authority, fail closed on
/// ambiguity, and be the single source of truth the brake enforces through.</summary>
public class CartAuthorityPolicyTests
{
    private static readonly CartAuthority[] AllAuthorities =
    {
        CartAuthority.Unknown, CartAuthority.Local, CartAuthority.Remote,
    };

    // -- completeness: every enum value is governed --

    [Fact]
    public void Policy_CoversEveryTeamsterFeature()
    {
        var governed = new HashSet<TeamsterFeature>(CartAuthorityPolicy.AllFeatures);
        foreach (TeamsterFeature feature in Enum.GetValues<TeamsterFeature>())
        {
            Assert.Contains(feature, governed);
        }

        Assert.Equal(Enum.GetValues<TeamsterFeature>().Length, governed.Count);
    }

    // -- exactly one mutation feature --

    [Fact]
    public void ParkingBrake_IsTheOnlyMutationFeature()
    {
        foreach (TeamsterFeature feature in CartAuthorityPolicy.AllFeatures)
        {
            bool expectMutation = feature == TeamsterFeature.ParkingBrake;
            Assert.Equal(expectMutation, CartAuthorityPolicy.IsMutation(feature));
            Assert.Equal(
                expectMutation ? FeatureClass.Mutation : FeatureClass.Observation,
                CartAuthorityPolicy.ClassOf(feature));
        }
    }

    // -- mutation truth table: only ParkingBrake + Local --

    [Fact]
    public void MayMutate_TrueOnlyForBrakeUnderLocalAuthority()
    {
        foreach (TeamsterFeature feature in CartAuthorityPolicy.AllFeatures)
        {
            foreach (CartAuthority authority in AllAuthorities)
            {
                bool expected = feature == TeamsterFeature.ParkingBrake &&
                    authority == CartAuthority.Local;
                Assert.Equal(expected, CartAuthorityPolicy.MayMutate(feature, authority));
            }
        }
    }

    [Fact]
    public void MayMutate_BrakeDeniedUnderRemoteAndUnknown()
    {
        Assert.True(CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, CartAuthority.Local));
        Assert.False(CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, CartAuthority.Remote));
        Assert.False(CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, CartAuthority.Unknown));
    }

    [Fact]
    public void NoObservationFeature_EverMutates()
    {
        foreach (TeamsterFeature feature in CartAuthorityPolicy.AllFeatures)
        {
            if (CartAuthorityPolicy.ClassOf(feature) != FeatureClass.Observation)
            {
                continue;
            }

            foreach (CartAuthority authority in AllAuthorities)
            {
                Assert.False(CartAuthorityPolicy.MayMutate(feature, authority));
            }
        }
    }

    // -- observation labeling --

    [Fact]
    public void RequiresRemoteLabel_OnlyForOwnerFreshObservationsAwayFromLocal()
    {
        // A locally-owned cart is always fresh — never labeled remote.
        foreach (TeamsterFeature feature in CartAuthorityPolicy.AllFeatures)
        {
            Assert.False(CartAuthorityPolicy.RequiresRemoteLabel(feature, CartAuthority.Local));
        }

        // Owner-fresh observations are labeled when observed remotely; the
        // brake (mutation) is never observation-labeled.
        Assert.True(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.CartStatusPanel, CartAuthority.Remote));
        Assert.True(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.CartTelemetry, CartAuthority.Unknown));
        Assert.False(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.ParkingBrake, CartAuthority.Remote));
        // Trips/route profiling are local-history/geometry reads, not
        // owner-fresh cart state, so they are not remote-labeled.
        Assert.False(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.TripRecording, CartAuthority.Remote));
        Assert.False(CartAuthorityPolicy.RequiresRemoteLabel(TeamsterFeature.RouteProfiling, CartAuthority.Remote));
    }

    // -- resolver fail-closed --

    [Theory]
    [InlineData(true, true, true, CartAuthority.Local)]
    [InlineData(true, true, false, CartAuthority.Remote)]
    [InlineData(true, false, true, CartAuthority.Unknown)]   // invalid view → unknown even if "owner"
    [InlineData(false, true, true, CartAuthority.Unknown)]   // capability off → unknown
    [InlineData(false, false, false, CartAuthority.Unknown)]
    public void Resolver_MapsOwnershipFactsFailClosed(
        bool capabilityOk, bool viewValid, bool isOwner, CartAuthority expected)
    {
        Assert.Equal(expected, CartAuthorityResolver.Resolve(capabilityOk, viewValid, isOwner));
    }

    [Fact]
    public void Unknown_IsTheDefaultAuthority()
    {
        Assert.Equal(CartAuthority.Unknown, default(CartAuthority));
    }

    // -- brake enforces THROUGH the policy (single source of truth) --

    [Fact]
    public void Brake_EngageRequiresExactlyThePolicysMutationAuthority()
    {
        var lifecycle = new BrakeLifecycle();
        // All engage-eligibility facts satisfied EXCEPT authority varies.
        BrakeFacts localAuthority = new(
            capabilityOk: true, inWorld: true, cartExists: true,
            isLocalAuthority: true, isAttached: false, distanceMeters: 1f);
        BrakeFacts notAuthority = new(
            capabilityOk: true, inWorld: true, cartExists: true,
            isLocalAuthority: false, isAttached: false, distanceMeters: 1f);

        // The facts' authority maps to the policy's mutation right exactly.
        Assert.Equal(
            CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, localAuthority.Authority),
            lifecycle.EvaluateToggle("cart-1", localAuthority, out _) == BrakeAction.Engage);
        Assert.False(CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, notAuthority.Authority));
        Assert.Equal(
            BrakeAction.None, lifecycle.EvaluateToggle("cart-1", notAuthority, out string reason));
        Assert.Equal("this client does not control the cart", reason);
    }

    [Fact]
    public void Brake_EngagedThenAuthorityLost_ReleasesViaPolicy()
    {
        var lifecycle = new BrakeLifecycle();
        BrakeFacts eligible = new(
            capabilityOk: true, inWorld: true, cartExists: true,
            isLocalAuthority: true, isAttached: false, distanceMeters: 1f);
        Assert.Equal(BrakeAction.Engage, lifecycle.EvaluateToggle("cart-1", eligible, out _));
        lifecycle.MarkEngaged("cart-1");

        BrakeFacts authorityLost = new(
            capabilityOk: true, inWorld: true, cartExists: true,
            isLocalAuthority: false, isAttached: false, distanceMeters: 1f);
        Assert.False(CartAuthorityPolicy.MayMutate(TeamsterFeature.ParkingBrake, authorityLost.Authority));
        Assert.Equal(BrakeAction.Release, lifecycle.EvaluateTick(authorityLost, out string reason));
        Assert.Equal("cart authority moved to another client", reason);
    }
}
