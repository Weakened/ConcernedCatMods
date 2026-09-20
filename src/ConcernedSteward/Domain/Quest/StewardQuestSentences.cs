using System;

namespace TheConcernedCat.ConcernedSteward.Domain.Quest;

/// <summary>Everything Sunniva's introduction says, in one place.
///
/// <b>Separated from the state machine on purpose, and it is the same
/// separation <c>StewardSentences</c> already makes.</b> The machine decides
/// what may happen; this decides what is read. Rewriting a line is an edit to
/// this file and nothing else: no state to migrate, no behaviour to re-test, and
/// no chance of changing what the quest does by changing what it says.
///
/// <b>Several beats, not one speech.</b> Each stage returns a small ordered set
/// of short lines rather than a paragraph, because the runtime prints them one
/// at a time and because an NPC who explains herself in a single block is an NPC
/// from a different game. The tone is the one Valheim writes in: short
/// sentences, nothing explained twice, and nobody telling the player what to
/// feel about it.
///
/// <b>What the beats must between them establish</b>, per #382, and where:
/// the object was hers (<see cref="StewardQuestStage.Examined"/>,
/// <see cref="StewardQuestStage.Answered"/>); she was a warrior-priestess and
/// she fell in battle (<see cref="StewardQuestStage.Answered"/>); she carried
/// light into dark places (<see cref="StewardQuestStage.Answered"/>); the
/// settlement's flames are a responsibility she takes seriously
/// (<see cref="StewardQuestStage.Answered"/>); and that after being taken on she
/// chooses to stay (<see cref="StewardQuestStage.Settled"/>).
/// <c>StewardQuestTests</c> asserts each of those is actually said, so deleting
/// a beat fails a test rather than quietly losing the character.</summary>
internal static class StewardQuestSentences
{
    /// <summary>The line #382 specifies, word for word. It is the one sentence
    /// in this file that is not free to be rewritten without the owner, because
    /// it is the sentence the whole quest was described by.</summary>
    internal const string Discovery =
        "A flint and steel are tangled with the resin. Runes have been carved into the metal.";

    private static readonly string[] None = Array.Empty<string>();

    private static readonly string[] FoundLines =
    {
        Discovery,
        "Somebody carried these a long way before they ended up in a tree.",
    };

    private static readonly string[] ExaminedLines =
    {
        "The runes are a name and a rank. Sunniva. Flamebearer.",
        "The steel is worn hollow where a thumb has held it. It has lit more fires than you have.",
        "There is a second line under the name, cut later and cut badly, the way a thing is cut " +
        "in the dark: still burning.",
    };

    private static readonly string[] AnsweredLines =
    {
        "A woman is standing at the edge of the firelight. She is not out of breath, and she has " +
        "not come from anywhere you can see.",
        "\"I was a priestess with a spear,\" she says. \"I carried light into the places that had " +
        "none. I fell in one of them and I did not come back up.\"",
        "\"Fires go out. That is the whole of what a fire does, if nobody stands over it.\"",
        "She looks past you, at your hearth, for a while. \"That one is low.\"",
    };

    private static readonly string[] SettledLines =
    {
        "Sunniva has set her pack down by the hearth and has not picked it up again. She is not " +
        "going anywhere.",
        "\"Keep the wood where I can reach it,\" she says. \"I will keep the flames.\"",
    };

    /// <summary>Every line of one beat, in order. Empty for a stage that has
    /// nothing to say, which is the honest answer for
    /// <see cref="StewardQuestStage.Unstarted"/> - nothing has happened.
    /// </summary>
    internal static string[] LinesFor(StewardQuestStage stage)
    {
        switch (stage)
        {
            case StewardQuestStage.Found: return FoundLines;
            case StewardQuestStage.Examined: return ExaminedLines;
            case StewardQuestStage.Answered: return AnsweredLines;
            case StewardQuestStage.Settled: return SettledLines;
            default: return None;
        }
    }

    /// <summary>The first line of a beat, for a caller with room for one.
    /// </summary>
    internal static string ForStage(StewardQuestStage stage)
    {
        string[] lines = LinesFor(stage);
        return lines.Length == 0 ? string.Empty : lines[0];
    }

    /// <summary>What to tell a player who asks where the introduction stands.
    /// Never a hint about what to do next unless there is genuinely something
    /// for them to do: an NPC who nags is worse than one who waits.</summary>
    internal static string Describe(StewardQuestStage stage)
    {
        switch (stage)
        {
            case StewardQuestStage.Found:
                return "You are carrying a flint and steel with a name cut into it. " +
                    "Run \"cs_steward runes\" to look at them properly.";

            case StewardQuestStage.Examined:
                return "The flint and steel belonged to somebody called Sunniva. " +
                    "Mark a settlement with \"cs_steward area <radius>\" and she will find it.";

            case StewardQuestStage.Answered:
                return "Sunniva is here, and she has looked at your fires. " +
                    "\"cs_steward recruit\" takes her on.";

            case StewardQuestStage.Settled:
                return "Sunniva lives here.";

            default:
                return "Nothing has happened yet.";
        }
    }

    /// <summary>Why a beat did not happen, in a sentence a player can act on.
    /// </summary>
    internal static string DescribeRefusal(StewardQuestRefusal refusal)
    {
        switch (refusal)
        {
            case StewardQuestRefusal.AlreadyFound:
                return "there is one flint and steel and you already have it";

            case StewardQuestRefusal.NotRecorded:
                return "nothing could be written down, and nothing is ever true in memory alone " +
                    "here — a thing granted and not recorded is granted again after a reload";

            case StewardQuestRefusal.OutOfOrder:
                return "that is not the next thing that happens";

            case StewardQuestRefusal.NobodyThere:
                return "there is nobody here to find anything";

            default:
                return "no reason was recorded, which is a bug — please report it";
        }
    }
}
