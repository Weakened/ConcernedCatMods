using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Quest;

namespace TheConcernedCat.Companions.Persistence;

/// <summary>One quest's persisted progress.
///
/// <see cref="PresentationRetired"/> is deliberately a second flag rather than
/// an extra quest stage. Recruitment must reach disk <i>before</i> the
/// collectible is taken out of the world, so the runtime saves first and
/// retires second; if the process dies between those two steps the flag is
/// still false, the retire step simply runs again on the next load, and the
/// unlock that was already written is never at risk.</summary>
internal sealed class CompanionQuestRecord
{
    /// <summary>Fields a newer build appended to this row. They are carried
    /// through verbatim on save so that running an older build once does not
    /// silently discard progress the newer one recorded.</summary>
    private readonly List<string> _unknownFields = new List<string>();

    public CompanionQuestRecord(QuestId questId, QuestState state)
    {
        QuestId = questId;
        State = state;
    }

    public QuestId QuestId { get; }

    public QuestState State { get; private set; }

    /// <summary>Increments on every accepted advance. Lets a caller detect
    /// that something actually changed without re-comparing whole records, and
    /// gives journal-style replay a tie-break if one is ever added.</summary>
    public int Revision { get; private set; }

    /// <summary>True once the collectible presentation has been removed for
    /// this quest.</summary>
    public bool PresentationRetired { get; private set; }

    /// <summary>True once the player has been told this companion joined their
    /// crew. Told once per character per world, when the companion first
    /// appears after recruitment - and told again after a reset, because a
    /// reset drops the whole record.</summary>
    public bool JoinAnnounced { get; private set; }

    public IReadOnlyList<string> UnknownFields => _unknownFields;

    /// <summary>Applies a transition through <see cref="QuestStateMachine"/>.
    /// The record is the only place state is mutated, so the invariants proved
    /// for the state machine hold for stored data too.</summary>
    public QuestTransitionOutcome Apply(QuestTransition transition)
    {
        QuestTransitionOutcome outcome = QuestStateMachine.TryApply(State, transition, out QuestState next);
        if (outcome == QuestTransitionOutcome.Advanced)
        {
            State = next;
            Revision++;
        }

        return outcome;
    }

    /// <summary>Marks the collectible as removed from the world. Idempotent;
    /// returns true only on the transition that actually changed it.</summary>
    public bool MarkPresentationRetired()
    {
        if (PresentationRetired)
        {
            return false;
        }

        PresentationRetired = true;
        Revision++;
        return true;
    }

    /// <summary>Marks the joined-your-crew notice as given. Idempotent; returns
    /// true only on the call that actually changed it.</summary>
    public bool MarkJoinAnnounced()
    {
        if (JoinAnnounced)
        {
            return false;
        }

        JoinAnnounced = true;
        Revision++;
        return true;
    }

    /// <summary>Restores a record read from disk without running it through the
    /// state machine. Only the codec calls this.</summary>
    internal static CompanionQuestRecord Restore(
        QuestId questId, QuestState state, int revision, bool presentationRetired,
        IEnumerable<string>? unknownFields, bool joinAnnounced = false)
    {
        var record = new CompanionQuestRecord(questId, state)
        {
            Revision = revision < 0 ? 0 : revision,
            PresentationRetired = presentationRetired,
            JoinAnnounced = joinAnnounced,
        };

        if (unknownFields != null)
        {
            foreach (string field in unknownFields)
            {
                record._unknownFields.Add(field ?? string.Empty);
            }
        }

        return record;
    }

    /// <summary>True when this record already grants the product unlock.</summary>
    public bool IsComplete => QuestStateMachine.IsComplete(State);

    public override string ToString()
    {
        return QuestId.Value + "=" + State + " r" + Revision.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
