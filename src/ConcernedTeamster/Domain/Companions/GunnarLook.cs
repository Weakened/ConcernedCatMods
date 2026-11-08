using System;
using TheConcernedCat.ConcernedNPC.Appearance;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;
internal static class GunnarLook
{
    public const string HairLabel = "Short Curls";
    public const string BeardLabel = "Stonedweller";
    // IDs are populated only from this installation's observed label matches.
    // There is deliberately no guessed Hair23/Beard16 fallback in the product.
    public static NpcLookSpec Spec(string? observedHairId = null, string? observedBeardId = null,
        string? hairOverride = null, string? beardOverride = null) =>
        new("Gunnar", NpcBodyModel.Male,
            Request(HairLabel, observedHairId), Request(BeardLabel, observedBeardId),
            null, null, 0.52f, 0.05f, 0.49f,
            new NpcLookRgb(0.35f, 0.32f, 0.28f), hairOverride, beardOverride);
    private static NpcLookRequest Request(string label, string? observed) => new(label,
        string.IsNullOrEmpty(observed) ? Array.Empty<string>() : new[] { observed! },
        Array.Empty<string>(), "CT_no_unverified_family_fallback");
}
