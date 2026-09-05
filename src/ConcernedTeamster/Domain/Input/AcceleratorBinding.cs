using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Input;

/// <summary>A rebindable keyboard/mouse accelerator: an action id and the
/// chord it fires on (CT-031). Chords are normalized (case-insensitive, the
/// modifiers sorted) so "Shift+M" and "m + shift" compare equal, making
/// conflict detection order- and case-independent. An empty chord means the
/// action has no accelerator bound — always valid, never a conflict.</summary>
public readonly struct AcceleratorBinding
{
    public AcceleratorBinding(string actionId, string chord)
    {
        ActionId = actionId ?? string.Empty;
        Chord = Normalize(chord);
    }

    public string ActionId { get; }

    /// <summary>The normalized chord, or empty when unbound.</summary>
    public string Chord { get; }

    public bool IsBound => Chord.Length > 0;

    /// <summary>Normalizes a chord to a canonical form: trims, lowercases,
    /// splits on '+', drops blanks, sorts the keys, and rejoins with '+'.
    /// Deterministic and allocation-bounded.</summary>
    public static string Normalize(string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord))
        {
            return string.Empty;
        }

        string[] parts = chord!.Split('+');
        var keys = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            string key = part.Trim().ToLowerInvariant();
            // Drop blanks and repeats so "m+m" and "m" — physically the same
            // chord — normalize identically and cannot miss a conflict.
            if (key.Length > 0 && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return string.Join("+", keys);
    }
}
