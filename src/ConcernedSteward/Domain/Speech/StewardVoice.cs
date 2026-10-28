using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedSteward.Domain.Speech;

/// <summary>How a line is shown. <see cref="Aside"/> is zero, so a line nobody
/// weighed is the quiet kind.</summary>
internal enum SpeechWeight
{
    /// <summary>Queued in the corner of the screen, one after another, and kept
    /// in the message log.</summary>
    Aside = 0,

    /// <summary>The one line of the moment, in the middle of the screen. Used
    /// for the discovery and for very little else.</summary>
    Important = 1,
}

/// <summary>What Sunniva says to the player, as opposed to what this mod writes
/// in its log.
///
/// <b>Why this exists (#382).</b> Until it did, every sentence she had went to
/// the BepInEx log and nowhere else: the flint and steel, the beats of her
/// introduction, and "she needs resin". A player in the game saw none of it, so
/// "no free fuel - she says what she needs" was true of a file nobody reads.
/// This is the seam between the two: the domain decides what she says and when,
/// and the runtime decides how it reaches a screen.
///
/// <b>Throttled by subject, not by sentence.</b> A shortage is re-measured on
/// every scan, and the number in it moves as the fires burn down; a throttle
/// keyed on the exact sentence would say "I need 40 resin", "I need 41 resin",
/// "I need 42 resin" a quarter of a minute apart. So a caller names the subject
/// - <c>short:$item_resin</c> - and the subject is said at most once per
/// cooldown, whatever its latest wording.
///
/// <b>Story beats are not throttled.</b> They are once-ever by construction (the
/// quest refuses to move twice), so throttling them would only be a way to lose
/// one.
///
/// Never throws. A line that could not be shown is a lost line, never a lost
/// round.</summary>
internal sealed class StewardVoice
{
    /// <summary>Five minutes between two mentions of the same thing. Long
    /// enough that a player who is fetching the resin is not told again on the
    /// way to the chest; short enough that a player who walked away and came
    /// back hears it.</summary>
    internal const float DefaultCooldownSeconds = 300f;

    /// <summary>The most subjects remembered at once. Subjects are few - a
    /// shortage per fuel kind, the lights she may not touch, a stop - so this is
    /// a ceiling on a defect rather than a working size.</summary>
    private const int MaxSubjects = 64;

    private readonly Action<string, SpeechWeight> _sink;
    private readonly float _cooldown;
    private readonly Dictionary<string, float> _lastSaid =
        new Dictionary<string, float>(StringComparer.Ordinal);

    internal StewardVoice(Action<string, SpeechWeight> sink, float cooldownSeconds = DefaultCooldownSeconds)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _cooldown = cooldownSeconds > 0f && !float.IsInfinity(cooldownSeconds)
            ? cooldownSeconds
            : DefaultCooldownSeconds;
    }

    /// <summary>Lines handed to the screen so far. For tests and the status
    /// command; never used to decide anything.</summary>
    internal int LinesSpoken { get; private set; }

    /// <summary>Says <paramref name="line"/> about <paramref name="subject"/>,
    /// unless that subject was mentioned within the cooldown.</summary>
    /// <returns>True when the line was handed to the screen.</returns>
    internal bool Say(string subject, string line, float now, SpeechWeight weight = SpeechWeight.Aside)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        string key = subject ?? string.Empty;
        if (_lastSaid.TryGetValue(key, out float last)
            && now >= last
            && now - last < _cooldown)
        {
            return false;
        }

        if (_lastSaid.Count >= MaxSubjects && !_lastSaid.ContainsKey(key))
        {
            _lastSaid.Clear();
        }

        _lastSaid[key] = now;
        return Emit(line, weight);
    }

    /// <summary>Says every line of a story beat, in order, with no throttle.
    /// </summary>
    internal void Narrate(IReadOnlyList<string>? lines, SpeechWeight firstLineWeight = SpeechWeight.Aside)
    {
        if (lines == null)
        {
            return;
        }

        for (int index = 0; index < lines.Count; index++)
        {
            // The first line of a beat may be the important one; the rest are
            // always queued, so a centre-screen line is never overwritten by the
            // line after it before anybody could read it.
            Emit(lines[index], index == 0 ? firstLineWeight : SpeechWeight.Aside);
        }
    }

    /// <summary>The subject has stopped being true. The next time it becomes
    /// true it is said at once rather than waiting out the cooldown of a
    /// previous, different occasion.</summary>
    internal void Forget(string subject) => _lastSaid.Remove(subject ?? string.Empty);

    /// <summary>Forgets every subject. Called when a world unloads: what was
    /// said in one world is not a reason to stay quiet in the next.</summary>
    internal void Reset() => _lastSaid.Clear();

    private bool Emit(string? line, SpeechWeight weight)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        try
        {
            _sink(line!, weight);
            LinesSpoken++;
            return true;
        }
        catch (Exception)
        {
            // A line that could not be shown is lost, and only the line.
            return false;
        }
    }
}
