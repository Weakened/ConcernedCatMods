using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Typing safety (RC10 feedback 14): while ANY text InputField is
/// focused, keystrokes are text — never Valheim actions and never CC/map
/// hotkeys. The runtime polls <see cref="AnyFieldFocused"/> every tick and
/// holds a reference-counted Jötunn input block exactly while a field is
/// focused; hotkey handlers and Escape-close paths consult the same state.
/// Nothing is swallowed when no field is focused, and blur releases
/// within one frame.</summary>
internal static class CcTextFocus
{
    private static int s_lastFocusedFrame = -10;

    /// <summary>True while the event system's selected object is a focused
    /// InputField. Fails open (false) if the UI stack is unavailable.</summary>
    public static bool AnyFieldFocused()
    {
        try
        {
            EventSystem? eventSystem = EventSystem.current;
            GameObject? selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null)
            {
                return false;
            }

            InputField field = selected.GetComponent<InputField>();
            if (field != null && field.isFocused)
            {
                s_lastFocusedFrame = Time.frameCount;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True on the frame a field is focused AND the frame right
    /// after it blurs. Escape both deactivates an InputField and would
    /// close the surrounding panel in the same frame; panels use this so
    /// the first Escape only ends typing.</summary>
    public static bool EscapeShouldOnlyBlur()
    {
        if (AnyFieldFocused())
        {
            return true;
        }

        return Time.frameCount - s_lastFocusedFrame <= 1;
    }
}
