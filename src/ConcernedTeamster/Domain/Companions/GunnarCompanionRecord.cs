using System;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;

namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>Gunnar's introduction record, not a worker permission or a tool gate.
/// Uses the shared format directly: its fail-open tool unlock policy must never
/// be interpreted as evidence that this character recruited a worker.</summary>
internal sealed class GunnarCompanionRecord
{
    public static readonly CompanionId Identity = new("gunnar");
    public static readonly ProductId Product = new("concerned-teamster");
    public static readonly QuestId Quest = new("a-fair-load");
    public const string DisplayNameKey = "companion.gunnar.name";
    public const string DefaultDisplayName = "Gunnar";
    public const bool PresentationEnabledByDefault = true;

    private readonly CompanionSidecarStore _store;
    private CompanionSidecar _sidecar;
    private readonly bool _readable;
    private bool _active = true;

    private GunnarCompanionRecord(CompanionSidecarStore store, CompanionSidecarStore.LoadReport report)
    {
        _store = store;
        _sidecar = report.Sidecar;
        LoadOutcome = report.Outcome;
        Notice = report.Notice;
        // Even a readable recruited row beside damaged rows is insufficient
        // evidence. Never turn the shared codec's recovery grants into a hire.
        _readable = report.Outcome == SidecarLoadOutcome.Loaded ||
            report.Outcome == SidecarLoadOutcome.Missing;
    }

    public static GunnarCompanionRecord Open(string directory, long world, long character)
    {
        var scope = new CompanionScope(Product, new WorldId(world), new CharacterId(character));
        if (!scope.IsComplete) throw new ArgumentException("A resolved world and character are required.");
        var store = new CompanionSidecarStore(directory);
        return new GunnarCompanionRecord(store, store.Load(scope));
    }

    public CompanionScope Scope => _sidecar.Scope;
    public SidecarLoadOutcome LoadOutcome { get; }
    public string? Notice { get; private set; }
    public QuestState State => _active && _readable && _sidecar.TryGetQuest(Quest, out CompanionQuestRecord record)
        ? record.State : QuestState.Unstarted;
    public bool IsRecruited => _active && _readable && State == QuestState.Recruited;
    public bool CanReplayStory => _active && _readable && QuestStateMachine.CanReplayStory(State);
    public bool ShouldPresentMemento(bool enabled) => enabled && _active && _readable &&
        (State == QuestState.Unstarted || State == QuestState.Discovered);
    public bool ShouldPresentFigure(bool enabled) => enabled && _active && _readable &&
        (State == QuestState.IntroPending || State == QuestState.Recruited);

    public static string ResolveDisplayName(string? configured, Func<string, string?>? translate = null)
    {
        string? localized = translate?.Invoke(DisplayNameKey);
        if (!string.IsNullOrWhiteSpace(localized) && localized != DisplayNameKey) return localized!;
        return string.IsNullOrWhiteSpace(configured) ? DefaultDisplayName : configured!;
    }

    public bool Discover() => Advance(QuestTransition.Discover);
    public bool Collect() => Advance(QuestTransition.Collect);
    public bool BeginIntroduction() => Advance(QuestTransition.BeginIntroduction);
    public bool Skip() => Advance(QuestTransition.Skip);

    /// <summary>Only #460's real-cart assignment adapter may supply acceptance.
    /// Having a cart, prior config, tools-only, or a visible figure is never
    /// acceptance. This persists the story result; it starts no job and grants
    /// no worker authority. Existing work-authority/custody gates still apply.</summary>
    public bool TryRecruitAfterAcceptedCartAssignment(bool assignmentAccepted) =>
        assignmentAccepted && Advance(QuestTransition.Welcome);

    internal void Close() { _active = false; }

    private bool Advance(QuestTransition transition)
    {
        if (!_active || !_readable || _sidecar.IsReadOnly) return false;
        // Work on a detached candidate. A failed save must not expose a hire,
        // hide a keepsake, or announce a result that exists only in memory.
        var candidate = CompanionSidecarCodec.Parse(
            CompanionSidecarCodec.Serialize(_sidecar), Scope).Sidecar;
        QuestTransitionOutcome outcome = candidate.Apply(Quest, transition);
        if (outcome == QuestTransitionOutcome.Rejected) return false;
        if (outcome == QuestTransitionOutcome.AlreadySatisfied) return true;
        CompanionSidecarStore.SaveReport saved = _store.Save(candidate);
        Notice = saved.Notice;
        if (!saved.Saved) return false;
        _sidecar = candidate;
        return true;
    }
}
