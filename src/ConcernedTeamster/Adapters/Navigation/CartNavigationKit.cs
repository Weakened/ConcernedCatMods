using System;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Risk;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Navigation;

/// <summary>Gunnar's cart navigation, assembled over the running game (#314):
/// what the hauling runtime injects in place of any placeholder planner. It owns
/// one planner (so one navmesh query budget across every haul), the motion
/// monitor, the recovery policy, the failure cache and the staging selector.
/// Main thread only. Nothing here moves a body; the executor does.</summary>
internal sealed class CartNavigationKit
{
    private CartNavigationKit(
        HaulLimits limits,
        GameCartTerrainProbe probe,
        NavmeshCartPathSource paths,
        CartRoutePlanner planner,
        HaulMotionMonitor motion,
        HaulRecoveryPolicy recovery,
        CartStagingSelector staging)
    {
        Limits = limits;
        Probe = probe;
        Paths = paths;
        Planner = planner;
        Motion = motion;
        Recovery = recovery;
        Staging = staging;
        Footprint = CartFootprintReader.VanillaCart;
    }

    public HaulLimits Limits { get; }

    public GameCartTerrainProbe Probe { get; }

    public NavmeshCartPathSource Paths { get; }

    /// <summary>The <see cref="ICartRoutePlanner"/>.</summary>
    public CartRoutePlanner Planner { get; }

    /// <summary>The <see cref="IHaulMotionMonitor"/>.</summary>
    public HaulMotionMonitor Motion { get; }

    public HaulRecoveryPolicy Recovery { get; }

    public CartStagingSelector Staging { get; }

    /// <summary>The footprint of the cart in use: the vanilla cart until one is
    /// read.</summary>
    public CartFootprint Footprint { get; private set; }

    /// <summary>Builds the kit. The calibration models are optional; when given,
    /// a route whose climb or descent they say this load fails is refused.
    /// </summary>
    public static CartNavigationKit Create(HaulLimits limits, LoadModel? climb = null, RiskModel? descent = null)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        limits.Validate();
        var probe = new GameCartTerrainProbe();
        var paths = new NavmeshCartPathSource();
        var planner = new CartRoutePlanner(
            limits, paths, probe, CartRouteFailureCache.For(CartFootprintReader.VanillaCart, limits), climb, descent);
        return new CartNavigationKit(
            limits, probe, paths, planner, new HaulMotionMonitor(limits), new HaulRecoveryPolicy(limits),
            new CartStagingSelector(planner, probe));
    }

    /// <summary>A cart was leased to Gunnar: measure it, keep it and Gunnar out
    /// of their own clearance probes, and match the failure cache to its size.
    /// False when the cart could not be measured; the previous footprint then
    /// stays, and the caller should refuse to plan with an unmeasured cart.
    /// </summary>
    public bool UseCart(Component? cart, Component? puller)
    {
        if (cart == null || !CartFootprintReader.TryRead(cart, out CartFootprint footprint, out float top))
        {
            return false;
        }

        Footprint = footprint;
        Probe.UseBodies(cart.transform, puller != null ? puller.transform : null, top);
        Planner.Failures = CartRouteFailureCache.For(footprint, Limits);
        Motion.Reset();
        Recovery.Reset();
        return true;
    }

    /// <summary>The lease ended: nothing is ignored any more and nothing learned
    /// about that cart's ways is kept.</summary>
    public void ReleaseCart()
    {
        Probe.UseBodies(null, null, CartFootprintReader.VanillaCartTopMetres);
        Footprint = CartFootprintReader.VanillaCart;
        Planner.Failures = CartRouteFailureCache.For(Footprint, Limits);
        Motion.Reset();
        Recovery.Reset();
    }
}
