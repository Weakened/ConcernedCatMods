using System.Collections.Generic;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Quest;

namespace TheConcernedCat.ConcernedForeman.Domain.Npc;

/// <summary>Where Thorstein's introduction stands, as a player would put it.</summary>
internal enum ThorsteinQuestStage
{
    /// <summary>The introduction has not begun; the carpenter's square has not
    /// been examined.</summary>
    NotMet = 0,

    /// <summary>He has introduced himself and is waiting for an axe and a
    /// hammer.</summary>
    WaitingForTools = 1,

    /// <summary>He is recruited. Permanent: losing a tool afterwards is a
    /// readiness question, never a relock.</summary>
    Recruited = 2,
}

/// <summary>What one pass should do. Exactly one, so the runtime has nothing to
/// order.</summary>
internal enum ToolsOfTheTradeStep
{
    /// <summary>Nothing to record.</summary>
    None = 0,

    /// <summary>Both tools are his and the introduction is open: recruit him
    /// through the settlement register, then record the quest complete. In
    /// that order - see <see cref="ToolsOfTheTrade.Decide"/>.</summary>
    Recruit = 1,

    /// <summary>He is already on the roster but this character's quest is not
    /// complete: a worker recruited before the quest existed, or by another
    /// character in this world, or a quest save that failed after the
    /// recruitment succeeded. Record the quest complete; recruit nobody.</summary>
    RecordCompletion = 2,
}

/// <summary>Why a <see cref="ToolsOfTheTradeStep.Recruit"/> that is otherwise
/// due cannot happen yet. The objectives are met; the settlement is not ready to
/// employ anybody.</summary>
internal enum ToolsOfTheTradeWait
{
    None = 0,

    /// <summary>No settlement area is marked. A worker belongs to a settlement,
    /// and the register refuses to employ one without it.</summary>
    NoSettlementArea = 1,

    /// <summary>The settlement's records could not be fully read, so nothing
    /// may be written over them.</summary>
    RecordsReadOnly = 2,

    /// <summary>This peer may not change the settlement (not the host, or the
    /// settlement runtime is off).</summary>
    NoAuthority = 3,

    /// <summary>He is on the roster in memory, but the settlement record has not
    /// reached disk. The story is never recorded over that.</summary>
    RecordsUnsaved = 5,

    /// <summary>A tool handover is unresolved. The record does not know
    /// whether he has it, and a recruitment decided on a guess is the kind of
    /// worker authority this repository requires to fail closed.</summary>
    HandoverUncertain = 4,
}

/// <summary>Everything the quest decision reads, as plain values.</summary>
internal readonly struct ToolsOfTheTradeFacts
{
    internal ToolsOfTheTradeFacts(
        QuestState quest,
        bool employed,
        bool axeHeld,
        bool hammerHeld,
        bool handoverUncertain,
        bool hasSettlementArea,
        bool recordsWritable,
        bool hasAuthority,
        bool rosterSaved = true)
    {
        Quest = quest;
        Employed = employed;
        AxeHeld = axeHeld;
        HammerHeld = hammerHeld;
        HandoverUncertain = handoverUncertain;
        HasSettlementArea = hasSettlementArea;
        RecordsWritable = recordsWritable;
        HasAuthority = hasAuthority;
        RosterSaved = rosterSaved;
    }

    /// <summary>The register in memory has nothing unwritten. False after a
    /// recruitment whose save failed: the roster says "employed" in memory and
    /// not on disk.</summary>
    internal bool RosterSaved { get; }

    /// <summary>This character's quest record, from the companion sidecar.</summary>
    internal QuestState Quest { get; }

    /// <summary>The settlement register employs Thorstein.</summary>
    internal bool Employed { get; }

    /// <summary>The tool ledger says an axe given to him is held by him. Read
    /// from the real ledger every time, never remembered by the quest: the
    /// tools are his actual equipment, not quest tokens.</summary>
    internal bool AxeHeld { get; }

    internal bool HammerHeld { get; }

    internal bool HandoverUncertain { get; }

    internal bool HasSettlementArea { get; }

    internal bool RecordsWritable { get; }

    internal bool HasAuthority { get; }
}

/// <summary>"Tools of the Trade" (#295): Thorstein's introduction, decided with
/// no game present.
///
/// <b>Story and authority are different records, and this keeps them so.</b>
/// The story is per character, in the shared companion sidecar
/// (<see cref="QuestState"/>). Whether Thorstein works for this settlement is
/// the settlement register's roster, per world, exactly as before the quest
/// existed. The quest never invents a second roster: completing it recruits him
/// through the same register act <c>cf_settle recruit</c> uses, with every
/// check that act already makes.
///
/// <b>The objectives are read, not stored.</b> "Give him an axe" is done when
/// the tool ledger says an axe given to him is held by him. The handover itself
/// is the existing one (#299-#301): a real, selected instance with its tier and
/// durability, moved into his own body. Nothing here moves an item, so nothing
/// here can lose or duplicate one, and a turn-in is never a destroyed quest
/// item.
///
/// <b>Monotonic.</b> Once recruited the stage stays recruited: a lost, broken or
/// returned tool is a readiness reason on the next order, never a relock. A
/// story replay grants nothing twice, because the only grant is the register's
/// recruitment and that is idempotent.</summary>
internal static class ToolsOfTheTrade
{
    /// <summary>The product slug the companion sidecar is filed under. Durable:
    /// it is part of the file name, so it is never renamed.</summary>
    internal static readonly ProductId Product = new ProductId("concerned-foreman");

    /// <summary>The quest's durable id inside that sidecar.</summary>
    internal static readonly QuestId QuestId = new QuestId("tools-of-the-trade");

