using System;
using TheConcernedCat.ConcernedNPC.Appearance;
using TheConcernedCat.ConcernedTeamster.Domain.Companions;
namespace TheConcernedCat.ConcernedTeamster.Adapters.Companions;

/// <summary>Read actual installed item metadata, never fabricate preset IDs.
/// Call once after ObjectDB and localization are ready, not from a hot tick.
/// In a non-English installation whose labels cannot be matched, no hardcoded
/// ID fallback is inferred: keep the source look and report it for review.</summary>
internal static class GunnarLookSource
{
    public static NpcLookSpec Read(Action<string> log, string? hairOverride = null, string? beardOverride = null)
    {
        string? hair=null, beard=null;
        try
        {
            if (ObjectDB.instance != null && Localization.instance != null)
            foreach (UnityEngine.GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null) continue;
                ItemDrop drop=item.GetComponent<ItemDrop>();
                if (drop == null) continue;
                string label=Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
                if (string.Equals(label,GunnarLook.HairLabel,StringComparison.OrdinalIgnoreCase)) hair=item.name;
                if (string.Equals(label,GunnarLook.BeardLabel,StringComparison.OrdinalIgnoreCase)) beard=item.name;
            }
        }
        catch (Exception exception) { log("Gunnar preset metadata unavailable ("+exception.GetType().Name+")."); }
        log("Gunnar installed label matches: Short Curls="+(hair ?? "not resolved")+
            "; Stonedweller="+(beard ?? "not resolved")+". Palette and fit still require visual review.");
        return GunnarLook.Spec(hair,beard,hairOverride,beardOverride);
    }
}
