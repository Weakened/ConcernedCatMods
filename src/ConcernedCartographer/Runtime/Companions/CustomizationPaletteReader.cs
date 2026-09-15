using System;
using System.Reflection;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Companions;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Reads the character-creation screen's colour palette off the live
/// game.
///
/// CC-NPC-006 requires the owner's slider readings to go through the game's own
/// conversion, and that conversion lerps between four colours declared in the
/// Unity inspector on <c>PlayerCustomizaton</c>. Inspector values are
/// serialized scene data: they cannot be derived from the assembly and they
/// must not be invented, so they are read from a live component or not used at
/// all.
///
/// The component lives on the start screen, which is gone by the time a world
/// is loaded, so the first successful read is cached for the rest of the
/// process. <c>Resources.FindObjectsOfTypeAll</c> is used rather than
/// <c>FindObjectOfType</c> because the panel is inactive unless a character is
/// actually being created. Nothing is instantiated, enabled or written to — the
/// six numbers are copied out and the component is left exactly as it was.
///
/// When nothing can be read, <see cref="CustomizationPalette.Unobserved"/> is
/// returned and <see cref="AppearanceColour"/> falls back to its documented
/// tuning colour. The report says which happened.</summary>
internal static class CustomizationPaletteReader
{
    private static CustomizationPalette _cached = CustomizationPalette.Unobserved;
    private static bool _loggedFailure;

    /// <summary>The live palette if it has ever been readable in this process,
    /// otherwise the unobserved one. Cheap after the first success.</summary>
    public static CustomizationPalette Read(ManualLogSource? log = null)
    {
        if (_cached.Observed)
        {
            return _cached;
        }

        CustomizationPalette? fresh = TryReadLive();
        if (fresh.HasValue)
        {
            _cached = fresh.Value;
            log?.LogInfo(
                "Read the game's customization palette: hair ramp " + _cached.HairLow + " to " +
                _cached.HairHigh + ", level " + _cached.MinimumLevel + " to " + _cached.MaximumLevel +
                ", skin ramp " + _cached.SkinLow + " to " + _cached.SkinHigh + ".");
            return _cached;
        }

        if (log != null && !_loggedFailure)
        {
            _loggedFailure = true;
            log.LogInfo(
                "The game's customization palette was not readable, so the companion's hair uses its " +
                "documented fallback colour instead of the reference sliders. Nothing else is affected.");
        }

        return CustomizationPalette.Unobserved;
    }

    /// <summary>Drops the cache. Test and diagnostic surface only.</summary>
    public static void Forget()
    {
        _cached = CustomizationPalette.Unobserved;
        _loggedFailure = false;
    }

    private static CustomizationPalette? TryReadLive()
    {
        try
        {
            PlayerCustomizaton[] found = Resources.FindObjectsOfTypeAll<PlayerCustomizaton>();
            if (found == null)
            {
                return null;
            }

            foreach (PlayerCustomizaton screen in found)
            {
                if (screen == null)
                {
                    continue;
                }

                return new CustomizationPalette(
                    skinLow: Triple(screen.m_skinColor0),
                    skinHigh: Triple(screen.m_skinColor1),
                    hairLow: Triple(screen.m_hairColor0),
                    hairHigh: Triple(screen.m_hairColor1),
                    minimumLevel: screen.m_hairMinLevel,
                    maximumLevel: screen.m_hairMaxLevel,
                    observed: true);
            }

            return null;
        }
        catch (MissingMemberException)
        {
            // A build that renamed or removed the fields. Reflection below is
            // not worth it for a tuning colour; the fallback is honest.
            return null;
        }
        catch (TypeLoadException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ColourTriple Triple(Color colour)
    {
        return new ColourTriple(colour.r, colour.g, colour.b);
    }

    /// <summary>The slider labels this build shows, for the acceptance note
    /// that resolves which of the owner's two hair readings is which. Returns
    /// null when the screen is not present. Read-only.</summary>
    public static string? DescribeSliders()
    {
        try
        {
            PlayerCustomizaton[] found = Resources.FindObjectsOfTypeAll<PlayerCustomizaton>();
            if (found == null || found.Length == 0)
            {
                return null;
            }

            foreach (PlayerCustomizaton screen in found)
            {
                if (screen == null)
                {
                    continue;
                }

                return "skinHue=" + LabelFor(screen.m_skinHue) +
                    " hairTone=" + LabelFor(screen.m_hairTone) +
                    " hairLevel=" + LabelFor(screen.m_hairLevel);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Best-effort label for one slider: the nearest text in its own
    /// hierarchy. Purely diagnostic, and silent when it finds nothing.</summary>
    private static string LabelFor(UnityEngine.UI.Slider? slider)
    {
        if (slider == null)
        {
            return "<absent>";
        }

        try
        {
            Transform? parent = slider.transform.parent;
            if (parent != null)
            {
                foreach (Component component in parent.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }

                    PropertyInfo? text = component.GetType().GetProperty(
                        "text", BindingFlags.Instance | BindingFlags.Public);
                    if (text?.GetValue(component) is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        return "\"" + value.Trim() + "\"";
                    }
                }
            }

            return slider.name;
        }
        catch
        {
            return slider.name;
        }
    }
}
