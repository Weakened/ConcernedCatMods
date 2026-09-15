using System;
using System.Collections.Generic;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Quest;
using TheConcernedCat.Companions.Unlock;

namespace TheConcernedCat.Companions.Persistence;

/// <summary>The in-memory form of one scope's companion data.
///
/// A sidecar is always addressed by a complete <see cref="CompanionScope"/>,
/// and it stores its own scope so a file that was copied between characters or
/// worlds is detected rather than merged.</summary>
internal sealed class CompanionSidecar
{
    private readonly Dictionary<string, CompanionQuestRecord> _quests =
        new Dictionary<string, CompanionQuestRecord>(StringComparer.Ordinal);
    private readonly List<string> _order = new List<string>();
    private readonly List<string> _forwardLines = new List<string>();

    public CompanionSidecar(CompanionScope scope)
    {
        if (!scope.IsComplete)
        {
            throw new ArgumentException(
                "A companion sidecar needs a resolved product, world, and character.", nameof(scope));
        }

        Scope = scope;
    }

    public CompanionScope Scope { get; }

    /// <summary>True when a save is warranted.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>True when this build must not overwrite the file it came from:
    /// a newer schema wrote it, so a downgrade would destroy data it cannot
    /// even read.</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>True when the file carried structured data from a newer build:
    /// an unknown row kind, a quest stage only a later schema defines, or a
    /// whole file this build may not rewrite. It is evidence that the player
    /// has used the product before, which the unlock policy honours - an
    /// unreadable future stage must never cost someone access they already had.
    ///
    /// Merely malformed rows do not set this. Garbage is carried so it is not
    /// destroyed, but it is not treated as proof of anything.</summary>
    public bool HasForwardData { get; private set; }

    /// <summary>Verbatim lines this build did not consume, re-emitted on save
    /// so a single run of an older build does not erase them.</summary>
    public IReadOnlyList<string> ForwardLines => _forwardLines;

    public IReadOnlyList<CompanionQuestRecord> Quests
    {
        get
        {
            var ordered = new List<CompanionQuestRecord>(_order.Count);
            foreach (string key in _order)
            {
                ordered.Add(_quests[key]);
            }

            return ordered;
        }
    }

    /// <summary>The recorded reason this scope has feature access, or
    /// <see cref="UnlockReason.NotUnlocked"/> when none was ever written.
    ///
    /// This is stored separately from quest progress because the two answer
    /// different questions. Quest progress is "did this character meet the
    /// companion"; this is "may this character use the tools". A returning
    /// player gets the second without the first, and keeps it afterwards no
    /// matter what happens to their beds, saves, settings, or companion.</summary>
    public UnlockReason GrantedReason { get; private set; } = UnlockReason.NotUnlocked;

    /// <summary>True when the file carried an unlock row this build did not
    /// consume. A newer build owns that decision, so this one will not write a
    /// competing row.</summary>
    public bool HasCarriedUnlockRow { get; private set; }

    /// <summary>Writes down a grant decided from outside the sidecar.
    /// Monotonic: the first recorded reason wins forever, so later sessions
    /// read the grant back instead of re-deriving it from evidence that may
    /// have since moved or been deleted.</summary>
    public bool RecordUnlockGrant(UnlockReason reason)
    {
        if (reason == UnlockReason.NotUnlocked || HasCarriedUnlockRow || IsReadOnly)
        {
            return false;
        }

        if (GrantedReason != UnlockReason.NotUnlocked)
        {
            return false;
        }

        GrantedReason = reason;
        IsDirty = true;
        return true;
    }

    public bool TryGetQuest(QuestId questId, out CompanionQuestRecord record)
    {
        return _quests.TryGetValue(questId.Value, out record!);
    }

    /// <summary>Returns the record for <paramref name="questId"/>, creating an
    /// unstarted one if needed. Creating a record is not itself progress, so it
    /// does not mark the sidecar dirty.</summary>
    public CompanionQuestRecord GetOrCreate(QuestId questId)
    {
        if (questId.IsEmpty)
        {
            throw new ArgumentException("A quest record needs a valid quest id.", nameof(questId));
        }

        if (_quests.TryGetValue(questId.Value, out CompanionQuestRecord? existing))
        {
            return existing!;
        }

        var record = new CompanionQuestRecord(questId, QuestState.Unstarted);
        _quests.Add(questId.Value, record);
        _order.Add(questId.Value);
        return record;
    }

    /// <summary>Applies a transition and marks the sidecar dirty only when
    /// something actually advanced.</summary>
    public QuestTransitionOutcome Apply(QuestId questId, QuestTransition transition)
    {
        CompanionQuestRecord record = GetOrCreate(questId);
        QuestTransitionOutcome outcome = record.Apply(transition);
        if (outcome == QuestTransitionOutcome.Advanced)
        {
            IsDirty = true;
        }

        return outcome;
    }

    /// <summary>Records that a collectible presentation was retired. Call this
    /// only after the state that justified it has been saved.</summary>
    public bool MarkPresentationRetired(QuestId questId)
    {
        CompanionQuestRecord record = GetOrCreate(questId);
        if (!record.MarkPresentationRetired())
        {
            return false;
        }

        IsDirty = true;
        return true;
    }

    /// <summary>True when any quest in this scope has been finished. This is
    /// the evidence the unlock policy reads.</summary>
    public bool HasAnyCompletedQuest()
    {
        foreach (string key in _order)
        {
            if (_quests[key].IsComplete)
            {
                return true;
            }
        }

        return false;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    internal void MarkReadOnly()
    {
        IsReadOnly = true;
        HasForwardData = true;
    }

    /// <summary>Keeps a line this build did not consume. <paramref name="isForwardData"/>
    /// distinguishes "a newer build wrote this" from "this row is malformed".</summary>
    internal void AddCarriedLine(string line, bool isForwardData)
    {
        _forwardLines.Add(line);
        if (isForwardData)
        {
            HasForwardData = true;
        }
    }

    internal void RestoreGrant(UnlockReason reason)
    {
        if (GrantedReason == UnlockReason.NotUnlocked)
        {
            GrantedReason = reason;
        }
    }

    internal void MarkCarriedUnlockRow()
    {
        HasCarriedUnlockRow = true;
    }

    /// <summary>Adds a record read from disk. Returns false when a record for
    /// that quest already exists, so the codec can carry the loser rather than
    /// discard it.</summary>
    internal bool Restore(CompanionQuestRecord record)
    {
        if (_quests.ContainsKey(record.QuestId.Value))
        {
            return false;
        }

        _quests.Add(record.QuestId.Value, record);
        _order.Add(record.QuestId.Value);
        return true;
    }
}
