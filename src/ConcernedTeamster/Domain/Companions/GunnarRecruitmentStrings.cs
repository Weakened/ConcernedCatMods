using System;
using System.Collections.Generic;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;
/// <summary>Lead-catalog rows and deterministic fallback, same boundary as the
/// Gunnar dialogue source while the shared localization file is actively owned.</summary>
internal static class GunnarRecruitmentStrings
{
    internal const string Status="status",Choose="choose",Confirm="confirm",Cancel="cancel",Offer="offer";
    internal static readonly IReadOnlyDictionary<string,string> English=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["gunnar.recruit.ui.button"]="Gunnar Introduction",
        ["gunnar.recruit.ui.title"]="A Fair Load",
        ["gunnar.recruit.ui.choose"]="Choose Pointed Cart",
        ["gunnar.recruit.ui.confirm"]="Confirm Recruitment",
        ["gunnar.recruit.ui.status"]="Refresh Status",
        ["gunnar.recruit.ui.cancel"]="Cancel / Close",
        ["gunnar.recruit.ui.offer"]="First Delivery (Optional)",
        ["gunnar.recruit.scope_changed"]="The character/world changed. Reopen the introduction and select again.",
        ["gunnar.recruit.unavailable"]="Gunnar's introduction is unavailable here.",
        ["gunnar.recruit.delivery_offer"]="Optional first delivery: open Gunnar's existing hauling panel, choose your cart and plan a trip using your own cargo. This offer adds nothing and starts no job.",
        ["gunnar.recruit.not_recruited"]="Confirm Gunnar's recruitment before planning an optional first delivery.",
        ["gunnar.recruit.select_first"]="Point at your cart, then Choose Pointed Cart before confirming.",
        ["gunnar.recruit.surface_failure"]="Recruitment stopped after a native/UI error. Existing hauling tools remain available.",
        ["gunnar.recruit.work_unavailable"]="Hauling is unavailable here.",
        ["gunnar.recruit.work_authority"]="Hauling is paused: enable the hauling switches on the host with no connected peers.",
        ["gunnar.recruit.worker_setup"]="Gunnar needs his worker setup in the existing hauling panel before a job can begin.",
        ["gunnar.recruit.assign_cart"]="No cart is assigned for a job. Choose one in the existing hauling panel.",
        ["gunnar.recruit.cart_assigned"]="His cart is assigned. Use the existing hauling panel to plan or inspect a trip.",
        ["gunnar.recruit.authority_required"]="Recruitment needs Teamster and Gunnar hauling enabled, Match Player pull strength, the host, and no connected peers. Nothing was recorded.",
        ["gunnar.recruit.save_failed"]="Recruitment could not be saved. Select and confirm again when storage is writable.",
        ["gunnar.recruit.stale"]="That confirmation is no longer current. Choose the exact cart again.",
        ["gunnar.recruit.intro_required"]="Read Gunnar's introduction before choosing a cart.",
        ["gunnar.recruit.selection_required"]="Point at a nearby real cart and choose it explicitly.",
        ["gunnar.recruit.wrong_kind"]="Choose a hand cart, rather than a siege machine.",
        ["gunnar.recruit.destroyed"]="The selected cart is gone. Choose a real cart again.",
        ["gunnar.recruit.unloaded"]="The selected cart is not fully loaded. Return to it and select again.",
        ["gunnar.recruit.ownership"]="The selected cart is not available to this host. Its ownership is left alone.",
        ["gunnar.recruit.in_use"]="The selected cart is occupied or in use. Free it before selecting again.",
        ["gunnar.recruit.reach"]="Return within reach of the selected cart, then select again.",
        ["gunnar.recruit.conflicting_lease"]="Gunnar already has a different cart assigned. Resolve that assignment in the hauling panel first.",
        ["gunnar.recruit.unknown_cart"]="The cart could not be checked. Nothing was recorded; select it again when available.",
        ["gunnar.recruit.optional_tip"]="Optional: plan a first delivery using your own cargo in the existing hauling panel.",
        ["gunnar.recruit.permanent"]="Gunnar is permanently recruited. Work readiness is separate.",
        ["gunnar.recruit.instructions"]="Point at a nearby real cart, Choose Pointed Cart, then confirm its name. Hauling opt-in, host authority and no connected peers are required.",
        ["gunnar.recruit.pending"]="A confirmation is already open for {0}. Confirm or Cancel / Close first.",
        ["gunnar.recruit.question"]="Confirm recruitment using {0}? No cart/cargo operation or job will start.",
        ["gunnar.recruit.attention"]="His current trip needs attention: {0}",
    };
    internal static string Resolve(string key,Func<string,string>? translate=null)
    {
        string? value=translate?.Invoke(key);
        return !string.IsNullOrWhiteSpace(value)&&value!=key?value!:
            English.TryGetValue(key,out string? english)?english:key;
    }
}
