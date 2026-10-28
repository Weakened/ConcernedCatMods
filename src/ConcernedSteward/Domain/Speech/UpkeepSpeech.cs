using System.Globalization;
using TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

namespace TheConcernedCat.ConcernedSteward.Domain.Speech;

/// <summary>What Sunniva says about her work, and the subject each sentence is
/// throttled under.
///
/// Kept apart from the loop for the same reason <c>StewardQuestSentences</c> is
/// kept apart from the quest: rewording a line is an edit to this file and to
/// nothing that decides anything.
///
/// <b>Item names are vanilla's own tokens</b> (<c>$item_resin</c>), and that is
/// deliberate: vanilla's message display localizes every <c>$token</c> in a line
/// before it draws it, so a player reads "Resin" in their own language and the
/// log keeps the exact identity a bug report needs.</summary>
internal static class UpkeepSpeech
{
    /// <summary>The subject for a shortage of one particular set of fuels.
    /// Keyed on the items and never on the counts, so a shortage that grows as
    /// the fires burn down is not a new thing to say every scan.</summary>
    internal static string ShortSubject(in RoundManifest shortfall)
    {
        if (shortfall.IsEmpty)
        {
            return "short:";
        }

        var text = new System.Text.StringBuilder("short:");
        for (int index = 0; index < shortfall.Lines.Count; index++)
        {
            if (index > 0)
            {
                text.Append(',');
            }

            text.Append(shortfall.Lines[index].Item);
        }

        return text.ToString();
    }

    /// <summary>"No free fuel", said to the player: what she needs, and for how
    /// many lights. <paramref name="lightsWaiting"/> is the number of lights
    /// that want fuel and are not in the round because of it.</summary>
    internal static string Short(in RoundManifest shortfall, int lightsWaiting)
    {
        string lights = lightsWaiting == 1
            ? "One light is"
            : lightsWaiting.ToString(CultureInfo.InvariantCulture) + " lights are";

        return StewardRole.DisplayNameFallbackCapitalised + ": \"I need " + shortfall.Describe() +
            ". " + lights + " burning down, and the chest I may take from does not hold enough.\"";
    }

    /// <summary>The subject for lights she would tend and may not touch.
    /// </summary>
    internal const string UntouchableSubject = "untouchable";

    /// <summary>Lights that are low and that she may not touch - a ward that is
    /// not the player's, or a fire this game session does not own. She does not
    /// work around it and does not stay quiet about it.</summary>
    internal static string Untouchable(int lights)
    {
        string count = lights == 1
            ? "One light here is"
            : lights.ToString(CultureInfo.InvariantCulture) + " lights here are";

        return StewardRole.DisplayNameFallbackCapitalised + ": \"" + count +
            " burning low, and they are not mine to touch. A ward that is not yours, or a fire " +
            "this world will not let me reach.\"";
    }

    /// <summary>The same, as a line for the status command rather than speech.
    /// </summary>
    internal static string UntouchableStatus(int lights) =>
        lights.ToString(CultureInfo.InvariantCulture) +
        " light(s) in the settlement are burning low and she may not touch them (a ward that is " +
        "not yours, or a fire this session does not own). She will not work around that.";

    /// <summary>The subject for a stop that needs a person.</summary>
    internal const string StoppedSubject = "stopped";

    /// <summary>She has stopped and will not start again until somebody runs
    /// <c>cs_steward resolve</c>. Said once, because the state does not change
    /// until a person acts.</summary>
    internal static string Stopped(string reason) =>
        StewardRole.DisplayNameFallbackCapitalised + " has stopped and needs you to look: " +
        (string.IsNullOrEmpty(reason) ? "no reason was recorded." : reason);
}
