using System;
using System.IO;
using System.Linq;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Session;
using TheConcernedCat.ConcernedForeman.Domain.Npc;

namespace ConcernedForeman.Tests;

/// <summary>#295 slice S1: "Tools of the Trade", decided with no game present.
/// The properties under test are the ones #295 states: either order, resumable,
/// the tools are his real equipment read from the ledger, recruitment is the
/// register's act and fails closed, a completed introduction never relocks, and
/// replaying the story grants nothing twice.</summary>
public sealed class ToolsOfTheTradeTests
{
    private static ToolsOfTheTradeFacts Facts(
        QuestState quest = QuestState.IntroPending,
        bool employed = false,
        bool axe = false,
        bool hammer = false,
        bool uncertain = false,
        bool area = true,
        bool writable = true,
        bool authority = true) =>
        new ToolsOfTheTradeFacts(quest, employed, axe, hammer, uncertain, area, writable, authority);

    [Fact]
    public void Before_the_square_is_examined_nothing_is_asked_and_nothing_recorded()
    {
        ToolsOfTheTradeFacts facts = Facts(QuestState.Unstarted, axe: true, hammer: true);

        Assert.Equal(ThorsteinQuestStage.NotMet, ToolsOfTheTrade.Stage(facts));
        Assert.Empty(ToolsOfTheTrade.Objectives(facts));
        Assert.Equal(ToolsOfTheTradeStep.None, ToolsOfTheTrade.Decide(facts, out _));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void One_tool_or_none_waits_and_shows_which(bool axe, bool hammer)
    {
        ToolsOfTheTradeFacts facts = Facts(axe: axe, hammer: hammer);

        Assert.Equal(ThorsteinQuestStage.WaitingForTools, ToolsOfTheTrade.Stage(facts));
        Assert.Equal(ToolsOfTheTradeStep.None, ToolsOfTheTrade.Decide(facts, out ToolsOfTheTradeWait wait));
        Assert.Equal(ToolsOfTheTradeWait.None, wait);

        var objectives = ToolsOfTheTrade.Objectives(facts);
        Assert.Equal(2, objectives.Count);
        Assert.Equal(axe, objectives[0].Done);
        Assert.Equal(hammer, objectives[1].Done);
    }

    [Fact]
    public void Both_tools_in_either_order_recruit_him_through_the_register()
    {
        // The facts carry no order: whichever tool arrived second, the pass
        // that sees both is the one that recruits.
        Assert.Equal(ToolsOfTheTradeStep.Recruit, ToolsOfTheTrade.Decide(Facts(axe: true, hammer: true), out _));
    }

    [Theory]
    [InlineData(false, true, true, false, (int)ToolsOfTheTradeWait.NoSettlementArea)]
    [InlineData(true, false, true, false, (int)ToolsOfTheTradeWait.RecordsReadOnly)]
    [InlineData(true, true, false, false, (int)ToolsOfTheTradeWait.NoAuthority)]
    [InlineData(true, true, true, true, (int)ToolsOfTheTradeWait.HandoverUncertain)]
    public void Recruitment_fails_closed_and_says_why(
        bool area, bool writable, bool authority, bool uncertain, int expectedWait)
    {
        ToolsOfTheTradeFacts facts = Facts(
            axe: true, hammer: true, area: area, writable: writable, authority: authority, uncertain: uncertain);

        Assert.Equal(ToolsOfTheTradeStep.None, ToolsOfTheTrade.Decide(facts, out ToolsOfTheTradeWait wait));
        Assert.Equal((ToolsOfTheTradeWait)expectedWait, wait);
        Assert.NotEqual(string.Empty, ToolsOfTheTrade.DescribeWait(wait));
    }

    [Fact]
    public void An_uncertain_handover_outranks_every_other_wait()
    {
        // The worst reason first: a recruitment decided over a guess about who
        // holds a tool is the thing that must never happen.
        ToolsOfTheTrade.Decide(
            Facts(axe: true, hammer: true, uncertain: true, area: false, writable: false, authority: false),
            out ToolsOfTheTradeWait wait);
        Assert.Equal(ToolsOfTheTradeWait.HandoverUncertain, wait);
    }

    [Fact]
    public void Once_recruited_a_lost_tool_never_relocks_him()
    {
        ToolsOfTheTradeFacts noTools = Facts(QuestState.Recruited, employed: true);

        Assert.Equal(ThorsteinQuestStage.Recruited, ToolsOfTheTrade.Stage(noTools));
        Assert.Empty(ToolsOfTheTrade.Objectives(noTools));
        Assert.Equal(ToolsOfTheTradeStep.None, ToolsOfTheTrade.Decide(noTools, out _));
    }

    [Theory]
    [InlineData((int)QuestState.Unstarted)]
    [InlineData((int)QuestState.Collected)]
    [InlineData((int)QuestState.IntroPending)]
    public void A_worker_already_on_the_roster_finishes_the_story_and_recruits_nobody(int quest)
    {
        // Recruited before the quest existed, by another character in this
        // world, or a story save that failed after the register write.
        ToolsOfTheTradeFacts facts = Facts((QuestState)quest, employed: true);

        Assert.Equal(ThorsteinQuestStage.Recruited, ToolsOfTheTrade.Stage(facts));
        Assert.Equal(ToolsOfTheTradeStep.RecordCompletion, ToolsOfTheTrade.Decide(facts, out _));
    }

    [Fact]
    public void A_finished_story_never_re_recruits_a_dismissed_worker()
    {
        // cf_settle dismiss is the player's act; the story does not undo it,
        // even with both tools still on record.
        ToolsOfTheTradeFacts facts = Facts(QuestState.Recruited, employed: false, axe: true, hammer: true);

        Assert.Equal(ToolsOfTheTradeStep.None, ToolsOfTheTrade.Decide(facts, out _));
        Assert.Equal(ThorsteinQuestStage.Recruited, ToolsOfTheTrade.Stage(facts));
    }

    [Fact]
    public void The_story_is_kept_per_character_and_survives_a_reopen()
    {
        string root = Path.Combine(Path.GetTempPath(), "cf-tools-of-the-trade-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CompanionSidecarStore(root);
            var scope = new CompanionScope(ToolsOfTheTrade.Product, new WorldId(42L), new CharacterId(7L));

            CompanionProgress first = CompanionProgress.Open(store, scope, ToolsOfTheTrade.QuestId);
            Assert.Equal(QuestState.Unstarted, first.QuestState);
            first.Advance(QuestTransition.Collect);
            first.Advance(QuestTransition.BeginIntroduction);
            Assert.False(first.HasUnsavedChanges);

            CompanionProgress reopened = CompanionProgress.Open(store, scope, ToolsOfTheTrade.QuestId);
            Assert.Equal(QuestState.IntroPending, reopened.QuestState);

            Assert.Equal(QuestTransitionOutcome.Advanced, reopened.Advance(QuestTransition.Welcome));
            Assert.True(reopened.AnnounceJoin());

            // A replay grants nothing twice: welcome again is satisfied, the
            // join is announced once.
            CompanionProgress again = CompanionProgress.Open(store, scope, ToolsOfTheTrade.QuestId);
            Assert.Equal(QuestTransitionOutcome.AlreadySatisfied, again.Advance(QuestTransition.Welcome));
            Assert.False(again.AnnounceJoin());

            var otherCharacter = new CompanionScope(ToolsOfTheTrade.Product, new WorldId(42L), new CharacterId(8L));
            Assert.Equal(
                QuestState.Unstarted,
                CompanionProgress.Open(store, otherCharacter, ToolsOfTheTrade.QuestId).QuestState);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void The_durable_names_are_pinned()
    {
        // Part of the sidecar's file name and contents: a rename strands every
        // player's story.
        Assert.Equal("concerned-foreman", ToolsOfTheTrade.Product.Value);
        Assert.Equal("tools-of-the-trade", ToolsOfTheTrade.QuestId.Value);
    }

    [Fact]
    public void The_authored_lines_are_the_issues_own()
    {
        Assert.Equal("A roof first. Everything else can wait. — T.", ThorsteinLines.Inscription);
        Assert.StartsWith("Left my tools with the last folk who needed them.", ThorsteinLines.Introduction);
        Assert.StartsWith("Good edge. Sound handle.", ThorsteinLines.Completion);
        Assert.True(new[] { ThorsteinLines.ObjectiveAxe, ThorsteinLines.ObjectiveHammer }.All(line => line.Length > 0));
    }
}
