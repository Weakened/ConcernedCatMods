using System;
using TheConcernedCat.ConcernedNPC.Appearance;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Dressing;

/// <summary>Reads the character-creation screen's colour endpoints off the live
/// game, once, and keeps them for the process (lifted from Concerned Foreman's
/// <c>ThorsteinPalette</c>, #298).
///
/// The conversion lerps between colours that are inspector values on the
/// game's customization component. They cannot be derived and must not be
/// invented, so they are read from a live component or not used. That
/// component exists only on the start screen, which is gone by the time a world
/// loads, so a plugin polls from its own Update a few times a second until one
/// read succeeds. Nothing is instantiated, enabled or written; six values are
/// copied out.
///
/// <b>One per process, shared by every consumer.</b> This package is one
/// assembly however many roles use it, so the cache is too: two plugins
/// polling read the same component and keep the same answer. Polling twice is
/// harmless; it stops as soon as either succeeds.</summary>
public static class NpcCustomizationPalette
{
    private const float IntervalSeconds = 0.25f;
    private const float GiveUpSeconds = 600f;

    private static readonly object Gate = new object();
    private static NpcLookPalette _cached = NpcLookPalette.Unobserved;
    private static float _sincePoll;
    private static float _polled;
    private static bool _gaveUp;
    private static bool _saidFallback;

    /// <summary>True once a live read has succeeded this process.</summary>
    public static bool Observed
    {
        get
        {
            lock (Gate)
            {
                return _cached.Observed;
            }
        }
    }

    /// <summary>Called from a plugin's Update in every scene. Cheap: four
    /// lookups a second until it succeeds or gives up, then nothing.</summary>
    public static void Poll(float unscaledDeltaTime, Action<string>? log)
    {
        lock (Gate)
        {
            if (_cached.Observed || _gaveUp)
            {
                return;
            }

            _polled += unscaledDeltaTime;
            _sincePoll += unscaledDeltaTime;
            if (_sincePoll < IntervalSeconds)
            {
                return;
            }

            _sincePoll = 0f;
            if (TryReadLive(out NpcLookPalette fresh))
            {
                _cached = fresh;
                log?.Invoke("Read the game's customization palette for companion colouring.");
                return;
            }

            if (_polled >= GiveUpSeconds)
            {
                _gaveUp = true;
            }
        }
    }

    /// <summary>The live palette if it was ever readable this process, otherwise
    /// the unobserved one, which the colour rule turns into the role's stated
    /// fallback. Says so once per process.</summary>
    public static NpcLookPalette Read(Action<string>? log)
    {
        lock (Gate)
        {
            if (!_cached.Observed && !_saidFallback)
            {
                _saidFallback = true;
                log?.Invoke(
                    "The game's customization palette was not read this session (it is only on the character " +
                    "screen), so companion hair uses each one's stated fallback colour and skin is the model's own.");
            }

            return _cached;
        }
    }

    private static bool TryReadLive(out NpcLookPalette palette)
    {
        palette = NpcLookPalette.Unobserved;
        try
        {
            PlayerCustomizaton[] found = Resources.FindObjectsOfTypeAll<PlayerCustomizaton>();
            if (found == null)
            {
                return false;
            }

            foreach (PlayerCustomizaton screen in found)
            {
                if (screen == null)
                {
                    continue;
                }

                palette = new NpcLookPalette(
                    Rgb(screen.m_skinColor0),
                    Rgb(screen.m_skinColor1),
                    Rgb(screen.m_hairColor0),
                    Rgb(screen.m_hairColor1),
                    screen.m_hairMinLevel,
                    screen.m_hairMaxLevel,
                    observed: true);
                return true;
            }
        }
        catch (Exception)
        {
            // A build that renamed the fields: the fallback is honest.
            _gaveUp = true;
        }

        return false;
    }

    private static NpcLookRgb Rgb(Color colour) => new NpcLookRgb(colour.r, colour.g, colour.b);
}
