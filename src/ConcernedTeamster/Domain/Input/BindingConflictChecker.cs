using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Input;

/// <summary>Detects accelerator conflicts (CT-031). Conflicts are reported,
/// never resolved by override: Teamster warns and leaves the binding to the
/// player, so it can never silently steal a key from the game or another
/// mod. Two kinds: EXTERNAL (a Teamster binding collides with a reserved
/// chord — a vanilla control or a configured known-mod default, supplied by
/// the caller so no mod name is invented here) and INTERNAL (two Teamster
/// actions share a chord). Pure and deterministic.</summary>
public static class BindingConflictChecker
{
    public sealed class ExternalConflict
    {
        public ExternalConflict(string actionId, string chord, string reservedLabel)
        {
            ActionId = actionId;
            Chord = chord;
            ReservedLabel = reservedLabel;
        }

        public string ActionId { get; }

        public string Chord { get; }

        /// <summary>What already owns the chord (e.g. "vanilla: Map").</summary>
        public string ReservedLabel { get; }
    }

    public sealed class InternalConflict
    {
        public InternalConflict(string chord, IReadOnlyList<string> actionIds)
        {
            Chord = chord;
            ActionIds = actionIds;
        }

        public string Chord { get; }

        public IReadOnlyList<string> ActionIds { get; }
    }

    /// <summary>Reserved chords owned by the game or another mod, keyed by
    /// normalized chord → human label. The caller builds this from vanilla
    /// defaults and any researched/configured mod binds; the checker invents
    /// none.</summary>
    public static IReadOnlyDictionary<string, string> BuildReserved(
        IEnumerable<KeyValuePair<string, string>> chordLabelPairs)
    {
        var reserved = new Dictionary<string, string>();
        foreach (KeyValuePair<string, string> pair in chordLabelPairs)
        {
            string chord = AcceleratorBinding.Normalize(pair.Key);
            if (chord.Length > 0 && !reserved.ContainsKey(chord))
            {
                reserved[chord] = pair.Value;
            }
        }

        return reserved;
    }

    /// <summary>Teamster bindings whose chord matches a reserved chord.
    /// Unbound actions never conflict.</summary>
    public static IReadOnlyList<ExternalConflict> FindExternalConflicts(
        IEnumerable<AcceleratorBinding> bindings,
        IReadOnlyDictionary<string, string> reserved)
    {
        var conflicts = new List<ExternalConflict>();
        foreach (AcceleratorBinding binding in bindings)
        {
            if (binding.IsBound && reserved.TryGetValue(binding.Chord, out string label))
            {
                conflicts.Add(new ExternalConflict(binding.ActionId, binding.Chord, label));
            }
        }

        return conflicts;
    }

    /// <summary>Chords bound to more than one Teamster action. Grouped by
    /// chord; deterministic (input order preserved within a group).</summary>
    public static IReadOnlyList<InternalConflict> FindInternalConflicts(
        IEnumerable<AcceleratorBinding> bindings)
    {
        var byChord = new Dictionary<string, List<string>>();
        var order = new List<string>();
        foreach (AcceleratorBinding binding in bindings)
        {
            if (!binding.IsBound)
            {
                continue;
            }

            if (!byChord.TryGetValue(binding.Chord, out List<string> actions))
            {
                actions = new List<string>();
                byChord[binding.Chord] = actions;
                order.Add(binding.Chord);
            }

            actions.Add(binding.ActionId);
        }

        var conflicts = new List<InternalConflict>();
        foreach (string chord in order)
        {
            if (byChord[chord].Count > 1)
            {
                conflicts.Add(new InternalConflict(chord, byChord[chord]));
            }
        }

        return conflicts;
    }
}
