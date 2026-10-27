using System;
using TheConcernedCat.ConcernedForeman.Domain.Presentation;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>Reads the character-creation screen's colour endpoints off the live
/// game, once, and keeps them for the process (#298).
///
/// The conversion #298 requires lerps between colours that are Unity inspector
/// values on the game's customization component. They cannot be derived and
/// must not be invented, so they are read from a live component or not used.
/// That component exists only on the start screen, which is gone by the time a
/// world loads, so the plugin polls from its own Update a few times a second
/// until one read succeeds (Hulgi's approach in Concerned Cartographer, copied
/// rather than referenced). Nothing is instantiated, enabled or written; six
/// values are copied out.</summary>
internal static class ThorsteinPalette
{
    private const float IntervalSeconds = 0.25f;
    private const float GiveUpSeconds = 600f;

    private static LookPalette _cached = LookPalette.Unobserved;
    private static float _sincePoll;
    private static float _polled;
    private static bool _gaveUp;
    private static bool _saidFallback;

    /// <summary>Called from the plugin's Update in every scene. Cheap: four
    /// lookups a second until it succeeds or gives up, then nothing.</summary>
    internal static void Poll(float unscaledDeltaTime, Action<string> log)
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
        if (TryReadLive(out LookPalette fresh))
        {
            _cached = fresh;
            log("Read the game's customization palette for Thorstein's colouring.");
            return;
        }

        if (_polled >= GiveUpSeconds)
        {
            _gaveUp = true;
        }
    }

    /// <summary>The live palette if it was ever readable this process, otherwise
    /// the unobserved one, which the colour rule turns into the stated fallback.
    /// </summary>
    internal static LookPalette Read(Action<string> log)
    {
        if (!_cached.Observed && !_saidFallback)
        {
            _saidFallback = true;
            log("The game's customization palette was not read this session (it is only on the character " +
                "screen), so Thorstein's hair uses its stated fallback colour and his skin is the model's own.");
        }

        return _cached;
    }

    private static bool TryReadLive(out LookPalette palette)
    {
        palette = LookPalette.Unobserved;
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

                palette = new LookPalette(
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

    private static LookRgb Rgb(Color colour) => new LookRgb(colour.r, colour.g, colour.b);
}
