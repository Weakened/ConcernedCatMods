using System;
using System.Reflection;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Companions;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Reads the character-creation screen's colour palette off the live
/// game, and keeps it.
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
    private static GameObject? _watcher;

    /// <summary>Starts watching for the character screen.
    ///
    /// The palette lives on a component in the start scene, and that scene is
    /// gone by the time a world is loaded — so a read attempted from the actor,
    /// in world, can never succeed. Observed exactly that way in game: the
    /// companion's hair fell back to its documented tuning colour every single
    /// session, which is honest but is not the reference colour.
    ///
    /// The watcher is a tiny behaviour that polls a few times a second until it
    /// gets one read, then destroys itself. Polling rather than patching
    /// because the screen is opened by a UI button this mod has no business
    /// hooking, and one successful read lasts the whole process.</summary>
    public static void BeginWatching(ManualLogSource log)
    {
        if (_cached.Observed || _watcher != null)
        {
            return;
        }

        try
        {
            _watcher = new GameObject("CC_CustomizationPaletteWatcher");
            UnityEngine.Object.DontDestroyOnLoad(_watcher);
            _watcher.AddComponent<PaletteWatcher>().Log = log;
        }
        catch (Exception exception)
        {
            _watcher = null;
            log.LogInfo(
                "The companion's reference hair colour could not be looked up on this build, so his " +
                "documented fallback colour is used: " + exception.Message);
        }
    }

    /// <summary>Polls for the character screen and stops as soon as it has the
    /// palette. Deliberately cheap: four checks a second, each one a type
    /// lookup that returns an empty array while the screen is absent.</summary>
    private sealed class PaletteWatcher : MonoBehaviour
    {
        private const float IntervalSeconds = 0.25f;

        public ManualLogSource? Log;

        private float _elapsed;

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < IntervalSeconds)
            {
                return;
            }

            _elapsed = 0f;
            if (!Read(Log).Observed)
            {
                return;
            }

            _watcher = null;
            UnityEngine.Object.Destroy(gameObject);
        }
    }

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

        return CustomizationPalette.Unobserved;
    }

    /// <summary>Says once, at the point of use, that the fallback is in play.
    /// Separate from <see cref="Read"/> because the watcher calls that four
    /// times a second and a log line per poll would be its own defect.</summary>
    public static void NoteFallbackOnce(ManualLogSource log)
    {
        if (_cached.Observed || _loggedFailure)
        {
            return;
        }

        _loggedFailure = true;
        log.LogInfo(
            "The game's customization palette was not read this session, so the companion's hair uses " +
            "its documented fallback colour instead of the reference sliders. Nothing else is affected.");
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
