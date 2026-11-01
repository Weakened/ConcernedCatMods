using System;
using System.Collections.Generic;
using System.IO;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Session;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Recruitment;
using TheConcernedCat.Settlement.Register;
using TheConcernedCat.Settlement.Tools;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>"Tools of the Trade" in a running game (#295, slice S1): this
/// character's story record, and the one moment it recruits Thorstein.
///
/// Every decision is <see cref="ToolsOfTheTrade"/>'s, which is game-free and
/// tested. This class resolves which character and world are meant, opens the
/// shared companion sidecar for them, reads the register and the tool ledger,
/// and carries out the one step the decision names.
///
/// <b>What it grants, and what it does not.</b> It moves no item: the axe and
/// hammer reach him through the existing handover (<c>cf_settle give</c>).
/// Its only act on the settlement is the register's own recruitment, with the
/// checks that act already makes (authority, a readable record, a settlement
/// area). It writes nothing into the world. Examining the carpenter's square
/// (slice S2, Concerned NPC's shared keepsake) runs the same
/// <c>cf_settle quest examine</c> this class answers.</summary>
internal sealed class ThorsteinQuest
{
    private readonly CompanionSidecarStore _store;
    private readonly Func<WorkerId> _worker;
    private readonly Func<bool> _hasAuthority;
    private readonly Action<string> _log;
    private CompanionProgress? _progress;

    internal ThorsteinQuest(Func<WorkerId> worker, Func<bool> hasAuthority, Action<string> log)
        : this(Path.Combine(ForemanPaths.Root, "companions"), worker, hasAuthority, log)
    {
    }

    internal ThorsteinQuest(string root, Func<WorkerId> worker, Func<bool> hasAuthority, Action<string> log)
    {
        _store = new CompanionSidecarStore(root);
        _worker = worker;
        _hasAuthority = hasAuthority;
        _log = log;
    }

    /// <summary>Drops the open record when a world goes away.</summary>
    internal void Forget() => _progress = null;

    /// <summary><c>cf_settle quest [status|examine]</c>.</summary>
    internal string Execute(SettlementRegister register, SettlementJournal journal, Func<string?> saveRecords,
        string[]? args)
    {
        string verb = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        if (!TryOpen(out CompanionProgress progress, out string reason))
        {
            return reason;
        }

        switch (verb)
        {
            case "status":
                return Describe(progress, register, journal) + Settle(progress, register, journal, saveRecords);
            case "examine":
                return Examine(progress, register, journal, saveRecords);
            default:
                return "Usage: cf_settle quest [status|examine]. examine does what examining the carpenter's " +
                    "square near your bed does.";
        }
    }

    /// <summary>This character's stage, cheaply, for deciding whether the
    /// carpenter's square belongs in the world (#295 S2). Reads the story and the
    /// roster only - no journal replay, since the stage does not depend on the
    /// tools. Null when the character or world cannot be identified.</summary>
    internal ThorsteinQuestStage? StageFor(SettlementRegister register, SettlementJournal journal)
    {
        if (!TryOpen(out CompanionProgress progress, out _))
        {
            return null;
        }

        var facts = new ToolsOfTheTradeFacts(
            progress.QuestState,
            register.Employs(_worker()),
            axeHeld: false,
            hammerHeld: false,
            handoverUncertain: false,
            register.HasSettlementArea,
            !register.IsReadOnly && !journal.IsReadOnly,
            _hasAuthority(),
            rosterSaved: !register.IsDirty);
        return ToolsOfTheTrade.Stage(facts);
    }

    private readonly TheConcernedCat.Companions.Dialogue.DialogueRotation _remarks = ThorsteinTalk.NewRotation();

    /// <summary>#295 S3: what he says to a press of the use key on his figure.
    /// Reads the real tool ledger, on the press only. Null for nothing to say.
    /// </summary>
    internal string? Talk(SettlementRegister register, SettlementJournal journal)
    {
        if (!TryOpen(out CompanionProgress progress, out _))
        {
            return null;
        }

        ToolsOfTheTradeFacts facts = Facts(progress, register, journal);
        ToolsOfTheTrade.Decide(facts, out ToolsOfTheTradeWait wait);
        return ThorsteinTalk.Line(ToolsOfTheTrade.Stage(facts), facts.AxeHeld, facts.HammerHeld, _remarks, wait);
    }

    /// <summary>Called after every tool handover: if that was the second tool,
    /// this is the moment he joins. Empty when nothing happened.</summary>
    internal string AfterHandover(SettlementRegister register, SettlementJournal journal, Func<string?> saveRecords)
    {
        if (!TryOpen(out CompanionProgress progress, out _))
        {
            return string.Empty;
        }

        return Settle(progress, register, journal, saveRecords);
    }

    private string Examine(
        CompanionProgress progress, SettlementRegister register, SettlementJournal journal, Func<string?> saveRecords)
    {
        ToolsOfTheTradeFacts before = Facts(progress, register, journal);
        if (ToolsOfTheTrade.Stage(before) == ThorsteinQuestStage.Recruited)
        {
            return "\"" + ThorsteinLines.Inscription + "\" Thorstein already works for you; the square " +
                "grants nothing twice." + Settle(progress, register, journal, saveRecords);
        }

        progress.Advance(QuestTransition.Collect);
        progress.Advance(QuestTransition.BeginIntroduction);
        if (progress.HasUnsavedChanges)
        {
            return "The square could not be saved, so after a reload it will need examining again: " +
                (progress.Notice ?? "the companion file was not written.");
        }

        return "\"" + ThorsteinLines.Inscription + "\"\nThorstein: \"" + ThorsteinLines.Introduction + "\"\n" +
            Describe(progress, register, journal) + Settle(progress, register, journal, saveRecords);
    }

    /// <summary>Carries out whatever <see cref="ToolsOfTheTrade.Decide"/> says.
    /// The order of the writes is <see cref="ToolsOfTheTradeFlow"/>'s, which is
    /// game-free and tested; this only answers its questions.</summary>
    private string Settle(
        CompanionProgress progress, SettlementRegister register, SettlementJournal journal, Func<string?> saveRecords)
    {
        string result = ToolsOfTheTradeFlow.Settle(new Ports(this, progress, register, journal, saveRecords));
        if (result.IndexOf(ThorsteinLines.Completion, StringComparison.Ordinal) >= 0)
        {
            _log("Thorstein joined (Tools of the Trade).");
        }

        return result;
    }

    private sealed class Ports : IToolsOfTheTradePorts
    {
        private readonly ThorsteinQuest _owner;
        private readonly CompanionProgress _progress;
        private readonly SettlementRegister _register;
        private readonly SettlementJournal _journal;
        private readonly Func<string?> _saveRecords;

        internal Ports(ThorsteinQuest owner, CompanionProgress progress, SettlementRegister register,
            SettlementJournal journal, Func<string?> saveRecords)
        {
            _owner = owner;
            _progress = progress;
            _register = register;
            _journal = journal;
            _saveRecords = saveRecords;
        }

        public ToolsOfTheTradeFacts Read() => _owner.Facts(_progress, _register, _journal);

        public bool TryRecruit(out string refusal)
        {
            RecruitmentResult result = _register.Recruit(_owner._worker(), WorkerRoles.Labourer, _owner._hasAuthority());
            refusal = result.IsRecruited ? string.Empty : result.Describe();
            return result.IsRecruited;
        }

        public bool TrySaveRoster(out string? notice)
        {
            notice = _saveRecords();
            return !_register.IsDirty;
        }

        public bool TryRecordWelcome(out string? notice)
        {
            _progress.Advance(QuestTransition.Welcome);
            notice = _progress.Notice;
            return !_progress.HasUnsavedChanges;
        }

        public bool TryAnnounceJoin() => _progress.AnnounceJoin();
    }

    private string Describe(CompanionProgress progress, SettlementRegister register, SettlementJournal journal)
    {
        ToolsOfTheTradeFacts facts = Facts(progress, register, journal);
        switch (ToolsOfTheTrade.Stage(facts))
        {
            case ThorsteinQuestStage.NotMet:
                return "Tools of the Trade: not begun. A worn carpenter's square lies near your bed while the " +
                    "settlement runtime is on; examine it (or cf_settle quest examine).";

            case ThorsteinQuestStage.WaitingForTools:
                var lines = new List<string> { "Tools of the Trade:" };
                foreach (ToolsOfTheTradeObjective objective in ToolsOfTheTrade.Objectives(facts))
                {
                    lines.Add("  " + objective);
                }

                lines.Add("  Hold the tool and use cf_settle give axe|hammer next to him.");
                return string.Join("\n", lines.ToArray());

            default:
                return "Tools of the Trade: complete. Thorstein " +
                    (facts.Employed ? "works for this settlement." : "is not on this settlement's roster now; " +
                        "taking him on again is cf_settle recruit " + _worker().Value + ".");
        }
    }

    private ToolsOfTheTradeFacts Facts(CompanionProgress progress, SettlementRegister register, SettlementJournal journal)
    {
        WorkerId worker = _worker();
        ToolLedger ledger = journal.Replay().Tools;
        return new ToolsOfTheTradeFacts(
            progress.QuestState,
            register.Employs(worker),
            ledger.TryGetHeld(worker, ToolKind.Axe, out _),
            ledger.TryGetHeld(worker, ToolKind.Hammer, out _),
            ledger.HasUncertainHandover(worker),
            register.HasSettlementArea,
            !register.IsReadOnly && !journal.IsReadOnly,
            _hasAuthority(),
            rosterSaved: !register.IsDirty);
    }

    private bool TryOpen(out CompanionProgress progress, out string reason)
    {
        progress = null!;
        if (!ThorsteinQuestScope.TryResolve(ToolsOfTheTrade.Product, out CompanionScope scope, out reason))
        {
            return false;
        }

        if (_progress == null || !_progress.Scope.Equals(scope))
        {
            _progress = CompanionProgress.Open(_store, scope, ToolsOfTheTrade.QuestId);
            if (_progress.Notice != null)
            {
                _log(_progress.Notice);
            }
        }

        progress = _progress;
        return true;
    }
}

/// <summary>Which character in which world, from the game. The same answer
/// Concerned Cartographer's companion layer uses: the world's id and the
/// player profile's id, and nothing until both exist.</summary>
internal static class ThorsteinQuestScope
{
    internal static bool TryResolve(ProductId product, out CompanionScope scope, out string reason)
    {
        scope = default;
        reason = string.Empty;
        try
        {
            ZNet net = ZNet.instance;
            long world = net != null ? net.GetWorldUID() : 0L;
            PlayerProfile? profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            long player = profile != null ? profile.GetPlayerID() : 0L;
            if (world == 0L || player == 0L)
            {
                reason = "No world or character is loaded yet, so there is no story to read.";
                return false;
            }

            scope = new CompanionScope(product, new WorldId(world), new CharacterId(player));
            if (!scope.IsComplete)
            {
                reason = "This world or character could not be identified, so the story is not read.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            reason = "The world or character could not be read on this build (" + exception.GetType().Name + ").";
            return false;
        }
    }
}
