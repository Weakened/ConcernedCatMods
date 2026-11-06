using System.Collections.Generic;
using TheConcernedCat.Companions.Dialogue;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>What Thorstein says when the player talks to him at home (#295 S3).
///
/// <b>Interaction-driven only.</b> He speaks when spoken to, never on a timer,
/// so there is no ambient chatter to tune or to grow tired of.
///
/// <b>Before he is taken on</b> he answers about the two things he asked for,
/// read from the real tool ledger the same way the quest's objectives are: he
/// never claims a tool he does not hold. <b>After</b>, he has the house-building
/// remarks #295 proposes, rotated through the shared companion
/// <see cref="DialogueRotation"/> so the same remark does not come back while
/// others are unsaid.
///
/// The line about Hulgi in #295 ("Hulgi keeps drawing houses on maps...") is
/// left out on purpose: it may only be said once Hulgi is actually known, and
/// this build has no signal for that yet. It is not said rather than said
/// wrongly. Authored fiction, an owner-invited proposal; English only, like the
/// rest of Thorstein's lines in this build.</summary>
internal static class ThorsteinTalk
{
    internal const string Name = "Thorstein";

    internal const string Verb = "Talk";

    internal const string BothTools =
        "An axe and a hammer, and we'll see about making this place a home. I'll wait.";

    internal const string NeedsHammer = "Good edge on that axe. Now a hammer, and I can start.";

    internal const string NeedsAxe = "That hammer will do nicely. An axe, and we're in business.";

    internal const string WaitingOnSettlement =
        "Both tools, and good ones. Show me where the settlement is and I'll get to work.";

    internal const string WaitingOnRecords =
        "Both tools, and good ones. But your settlement's records can't be read right now, and I'll not be " +
        "taken on over a smudged page.";

    internal const string WaitingOnUnsavedRecords =
        "Both tools, and good ones. Your settlement's records won't save just now; once they do, I'm yours.";

    internal const string WaitingOnAuthority =
        "Both tools, and good ones. Only the one hosting this world can take me on.";

    internal const string WaitingOnHandover =
        "One of those handovers hasn't settled. Let's square it first - I'll not work on a guess.";

    internal const string WaitingBriefly = "Both tools, and good ones. Give me a moment.";

    /// <summary>The remarks after he is taken on, by stable key.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Remarks = new Dictionary<string, string>
    {
        ["thorstein.home.roof"] = "A roof is a promise to whoever sleeps beneath it.",
        ["thorstein.home.beam"] = "That beam is doing a great deal of work. I hope you thanked it.",
        ["thorstein.home.measure"] = "Measure twice. Carry the timber once.",
    };

    /// <summary>A fresh rotation over <see cref="Remarks"/>.</summary>
    internal static DialogueRotation NewRotation()
    {
        var lines = new List<DialogueLine>();
        foreach (string key in Remarks.Keys)
        {
            lines.Add(new DialogueLine(key, DialogueCategory.CatAndHome));
        }

        return new DialogueRotation(new DialogueCatalog(lines), suppressionWindow: Remarks.Count - 1);
    }

    /// <summary>The line for a press of the use key, or null for none.</summary>
    /// <param name="wait">Why recruitment is waiting, from
    /// <see cref="ToolsOfTheTrade.Decide"/>: with both tools held, the line
    /// names that cause rather than guessing one.</param>
    internal static string? Line(
        ThorsteinQuestStage stage, bool axeHeld, bool hammerHeld, DialogueRotation rotation,
        ToolsOfTheTradeWait wait = ToolsOfTheTradeWait.None)
    {
        switch (stage)
        {
            case ThorsteinQuestStage.WaitingForTools:
                if (axeHeld && !hammerHeld)
                {
                    return NeedsHammer;
                }

                if (hammerHeld && !axeHeld)
                {
                    return NeedsAxe;
                }

                if (!axeHeld)
                {
                    return BothTools;
                }

                // Both held but not yet taken on: say the real reason, the one
                // ToolsOfTheTrade.Decide gave (S3 review).
                switch (wait)
                {
                    case ToolsOfTheTradeWait.NoSettlementArea:
                        return WaitingOnSettlement;
                    case ToolsOfTheTradeWait.RecordsReadOnly:
                        return WaitingOnRecords;
                    case ToolsOfTheTradeWait.RecordsUnsaved:
                        return WaitingOnUnsavedRecords;
                    case ToolsOfTheTradeWait.NoAuthority:
                        return WaitingOnAuthority;
                    case ToolsOfTheTradeWait.HandoverUncertain:
                        return WaitingOnHandover;
                    default:
                        return WaitingBriefly;
                }

            case ThorsteinQuestStage.Recruited:
                return rotation.TryNext(null, DialogueContext.Empty, out DialogueLine line)
                    && Remarks.TryGetValue(line.Key, out string? text)
                    ? text
                    : null;

            default:
                return null;
        }
    }
}
