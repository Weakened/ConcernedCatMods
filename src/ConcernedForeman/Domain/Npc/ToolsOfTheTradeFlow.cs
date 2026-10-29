using TheConcernedCat.Companions.Quest;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>What the flow needs from a running game, and nothing more. The
/// runtime answers with the settlement register, the tool ledger and the
/// companion sidecar; the tests answer with fakes that can fail any write.</summary>
internal interface IToolsOfTheTradePorts
{
    /// <summary>Reads every fact afresh, including whether the roster in memory
    /// has reached disk.</summary>
    ToolsOfTheTradeFacts Read();

    /// <summary>The register's own recruitment, with its own checks. True when
    /// he is on the roster in memory; <paramref name="refusal"/> otherwise.</summary>
    bool TryRecruit(out string refusal);

    /// <summary>Writes the settlement records. True only when nothing is left
    /// unwritten.</summary>
    bool TrySaveRoster(out string? notice);

    /// <summary>Records the story complete. True only when it reached disk.</summary>
    bool TryRecordWelcome(out string? notice);

    /// <summary>Records that the join was announced. True only the first time,
    /// and only once it reached disk.</summary>
    bool TryAnnounceJoin();
}

/// <summary>The sequence that turns a <see cref="ToolsOfTheTrade.Decide"/>
/// answer into writes, game-free so that its one ordering rule is tested
/// rather than reasoned about: <b>the story is recorded only over a roster that
/// is on disk.</b>
///
/// The register's recruitment adds him to the roster in memory before the save.
/// So a save that fails leaves a roster that says "employed" in memory and does
/// not say it on disk. Recording the story over that state would leave a story
/// that says "recruited" over a roster that, after a reload, does not - which
/// <see cref="ToolsOfTheTrade.Decide"/> then reads as a dismissal and never
/// repairs. So an unsaved roster is saved again first, and if it still will not
/// write, the story waits.</summary>
internal static class ToolsOfTheTradeFlow
{
    internal static string Settle(IToolsOfTheTradePorts ports)
    {
        ToolsOfTheTradeFacts facts = ports.Read();
        if (facts.Employed && !facts.RosterSaved)
        {
            // Left over from a failed save earlier this session. Try once more
            // before anything is decided on top of it.
            if (!ports.TrySaveRoster(out string? notice))
            {
                return Unsaved(notice);
            }

            facts = ports.Read();
        }

        switch (ToolsOfTheTrade.Decide(facts, out ToolsOfTheTradeWait wait))
        {
            case ToolsOfTheTradeStep.Recruit:
                if (!ports.TryRecruit(out string refusal))
                {
                    return "\nHe has both tools but could not be taken on: " + refusal;
                }

                if (!ports.TrySaveRoster(out string? saveNotice))
                {
                    return Unsaved(saveNotice);
                }

                return RecordStory(ports, ports.Read());

            case ToolsOfTheTradeStep.RecordCompletion:
                return RecordStory(ports, facts);

            default:
                string waiting = ToolsOfTheTrade.DescribeWait(wait);
                return waiting.Length == 0 ? string.Empty : "\n" + waiting;
        }
    }

    private static string RecordStory(IToolsOfTheTradePorts ports, in ToolsOfTheTradeFacts facts)
    {
        if (!ports.TryRecordWelcome(out string? notice))
        {
            // He is employed and that is on disk; only the story is late. It is
            // finished on the next world load (RecordCompletion).
            return "\nThorstein is taken on. His story could not be saved and will be recorded when this world " +
                "is next loaded." + (notice == null ? string.Empty : " " + notice);
        }

        if (!ports.TryAnnounceJoin())
        {
            return string.Empty;
        }

        // The completion line belongs to the moment he took the tools from THIS
        // character: their introduction was open and both tools are his. A
        // worker on the roster before this story existed, or taken on by
        // another character, gets the record without the speech. The facts are
        // the ones read before the welcome, so this sees the story as it was.
        if (facts.Quest == QuestState.IntroPending && facts.AxeHeld && facts.HammerHeld)
        {
            return "\nThorstein: \"" + ThorsteinLines.Completion + "\"\nThorstein joined your settlement. " +
                "Give him a work order when you are ready; nothing starts on its own.";
        }

        return "\nThorstein already works for this settlement, so his introduction is recorded as complete.";
    }

    private static string Unsaved(string? notice) =>
        "\nThe settlement record has unsaved changes and could not be saved, so Thorstein's story is not " +
        "recorded yet; it waits until the record is written. Nothing else changed." +
        (notice == null ? string.Empty : " " + notice);
}
