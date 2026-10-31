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
        bool authority = true,
        bool rosterSaved = true) =>
        new ToolsOfTheTradeFacts(quest, employed, axe, hammer, uncertain, area, writable, authority, rosterSaved);

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

    [Fact]
    public void An_unsaved_roster_is_never_built_on()
    {
        // Employed in memory only: the story must not be recorded over it.
        ToolsOfTheTradeFacts facts = Facts(QuestState.IntroPending, employed: true, axe: true, hammer: true,
            rosterSaved: false);

        Assert.Equal(ToolsOfTheTradeStep.None, ToolsOfTheTrade.Decide(facts, out ToolsOfTheTradeWait wait));
        Assert.Equal(ToolsOfTheTradeWait.RecordsUnsaved, wait);
    }

    // --- the flow: the order of the writes ----------------------------------------------------

    private sealed class FakePorts : IToolsOfTheTradePorts
    {
        internal QuestState Quest = QuestState.IntroPending;
        internal bool EmployedInMemory;
        internal bool EmployedOnDisk;
        internal bool Axe = true;
        internal bool Hammer = true;
        internal bool Area = true;
        internal bool RosterSaveFails;
        internal bool StorySaveFails;
        internal bool Announced;
        internal readonly System.Collections.Generic.List<string> Writes = new System.Collections.Generic.List<string>();

        public ToolsOfTheTradeFacts Read() => new ToolsOfTheTradeFacts(
            Quest, EmployedInMemory, Axe, Hammer, false, Area, true, true,
            rosterSaved: EmployedInMemory == EmployedOnDisk);

        public bool TryRecruit(out string refusal)
        {
            Writes.Add("recruit");
            refusal = string.Empty;
            EmployedInMemory = true;
            return true;
        }

        public bool TrySaveRoster(out string? notice)
        {
            Writes.Add("save-roster");
            notice = RosterSaveFails ? "disk full" : null;
            if (!RosterSaveFails)
            {
                EmployedOnDisk = EmployedInMemory;
            }

            return !RosterSaveFails;
        }

        public bool TryRecordWelcome(out string? notice)
        {
            Writes.Add("welcome");
            notice = null;
            if (StorySaveFails)
            {
                return false;
            }

            Quest = QuestState.Recruited;
            return true;
        }

        public bool TryAnnounceJoin()
        {
            if (Announced)
            {
                return false;
            }

            Announced = true;
            return true;
        }
    }

    [Fact]
    public void The_roster_reaches_disk_before_the_story_is_recorded()
    {
        var ports = new FakePorts();

        string said = ToolsOfTheTradeFlow.Settle(ports);

        Assert.Equal(new[] { "recruit", "save-roster", "welcome" }, ports.Writes);
        Assert.True(ports.EmployedOnDisk);
        Assert.Equal(QuestState.Recruited, ports.Quest);
        Assert.Contains(ThorsteinLines.Completion, said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_roster_save_records_no_story_now_or_on_the_next_pass()
    {
        var ports = new FakePorts { RosterSaveFails = true };

        string first = ToolsOfTheTradeFlow.Settle(ports);
        string second = ToolsOfTheTradeFlow.Settle(ports);

        Assert.DoesNotContain("welcome", ports.Writes);
        Assert.NotEqual(QuestState.Recruited, ports.Quest);
        Assert.Contains("could not be saved", first, StringComparison.Ordinal);
        Assert.Contains("could not be saved", second, StringComparison.Ordinal);
        Assert.DoesNotContain(ThorsteinLines.Completion, first + second, StringComparison.Ordinal);
    }

    [Fact]
    public void A_roster_save_that_succeeds_on_retry_lets_the_story_finish()
    {
        var ports = new FakePorts { RosterSaveFails = true };
        ToolsOfTheTradeFlow.Settle(ports);

        ports.RosterSaveFails = false;
        string said = ToolsOfTheTradeFlow.Settle(ports);

        Assert.True(ports.EmployedOnDisk);
        Assert.Equal(QuestState.Recruited, ports.Quest);
        Assert.Contains(ThorsteinLines.Completion, said, StringComparison.Ordinal);
        Assert.Equal(1, ports.Writes.FindAll(write => write == "recruit").Count);
    }

    [Fact]
    public void A_failed_story_save_keeps_him_employed_and_says_so()
    {
        var ports = new FakePorts { StorySaveFails = true };

        string said = ToolsOfTheTradeFlow.Settle(ports);

        Assert.True(ports.EmployedOnDisk);
        Assert.NotEqual(QuestState.Recruited, ports.Quest);
        Assert.Contains("taken on", said, StringComparison.Ordinal);
        Assert.False(ports.Announced);
    }

    [Fact]
    public void A_worker_employed_before_the_story_gets_the_record_without_the_speech()
    {
        var ports = new FakePorts
        {
            Quest = QuestState.Unstarted, EmployedInMemory = true, EmployedOnDisk = true, Axe = false, Hammer = false,
        };

        string said = ToolsOfTheTradeFlow.Settle(ports);

        Assert.Equal(new[] { "welcome" }, ports.Writes);
        Assert.DoesNotContain(ThorsteinLines.Completion, said, StringComparison.Ordinal);
        Assert.Contains("already works", said, StringComparison.Ordinal);
    }

    [Fact]
    public void The_join_is_announced_once()
    {
        var ports = new FakePorts();
        string first = ToolsOfTheTradeFlow.Settle(ports);
        string again = ToolsOfTheTradeFlow.Settle(ports);

        Assert.Contains(ThorsteinLines.Completion, first, StringComparison.Ordinal);
        Assert.Equal(string.Empty, again);
        Assert.Equal(1, ports.Writes.FindAll(write => write == "recruit").Count);
    }

    [Fact]
    public void Another_characters_first_look_records_the_story_without_the_speech()
    {
        // Thorstein holds both tools (the ledger is per world), but this
        // character never opened the introduction.
        var ports = new FakePorts { Quest = QuestState.Unstarted, EmployedInMemory = true, EmployedOnDisk = true };

        string said = ToolsOfTheTradeFlow.Settle(ports);

        Assert.Equal(QuestState.Recruited, ports.Quest);
        Assert.DoesNotContain(ThorsteinLines.Completion, said, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsaved_roster_is_not_reported_as_recruited()
    {
        Assert.Equal(
            ThorsteinQuestStage.WaitingForTools,
            ToolsOfTheTrade.Stage(Facts(QuestState.IntroPending, employed: true, axe: true, hammer: true,
                rosterSaved: false)));
    }

    // --- S2: the carpenter's square -----------------------------------------------------------

    [Fact]
    public void The_square_lies_there_only_before_the_introduction_for_the_host_near_home()
    {
        Assert.True(ToolsOfTheTrade.ShowKeepsake(ThorsteinQuestStage.NotMet, true, true, true, true));

        Assert.False(ToolsOfTheTrade.ShowKeepsake(ThorsteinQuestStage.WaitingForTools, true, true, true, true));
        Assert.False(ToolsOfTheTrade.ShowKeepsake(ThorsteinQuestStage.Recruited, true, true, true, true));
        Assert.False(ToolsOfTheTrade.ShowKeepsake(null, true, true, true, true));
        Assert.False(ToolsOfTheTrade.ShowKeepsake(ThorsteinQuestStage.NotMet, false, true, true, true));
        Assert.False(ToolsOfTheTrade.ShowKeepsake(ThorsteinQuestStage.NotMet, true, false, true, true));
        Assert.False(ToolsOfTheTrade.ShowKeepsake(ThorsteinQuestStage.NotMet, true, true, false, true));
        Assert.False(ToolsOfTheTrade.ShowKeepsake(ThorsteinQuestStage.NotMet, true, true, true, false));
    }

    [Fact]
    public void The_square_is_the_shared_keepsake_with_his_words()
    {
        Assert.Equal("Worn carpenter's square", ToolsOfTheTrade.Keepsake.HoverName);
        Assert.Equal("Examine", ToolsOfTheTrade.Keepsake.HoverVerb);
        Assert.Equal("Hammer", ToolsOfTheTrade.Keepsake.PrefabCandidates[0]);
        Assert.Equal(1f, ToolsOfTheTrade.Keepsake.Scale);
    }
}
