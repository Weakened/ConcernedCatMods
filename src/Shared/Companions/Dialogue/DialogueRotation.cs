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

    /// <summary>The key used for a whole-catalogue request, which is not any
    /// category's value.</summary>
    private const int WholeCatalogue = -1;

    private readonly DialogueCatalog _catalog;
    private readonly int _suppressionWindow;
    private readonly List<string> _recent = new List<string>();

    /// <summary>One cursor per request scope, each a position in the
    /// CATALOGUE.
    ///
    /// A single shared cursor cannot work, and the reason is worth keeping.
    /// <see cref="TryNext"/> takes a nullable category precisely so a caller can
    /// mix a narrow request with a whole-catalogue one — greet first, then say
    /// something. But a greeting only ever lives at a handful of catalogue
    /// positions, so every greeting call would drag a shared cursor back to one
    /// of them, and the whole-catalogue walk would restart near the front
    /// forever. The back of a twenty-four line catalogue would only be reached
    /// when the recency window happened to push the walk that far, and the
    /// per-character starting offset — the only thing making two characters
    /// hear a different order — would be discarded on the first narrow call.
    ///
    /// Separate cursors let each scope walk its own way through at its own
    /// pace, while the recency window stays shared so the two scopes still
    /// avoid repeating each other.</summary>
    private readonly Dictionary<int, int> _cursors = new Dictionary<int, int>();

    private readonly int _startOffset;

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
        // range instead of rejecting it. Every scope's cursor starts here, so
        // the per-character order survives however the caller mixes requests.
        _startOffset = catalog.Count == 0 ? 0 : (int)(((uint)startOffset) % (uint)catalog.Count);
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

        int scope = category.HasValue ? (int)category.Value : WholeCatalogue;

        DialogueLine? chosen = PickUnsuppressed(eligible, scope);
        if (chosen == null)
        {
            // Everything eligible is inside the window. Release the oldest
            // suppressions until something frees up.
            ReleaseOldestUntilAvailable(eligible);
            chosen = PickUnsuppressed(eligible, scope);
            if (chosen == null)
            {
                chosen = eligible[0];
                AdvanceCursorPast(scope, chosen);
            }
        }

        Remember(chosen);
        line = chosen;
        return true;
    }

    /// <summary>Walks forward from this scope's cursor over the eligible
    /// lines. The cursor is a position in the <b>catalogue</b>, so it stays
    /// meaningful whichever subset this particular call is choosing from.</summary>
    private DialogueLine? PickUnsuppressed(List<DialogueLine> eligible, int scope)
    {
        int start = FirstAtOrAfterCursor(eligible, CursorFor(scope));

        for (int step = 0; step < eligible.Count; step++)
        {
            int index = (start + step) % eligible.Count;
            DialogueLine candidate = eligible[index];
            if (!_recent.Contains(candidate.Key))
            {
                AdvanceCursorPast(scope, candidate);
                return candidate;
            }
        }

        return null;
    }

    private int CursorFor(int scope)
    {
        return _cursors.TryGetValue(scope, out int cursor) ? cursor : _startOffset;
    }

    /// <summary>The index in <paramref name="eligible"/> of the first line
    /// whose catalogue position is at or after the cursor, wrapping to 0.
    /// Eligible lists are built in catalogue order, so this is a scan rather
    /// than a search.</summary>
    private int FirstAtOrAfterCursor(List<DialogueLine> eligible, int cursor)
    {
        for (int index = 0; index < eligible.Count; index++)
        {
            if (_catalog.IndexOf(eligible[index]) >= cursor)
            {
                return index;
            }
        }

        return 0;
    }

    private void AdvanceCursorPast(int scope, DialogueLine chosen)
    {
        if (_catalog.Count == 0)
        {
            _cursors[scope] = 0;
            return;
        }

        int position = _catalog.IndexOf(chosen);
        _cursors[scope] = position < 0 ? 0 : (position + 1) % _catalog.Count;
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
