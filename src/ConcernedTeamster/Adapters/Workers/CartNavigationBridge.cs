using System;
using TheConcernedCat.ConcernedTeamster.Adapters.Navigation;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The executor's narrow navigation port over agent B's
/// <see cref="CartNavigationKit"/> (#313/#314 integration). It resolves the
/// leased cart and Gunnar's bound body for the kit, and maps B's steering
/// answers onto the executor's own; the planning, steering, refresh and failure
/// memory are all B's.</summary>
internal sealed class CartNavigationBridge : IHaulNavigation
{
    private readonly CartNavigationKit _kit;
    private readonly VagonHitchSeam _seam;
    private readonly TeamsterWorkerBody _body;
    private bool _measured;

    public CartNavigationBridge(CartNavigationKit kit, VagonHitchSeam seam, TeamsterWorkerBody body)
    {
        _kit = kit ?? throw new ArgumentNullException(nameof(kit));
        _seam = seam ?? throw new ArgumentNullException(nameof(seam));
        _body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public CartNavigationKit Kit => _kit;

    public CartFootprint? Footprint => _measured ? _kit.Footprint : (CartFootprint?)null;

    public bool UseCart(CartKey cart)
    {
        try
        {
            Vagon? vagon = _seam.Resolve(cart);
            _measured = vagon != null && _kit.UseCart(vagon, _body.Bound);
        }
        catch
        {
            _measured = false;
        }

        return _measured;
    }

    public void ReleaseCart()
    {
        _measured = false;
        try
        {
            _kit.ReleaseCart();
        }
        catch
        {
            // Forgetting cannot fail in a way that matters: the next lease
            // measures again.
        }
    }

    public CartRoutePlan Plan(CartRouteRequest request, WorkPoint? pullerPosition, float now) =>
        _kit.Planner.Plan(request, pullerPosition, now);

    public HaulSteering Steer(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition)
    {
        CartSteering steering = _kit.Planner.Follow(plan, pullerPosition, cartPosition);
        return new HaulSteering(Map(steering.Status), steering.Goal);
    }

    public bool NeedsRefresh(CartRoutePlan plan, float now) => _kit.Planner.NeedsRefresh(plan, now);

    public CartRoutePlan Refresh(CartRoutePlan plan, WorkPoint pullerPosition, WorkPoint cartPosition, float now) =>
        _kit.Planner.Refresh(plan, pullerPosition, cartPosition, now);

    public void RememberStall(WorkPoint legTarget, WorkPoint cartPosition, WorkPoint pullerPosition, float now) =>
        _kit.Planner.Failures?.Remember(legTarget, cartPosition, pullerPosition, HaulAttentionReason.Wedged, now);

    internal static HaulSteeringStatus Map(CartSteeringStatus status)
    {
        switch (status)
        {
            case CartSteeringStatus.Following:
                return HaulSteeringStatus.Following;
            case CartSteeringStatus.Finished:
                return HaulSteeringStatus.Finished;
            case CartSteeringStatus.LeftCorridor:
                return HaulSteeringStatus.LeftCorridor;
            default:
                return HaulSteeringStatus.NotSuitable;
        }
    }
}