    internal static ThorsteinQuestStage Stage(in ToolsOfTheTradeFacts facts)
    {
        if (QuestStateMachine.IsComplete(facts.Quest) || (facts.Employed && facts.RosterSaved))
        {
            return ThorsteinQuestStage.Recruited;
        }

        return facts.Quest == QuestState.IntroPending
            ? ThorsteinQuestStage.WaitingForTools
            : ThorsteinQuestStage.NotMet;
    }

    /// <summary>The one thing this pass should record.
    ///
    /// <b>Register first, story second.</b> If the recruitment is written and
    /// the story save then fails, the next pass sees him employed with an
    /// incomplete story and finishes the story (<see
    /// cref="ToolsOfTheTradeStep.RecordCompletion"/>). The other order could
    /// leave a story that says "recruited" over a roster that does not, and
    /// nothing would ever put him on it.</summary>
    internal static ToolsOfTheTradeStep Decide(in ToolsOfTheTradeFacts facts, out ToolsOfTheTradeWait wait)
    {
        wait = ToolsOfTheTradeWait.None;
        if (facts.Employed && !facts.RosterSaved)
        {
            // Employed only in memory. Recording the story now would leave it
            // saying "recruited" over a roster that, after a reload, does not.
            wait = ToolsOfTheTradeWait.RecordsUnsaved;
            return ToolsOfTheTradeStep.None;
        }

        if (facts.Employed)
        {
            return QuestStateMachine.IsComplete(facts.Quest)
                ? ToolsOfTheTradeStep.None
                : ToolsOfTheTradeStep.RecordCompletion;
        }

        if (QuestStateMachine.IsComplete(facts.Quest))
        {
            // The story is done but he is not employed: dismissed on purpose
            // (cf_settle dismiss), or recruited in another settlement record.
            // The story never re-recruits; employing him again is the player's
            // explicit act.
            return ToolsOfTheTradeStep.None;
        }

        if (facts.Quest != QuestState.IntroPending || !facts.AxeHeld || !facts.HammerHeld)
        {
            return ToolsOfTheTradeStep.None;
        }

        if (facts.HandoverUncertain)
        {
            wait = ToolsOfTheTradeWait.HandoverUncertain;
        }
        else if (!facts.HasAuthority)
        {
            wait = ToolsOfTheTradeWait.NoAuthority;
        }
        else if (!facts.RecordsWritable)
        {
            wait = ToolsOfTheTradeWait.RecordsReadOnly;
        }
        else if (!facts.HasSettlementArea)
        {
            wait = ToolsOfTheTradeWait.NoSettlementArea;
        }

        return wait == ToolsOfTheTradeWait.None ? ToolsOfTheTradeStep.Recruit : ToolsOfTheTradeStep.None;
    }

    /// <summary>The two visible objectives, in either order, each done or not.
    /// Empty before the introduction and after recruitment.</summary>
    internal static IReadOnlyList<ToolsOfTheTradeObjective> Objectives(in ToolsOfTheTradeFacts facts)
    {
        if (Stage(facts) != ThorsteinQuestStage.WaitingForTools)
        {
            return new ToolsOfTheTradeObjective[0];
        }

        return new[]
        {
            new ToolsOfTheTradeObjective(ThorsteinLines.ObjectiveAxe, facts.AxeHeld),
            new ToolsOfTheTradeObjective(ThorsteinLines.ObjectiveHammer, facts.HammerHeld),
        };
    }

    /// <summary>What the player is told about a wait.</summary>
    internal static string DescribeWait(ToolsOfTheTradeWait wait)
    {
        switch (wait)
        {
            case ToolsOfTheTradeWait.NoSettlementArea:
                return "He has both tools, but there is no settlement for him to work for yet. Mark one " +
                    "(cf_settle area <radius>) and he will join.";
            case ToolsOfTheTradeWait.RecordsReadOnly:
                return "He has both tools, but this settlement's records could not be fully read, so nothing " +
                    "is written over them. See cf_settle status.";
            case ToolsOfTheTradeWait.NoAuthority:
                return "He has both tools, but only the host with the settlement runtime on can take him on.";
            case ToolsOfTheTradeWait.RecordsUnsaved:
                return "The settlement record has unsaved changes and could not be saved; his story waits until " +
                    "it is. See cf_settle status.";
            case ToolsOfTheTradeWait.HandoverUncertain:
                return "He has both tools on record, but one handover is unresolved. Run cf_settle reconcile " +
                    "first; he is not taken on over a guess.";
            default:
                return string.Empty;
        }
    }
}

/// <summary>One visible objective.</summary>
internal readonly struct ToolsOfTheTradeObjective
{
    internal ToolsOfTheTradeObjective(string text, bool done)
    {
        Text = text;
        Done = done;
    }

    internal string Text { get; }

    internal bool Done { get; }

    public override string ToString() => (Done ? "[x] " : "[ ] ") + Text;
}

/// <summary>Thorstein's authored lines for the introduction (#295). Authored
/// fiction, an owner-invited proposal, not vanilla lore. English only in this
/// slice: Foreman has no localization catalog yet, and the talk slice (S3) is
/// where these move into one.</summary>
internal static class ThorsteinLines
{
    internal const string Inscription = "A roof first. Everything else can wait. — T.";

    internal const string Introduction =
        "Left my tools with the last folk who needed them. They've a roof now. I've two empty hands. " +
        "Bring me an axe and a hammer. We'll see about making this place a home.";

    internal const string Completion =
        "Good edge. Sound handle. Right, I'm Thorstein. Point out the work — I'll mind the rest.";

    internal const string ObjectiveAxe = "Give Thorstein an axe";

    internal const string ObjectiveHammer = "Give Thorstein a hammer";
}
