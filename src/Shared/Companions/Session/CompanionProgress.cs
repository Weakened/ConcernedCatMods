using System;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Unlock;

namespace TheConcernedCat.Companions.Session;

/// <summary>One character's companion state for one session: the sidecar, the
/// quest, and the unlock decision, kept in agreement.
///
/// This is the seam a game adapter talks to. It exists mainly to own one
/// ordering rule that is easy to get wrong and expensive to get wrong:
/// <b>progress reaches disk before its presentation is removed.</b> A build that
/// takes the collectible out of the world first and saves afterwards will,
/// sooner or later, take it out and then fail to save - leaving a player with no
/// collectible, no companion, and no way back into the introduction. So
/// <see cref="TryRetirePresentation"/> refuses while anything is unsaved.
///
/// The second rule it owns is that a grant decided from outside the sidecar is
/// written down immediately, which is what turns "we worked out that you are an
/// existing user" into something that survives the evidence going away.</summary>
internal sealed class CompanionProgress
{
    private readonly CompanionSidecarStore _store;
    private readonly CompanionSidecar _sidecar;
    private readonly QuestId _questId;
    private readonly LegacyEvidence _evidence;
    private bool _toolsOnlyPreference;

    private CompanionProgress(
        CompanionSidecarStore store,
        CompanionSidecar sidecar,
        QuestId questId,
        SidecarLoadOutcome loadOutcome,
        string? notice,
        LegacyEvidence evidence,
        bool toolsOnlyPreference)
    {
        _store = store;
        _sidecar = sidecar;
        _questId = questId;
        _evidence = evidence;
        _toolsOnlyPreference = toolsOnlyPreference;
        LoadOutcome = loadOutcome;
        Notice = notice;
    }

    /// <summary>Opens a character's companion state and resolves access.</summary>
    /// <param name="evidence">What the product found of its own prior data for
    /// this character. Products answer this themselves; the shared layer has no
    /// idea what a given product's old data looks like.</param>
    public static CompanionProgress Open(
        CompanionSidecarStore store,
        CompanionScope scope,
        QuestId questId,
        LegacyEvidence evidence = LegacyEvidence.None,
        bool toolsOnlyPreference = false)
    {
        if (store == null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        CompanionSidecarStore.LoadReport report = store.Load(scope);
        var progress = new CompanionProgress(
            store, report.Sidecar, questId, report.Outcome, report.Notice,
            evidence, toolsOnlyPreference);

        progress.Resolve();
        return progress;
    }

    public CompanionScope Scope => _sidecar.Scope;

    public SidecarLoadOutcome LoadOutcome { get; }

    /// <summary>An actionable sentence for the player, or null. Set when the
    /// data file needed explaining, and replaced when a save is refused.</summary>
    public string? Notice { get; private set; }

    public UnlockDecision Decision { get; private set; }

    public bool IsUnlocked => Decision.IsUnlocked;

    public QuestState QuestState => _sidecar.TryGetQuest(_questId, out CompanionQuestRecord record)
        ? record.State
        : Quest.QuestState.Unstarted;

    public bool HasCompanion => QuestStateMachine.HasCompanion(QuestState);

    /// <summary>True when the collectible belongs in the world right now.
    /// Requires both that the introduction is unfinished and that the player has
    /// not asked to skip it.</summary>
    public bool ShouldPresentCollectible =>
        QuestStateMachine.ShouldPresentCollectible(QuestState) && Decision.ShouldPresentIntroduction;

    /// <summary>True once the collectible has been taken out of the world.</summary>
    public bool PresentationRetired =>
        _sidecar.TryGetQuest(_questId, out CompanionQuestRecord record) && record.PresentationRetired;

    public bool CanReplayStory => QuestStateMachine.CanReplayStory(QuestState);

    /// <summary>True when there is unsaved progress. Should be false whenever
    /// the player is about to be shown a consequence of that progress.</summary>
    public bool HasUnsavedChanges => _sidecar.IsDirty;

    /// <summary>Applies a quest transition and saves if anything advanced.
    ///
    /// Saving inline rather than on a later tick is deliberate: every transition
    /// here is a rare, player-visible moment, and the cost of one small file
    /// write is nothing next to losing the moment to a crash.</summary>
    public QuestTransitionOutcome Advance(QuestTransition transition)
    {
        QuestTransitionOutcome outcome = _sidecar.Apply(_questId, transition);
        if (outcome == QuestTransitionOutcome.Advanced)
        {
            Save();
            Resolve();
        }

        return outcome;
    }

    /// <summary>Records that the collectible has been removed from the world.
    ///
    /// Refuses while the introduction is unfinished or anything is unsaved, so
    /// the world only ever loses the collectible <i>after</i> the reason for
    /// losing it is on disk. If the process dies between the two, the flag is
    /// still false and the removal simply runs again next session.</summary>
    public bool TryRetirePresentation()
    {
        if (!QuestStateMachine.IsComplete(QuestState))
        {
            return false;
        }

        if (_sidecar.IsDirty)
        {
            return false;
        }

        if (PresentationRetired)
        {
            return false;
        }

        _sidecar.MarkPresentationRetired(_questId);
        Save();
        return true;
    }

    /// <summary>Applies a change to the tools-only preference. Turning it on
    /// finishes the introduction; turning it off never takes access away,
    /// because the grant was recorded when it went on.</summary>
    public void SetToolsOnlyPreference(bool enabled)
    {
        _toolsOnlyPreference = enabled;
        if (enabled)
        {
            Advance(QuestTransition.Skip);
        }

        Resolve();
    }

    /// <summary>Re-resolves access, persisting a grant when one was newly
    /// decided from outside the sidecar.
    ///
    /// Safe to call as often as needed: once a grant exists in the sidecar the
    /// policy short-circuits on it, so re-deriving never changes an answer that
    /// has already been given to the player.</summary>
    private void Resolve()
    {
        // "Unreadable" covers more than a sidecar this build refuses to write.
        // A file that was quarantined for being corrupt is gone by the time the
        // next session looks, so the fact that it existed has to be honoured
        // now, while it is still known.
        bool unreadable = _sidecar.IsReadOnly
            || _sidecar.HasForwardData
            || SidecarLoadOutcomes.IndicatesPriorData(LoadOutcome);

        UnlockDecision decision = UnlockPolicy.Decide(
            QuestState,
            _sidecar.GrantedReason,
            _evidence,
            unreadable,
            _toolsOnlyPreference);

        if (decision.ShouldPersistGrant && _sidecar.RecordUnlockGrant(decision.Reason))
        {
            Save();
        }

        Decision = decision;
    }

    private void Save()
    {
        CompanionSidecarStore.SaveReport report = _store.Save(_sidecar);
        if (!report.Saved && report.Notice != null)
        {
            Notice = report.Notice;
        }
    }
}
