namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>One sentence a player can act on for every refusal agent A owns
/// (CONTRACTS.md §2.3): why a cart was not assigned, and why Gunnar did not
/// hitch. Attention reasons are presented by agent E; these are the refusals
/// the mechanics give at the moment of asking.
///
/// Unspecified has no sentence on purpose: seeing it is a bug, and it says so.
/// </summary>
internal static class HaulRefusalSentences
{
    public const string BugSentence = "Something went wrong that should not be possible; the log has the details.";

    public static string Describe(CartAssignmentRefusal refusal)
    {
        switch (refusal)
        {
            case CartAssignmentRefusal.NoAuthority:
                return "Gunnar only takes carts in single player, or as the host while nobody else is connected, with hauling turned on.";
            case CartAssignmentRefusal.WorkerUnavailable:
                return "Gunnar is not available to haul right now.";
            case CartAssignmentRefusal.NotACart:
                return "That is not a hand cart, so Gunnar cannot pull it.";
            case CartAssignmentRefusal.AmbiguousSelection:
                return "Point at one cart and select it first, then confirm within a few seconds.";
            case CartAssignmentRefusal.Destroyed:
                return "That cart no longer exists.";
            case CartAssignmentRefusal.NotLoaded:
                return "That cart is not loaded; go closer to it and select it again.";
            case CartAssignmentRefusal.NotOwnedHere:
                return "This game does not control that cart right now, so Gunnar will not take it.";
            case CartAssignmentRefusal.InUse:
                return "That cart is in use: close its container or let go of it first.";
            case CartAssignmentRefusal.Braked:
                return "That cart's parking brake is on; release it yourself first. Gunnar never touches the brake.";
            case CartAssignmentRefusal.NotUpright:
                return "That cart is not standing upright.";
            case CartAssignmentRefusal.AlreadyLeased:
                return "That cart is already assigned.";
            case CartAssignmentRefusal.WorkerBusy:
                return "Gunnar already has a cart; release it before assigning another.";
            case CartAssignmentRefusal.StaleIdentity:
                return "That selection is from before the world was loaded again; select the cart again.";
            case CartAssignmentRefusal.TooFarToSelect:
                return "Stand closer to the cart you want to assign.";
            default:
                return BugSentence;
        }
    }

    public static string Describe(HitchRefusal refusal)
    {
        switch (refusal)
        {
            case HitchRefusal.NoAuthority:
                return "Gunnar only hitches in single player, or as the host while nobody else is connected, with hauling turned on.";
            case HitchRefusal.LeaseNotActive:
                return "That cart is no longer assigned to Gunnar.";
            case HitchRefusal.CartGone:
                return "The assigned cart is gone or not loaded.";
            case HitchRefusal.NotOwnedHere:
                return "This game does not control the cart right now, so Gunnar will not hitch.";
            case HitchRefusal.MassNotCurrent:
                return "The cart's weight has not caught up with its load yet; Gunnar waits a moment before hitching.";
            case HitchRefusal.InUse:
                return "Someone is using the cart: its container is open or it is already hitched.";
            case HitchRefusal.Braked:
                return "The cart's parking brake is on; release it yourself. Gunnar never touches the brake.";
            case HitchRefusal.OtherJointOnClient:
                return "Another cart is being pulled nearby; hitching would unhitch it, so Gunnar waits.";
            case HitchRefusal.NotUpright:
                return "The cart is not standing upright.";
            case HitchRefusal.OutOfReach:
                return "Gunnar is not close enough to the handle yet.";
            case HitchRefusal.PullerBodyInvalid:
                return "Gunnar's body is not ready to pull a cart.";
            case HitchRefusal.VerifyFailed:
                return "The hitch did not hold as expected, so Gunnar let go again.";
            case HitchRefusal.SeamUnavailable:
                return "This game version changed how carts are hitched, so Gunnar cannot pull carts until Concerned Teamster is updated.";
            default:
                return BugSentence;
        }
    }
}
