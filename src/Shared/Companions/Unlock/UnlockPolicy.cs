using TheConcernedCat.Companions.Quest;

namespace TheConcernedCat.Companions.Unlock;

/// <summary>Decides feature access for one scope.
///
/// The policy is deliberately biased: every uncertain input resolves to
/// unlocked. Wrongly unlocking shows a returning player the tools they already
/// had; wrongly locking takes away tools somebody has been using for months
/// because a file moved. Only one input - a fresh character with no evidence of
/// any kind - produces a locked result.
///
/// Access is also monotonic. The first non-quest grant is written to the
/// sidecar, and from then on it is read back rather than re-derived, so a
/// deleted atlas, a changed setting, a lost bed, a death, or a hidden companion
/// can never revoke it.</summary>
internal static class UnlockPolicy
{
    /// <summary>Resolves access.</summary>
    /// <param name="questState">The introduction's persisted stage.</param>
    /// <param name="persistedGrantReason">A grant recorded in an earlier
    /// session, or <see cref="UnlockReason.NotUnlocked"/> when there is
    /// none.</param>
    /// <param name="evidence">What the product found of its own prior data for
    /// this character and world.</param>
    /// <param name="companionDataUnreadable">True when the sidecar could not be
    /// read, or was written by a newer build.</param>
    /// <param name="toolsOnlyPreference">True when the player has asked for
    /// tools without the story.</param>
    public static UnlockDecision Decide(
        QuestState questState,
        UnlockReason persistedGrantReason,
        LegacyEvidence evidence,
        bool companionDataUnreadable,
        bool toolsOnlyPreference)
    {
        bool questComplete = QuestStateMachine.IsComplete(questState);

        // The story is offered whenever the introduction is unfinished and the
        // player has not asked to skip it. This is independent of access: a
        // grandfathered player keeps their tools and may still meet the
        // companion.
        bool offerIntroduction = !questComplete && !toolsOnlyPreference;
        bool offerToolsOnly = !questComplete;

        if (persistedGrantReason != UnlockReason.NotUnlocked)
        {
            return new UnlockDecision(
                true, UnlockReason.PreviouslyGranted, false, offerIntroduction, offerToolsOnly);
        }

        if (questComplete)
        {
            return new UnlockDecision(
                true, UnlockReason.QuestCompleted, false, false, false);
        }

        if (toolsOnlyPreference)
        {
            // Worth writing down: the player may turn the preference back off
            // later, and that must not take the tools away again.
            return new UnlockDecision(
                true, UnlockReason.ToolsOnlyPreference, true, false, offerToolsOnly);
        }

        if (evidence == LegacyEvidence.Present)
        {
            return new UnlockDecision(
                true, UnlockReason.ExistingUserData, true, offerIntroduction, offerToolsOnly);
        }

        if (evidence == LegacyEvidence.Ambiguous)
        {
            return new UnlockDecision(
                true, UnlockReason.AmbiguousLegacyEvidence, true, offerIntroduction, offerToolsOnly);
        }

        if (companionDataUnreadable)
        {
            // Persisting matters most in exactly this case. An unreadable file
            // gets moved aside, so the next session finds no file at all and
            // would see a brand new player - taking away the access this
            // session just granted. Writing the grant down now is what prevents
            // that. Where the file genuinely cannot be written (a newer schema,
            // another character's data), the store refuses the save and the
            // grant is re-derived next session from the same file condition.
            return new UnlockDecision(
                true, UnlockReason.DataUnreadable, true, offerIntroduction, offerToolsOnly);
        }

        return new UnlockDecision(
            false, UnlockReason.NotUnlocked, false, offerIntroduction, offerToolsOnly);
    }
}
