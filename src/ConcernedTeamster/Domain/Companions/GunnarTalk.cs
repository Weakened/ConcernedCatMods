using System;
using TheConcernedCat.Companions.Dialogue;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Ui.Hauling;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>No clock or ambient entry point. The figure's Use callback is the
/// sole caller. Scope owners create a new rotation on world/character change.</summary>
internal sealed class GunnarTalk
{
    public static readonly DialogueCatalog Catalog = new(new DialogueLine[]
    {
        new("gunnar.hello", DialogueCategory.Greeting),
        new("gunnar.back", DialogueCategory.Greeting),
        new("gunnar.ready", DialogueCategory.Greeting),
        new("gunnar.road", DialogueCategory.Travel),
        new("gunnar.stack", DialogueCategory.Travel),
        new("gunnar.downhill", DialogueCategory.Travel),
        new("gunnar.heavy", DialogueCategory.Travel),
        new("gunnar.walk", DialogueCategory.Travel),
        new("gunnar.bend", DialogueCategory.Travel),
        new("gunnar.return", DialogueCategory.Travel),
        new("gunnar.roots", DialogueCategory.Travel),
        new("gunnar.rope", DialogueCategory.Travel),
        new("gunnar.balance", DialogueCategory.Travel),
        new("gunnar.stop", DialogueCategory.Travel),
        new("gunnar.pace", DialogueCategory.Travel),
        new("gunnar.check", DialogueCategory.Travel),
        new("gunnar.room", DialogueCategory.Travel),
        new("gunnar.fair", DialogueCategory.CatAndHome),
        new("gunnar.fire", DialogueCategory.CatAndHome),
        new("gunnar.rest", DialogueCategory.CatAndHome),
        new("gunnar.bench", DialogueCategory.CatAndHome),
        new("gunnar.home", DialogueCategory.CatAndHome),
        new("gunnar.boots", DialogueCategory.CatAndHome),
        new("gunnar.thanks", DialogueCategory.CatAndHome),
        new("gunnar.quiet", DialogueCategory.CatAndHome),
        new("gunnar.sky", DialogueCategory.Weather),
        new("gunnar.rain", DialogueCategory.Weather),
        new("gunnar.wind", DialogueCategory.Weather),
        new("gunnar.dry", DialogueCategory.Weather),
        new("gunnar.weather", DialogueCategory.Weather),
    });
    private readonly DialogueRotation _rotation;
    public GunnarTalk(int startOffset = 0) { _rotation=new DialogueRotation(Catalog,startOffset:startOffset); }
    public static string? AttentionKey(HaulAttentionReason reason) =>
        reason != HaulAttentionReason.Unspecified && Enum.IsDefined(typeof(HaulAttentionReason),reason)
        ? HaulPanelKeys.ForReason(reason) : null;
    public string? Interact(QuestState stage, HaulPhase phase, HaulAttentionReason reason,
        Func<string,string>? translate = null)
    {
        if (stage == QuestState.IntroPending)
            return GunnarDialogueStrings.Resolve("gunnar.objective",translate);
        if (stage != QuestState.Recruited) return null;
        string? attention = phase == HaulPhase.NeedsAttention ? AttentionKey(reason) : null;
        if (attention != null)
            return GunnarDialogueStrings.Resolve("gunnar.attention",translate).Replace("{0}",TeamsterStrings.Get(attention));
        return _rotation.TryNext(null,DialogueContext.Empty,out DialogueLine line)
            ? GunnarDialogueStrings.Resolve(line.Key,translate) : null;
    }
}
