using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using TheConcernedCat.Companions.Dialogue;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Reads which biomes <b>this</b> character has seen, for the dialogue
/// gate.
///
/// Three properties matter more than the mechanism.
///
/// It is <b>local only</b>. The set comes from <c>Player.m_localPlayer</c> and
/// nowhere else — never a peer, never a shared map, never a world flag. Another
/// player's travels must not put words in Hulgi's mouth, because a spoiler
/// cannot be taken back.
///
/// It is <b>read-only</b>. The field is a live game collection; it is copied
/// out and never added to, removed from, or held.
///
/// It <b>fails closed</b>. The audit confirmed the field is a
/// <c>HashSet&lt;string&gt;</c> but could not establish the spellings this
/// build puts in it. If the read fails, or the spellings do not match, the
/// result is an empty context: no biome-gated line is eligible, Hulgi says
/// something ungated, and a player notices nothing. Guessing would be the one
/// failure mode with no recovery.</summary>
internal sealed class KnownBiomeReader
{
    private const string FieldName = "m_knownBiome";

    private readonly ManualLogSource _log;
    private bool _loggedUnavailable;
    private string _lastObserved = "<not read yet>";

    public KnownBiomeReader(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>The spellings this build actually produced, for the console
    /// tool. This is how the audit's open row about the exact
    /// <c>m_knownBiome</c> strings gets closed by observation.</summary>
    public string LastObserved => _lastObserved;

    public DialogueContext Read()
    {
        try
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return DialogueContext.Empty;
            }

            FieldInfo? field = typeof(Player).GetField(
                FieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                NoteUnavailable("the field is not present on this build");
                return DialogueContext.Empty;
            }

            if (field.GetValue(player) is not IEnumerable raw)
            {
                NoteUnavailable("the field is not a readable collection on this build");
                return DialogueContext.Empty;
            }

            var names = new List<string>();
            foreach (object? entry in raw)
            {
                if (entry is string name && name.Length > 0)
                {
                    names.Add(name);
                }
                else if (entry != null)
                {
                    // A build that stores enum values rather than names still
                    // has a usable spelling in ToString().
                    string text = entry.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        names.Add(text);
                    }
                }
            }

            _lastObserved = names.Count == 0 ? "<none yet>" : string.Join(", ", names.ToArray());
            return new DialogueContext(names);
        }
        catch (Exception exception)
        {
            NoteUnavailable(SafeLogText.Brief(exception));
            return DialogueContext.Empty;
        }
    }

    private void NoteUnavailable(string reason)
    {
        _lastObserved = "<unavailable: " + reason + ">";
        if (_loggedUnavailable)
        {
            return;
        }

        _loggedUnavailable = true;
        _log.LogInfo(
            "The companion cannot read which biomes this character has visited on this build, so his " +
            "place-specific remarks are switched off and he talks about other things. Saying something " +
            $"about a place you have not been would be worse than saying nothing: {reason}.");
    }
}
