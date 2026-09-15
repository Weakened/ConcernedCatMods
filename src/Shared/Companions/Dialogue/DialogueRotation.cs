using System;
using System.Collections.Generic;

namespace TheConcernedCat.Companions.Dialogue;

/// <summary>Picks the next line, avoiding recent repeats.
///
/// The selection is deterministic rather than random, for two reasons. A random
/// picker repeats itself far more often than players expect, which is exactly
/// the complaint this class exists to prevent; and a deterministic picker can
/// be tested for the property that actually matters - that a line does not come
/// back until the window has passed.
///
/// A cursor walks the eligible lines in order and a bounded recency window
/// suppresses what was said lately. The starting offset is supplied by the
/// caller (a stable hash of the world and character, say), so two characters do
/// not hear the catalogue in the same order while each one stays reproducible.
///
/// When the window would leave nothing eligible - a small catalogue, or a
/// narrow category - the oldest suppressions are released rather than returning
/// nothing. Saying something slightly sooner than ideal beats a companion who
/// has run out of words.</summary>
internal sealed class DialogueRotation
{
    public const int DefaultSuppressionWindow = 8;

    private readonly DialogueCatalog _catalog;
    private readonly int _suppressionWindow;
    private readonly List<string> _recent = new List<string>();
    private int _cursor;

    public DialogueRotation(
        DialogueCatalog catalog,
        int suppressionWindow = DefaultSuppressionWindow,
        int startOffset = 0)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (suppressionWindow < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suppressionWindow));
        }

        _suppressionWindow = suppressionWindow;

        // A negative or huge offset is a caller's hash, not a bug. Fold it into
        // range instead of rejecting it.
        _cursor = catalog.Count == 0 ? 0 : (int)(((uint)startOffset) % (uint)catalog.Count);
    }

    /// <summary>Lines said recently, newest last.</summary>
    public IReadOnlyList<string> RecentKeys => _recent;

    /// <summary>Selects the next line in <paramref name="category"/>, or from
    /// the whole catalogue when it is null.</summary>
    /// <returns>False when nothing is eligible at all - an empty catalogue, or
    /// a category whose every line is gated behind places this character has
    /// not seen. The caller stays silent; it does not fall back to saying
    /// something inappropriate.</returns>
    public bool TryNext(DialogueCategory? category, DialogueContext context, out DialogueLine line)
    {
        line = null!;
        List<DialogueLine> eligible = _catalog.Eligible(category, context);
        if (eligible.Count == 0)
        {
            return false;
        }

        DialogueLine? chosen = PickUnsuppressed(eligible);
        if (chosen == null)
        {
            // Everything eligible is inside the window. Release the oldest
            // suppressions until something frees up.
            ReleaseOldestUntilAvailable(eligible);
            chosen = PickUnsuppressed(eligible) ?? eligible[0];
        }

        Remember(chosen);
        line = chosen;
        return true;
    }

    /// <summary>Walks forward from the cursor over the eligible lines, so
    /// successive calls move through the catalogue instead of re-testing the
    /// same line.</summary>
    private DialogueLine? PickUnsuppressed(List<DialogueLine> eligible)
    {
        for (int step = 0; step < eligible.Count; step++)
        {
            int index = (_cursor + step) % eligible.Count;
            DialogueLine candidate = eligible[index];
            if (!_recent.Contains(candidate.Key))
            {
                _cursor = (index + 1) % eligible.Count;
                return candidate;
            }
        }

        return null;
    }

    private void ReleaseOldestUntilAvailable(List<DialogueLine> eligible)
    {
        while (_recent.Count > 0)
        {
            _recent.RemoveAt(0);
            foreach (DialogueLine candidate in eligible)
            {
                if (!_recent.Contains(candidate.Key))
                {
                    return;
                }
            }
        }
    }

    private void Remember(DialogueLine chosen)
    {
        _recent.Add(chosen.Key);
        while (_recent.Count > _suppressionWindow)
        {
            _recent.RemoveAt(0);
        }
    }

    /// <summary>Clears the recency window. Called when a session ends, so a new
    /// one does not start mid-window.</summary>
    public void Reset()
    {
        _recent.Clear();
    }
}
