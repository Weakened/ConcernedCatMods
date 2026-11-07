using System;
using System.Collections.Generic;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;
/// <summary>Rows ready for the lead-owned TeamsterStrings English catalog.
/// Until that merge, the same source rows provide a deterministic fallback.</summary>
internal static class GunnarDialogueStrings
{
    private static readonly Dictionary<string,string> Rows = new(StringComparer.Ordinal)
    {
        ["gunnar.intro"] = "I am Gunnar. That buckle has seen a few roads. Bring me a usable cart, and we can discuss a fair load.",
        ["gunnar.objective"] = "Bring Gunnar a usable cart",
        ["gunnar.complete"] = "A sound cart, chosen by you. I will lend a hand when you ask.",
        ["gunnar.attention"] = "We need to stop: {0}",
        ["gunnar.hello"] = "You look as though you have something heavy in mind.",
        ["gunnar.back"] = "Back with everything you left with? Good.",
        ["gunnar.ready"] = "We can talk before we put our backs into it.",
        ["gunnar.road"] = "A shorter road is not always an easier pull.",
        ["gunnar.stack"] = "One more stack. That is what everyone says before we lose a wheel.",
        ["gunnar.downhill"] = "Downhill is not free. You pay at the bottom.",
        ["gunnar.heavy"] = "Heavy is not the problem. Pretending it is not heavy is the problem.",
        ["gunnar.walk"] = "Walk the road before you promise the load.",
        ["gunnar.bend"] = "Leave yourself room at the bend. A cart has opinions about corners.",
        ["gunnar.return"] = "Leave enough strength for the way home.",
        ["gunnar.roots"] = "A root is small until you put a wheel against it.",
        ["gunnar.rope"] = "A sound rope is worth looking at twice.",
        ["gunnar.balance"] = "A balanced load saves an argument with every rut.",
        ["gunnar.stop"] = "Choose where to stop before the hill chooses for you.",
        ["gunnar.pace"] = "A steady pull brings more home than a hurried promise.",
        ["gunnar.check"] = "Count what you brought. Then count what arrived.",
        ["gunnar.room"] = "Room for another bundle is not the same as an easy pull.",
        ["gunnar.fair"] = "A fair load. A clear road. Everyone home. I meant every word.",
        ["gunnar.fire"] = "A fire is a fine place to put down a worry.",
        ["gunnar.rest"] = "Rest is part of the journey. I keep telling people that.",
        ["gunnar.bench"] = "A bench that does not move is a welcome change from a cart.",
        ["gunnar.home"] = "Getting home with the load is only half of it. Everyone must get home too.",
        ["gunnar.boots"] = "I will take dry boots over an impressive story.",
        ["gunnar.thanks"] = "You carried your share. That matters.",
        ["gunnar.quiet"] = "A quiet camp is a thing worth keeping.",
        ["gunnar.sky"] = "Look at the sky before you load the last bundle.",
        ["gunnar.rain"] = "Rain has a way of making a familiar road ask new questions.",
        ["gunnar.wind"] = "Leave a little patience for the wind.",
        ["gunnar.dry"] = "Keep something dry for the way back.",
        ["gunnar.weather"] = "We can wait out poor weather. The road will still be there.",
    };
    public static IReadOnlyDictionary<string,string> English => Rows;
    public static string Resolve(string key, Func<string,string>? translate = null)
    {
        string? value=translate?.Invoke(key);
        if (!string.IsNullOrWhiteSpace(value) && value != key) return value!;
        return Rows.TryGetValue(key,out string? english) ? english : key;
    }
}
