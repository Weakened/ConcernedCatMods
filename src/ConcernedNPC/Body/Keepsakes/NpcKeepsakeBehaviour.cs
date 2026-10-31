using System;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Keepsakes;

/// <summary>A keepsake's hover prompt and use, through the game's own
/// <c>Hoverable</c> and <c>Interactable</c>, so it is examined the way any
/// object is. Internal: only <see cref="NpcKeepsake"/> puts one on an object,
/// and only on the keepsake it built.</summary>
internal sealed class NpcKeepsakeBehaviour : MonoBehaviour, Hoverable, Interactable
{
    private string _name = string.Empty;
    private string _verb = string.Empty;
    private Action? _onExamine;
    private Action<string>? _log;

    /// <summary>True once the game's own hover raycast has reached it.</summary>
    internal bool HoverObserved { get; private set; }

    internal void Configure(string name, string verb, Action onExamine, Action<string>? log)
    {
        _name = name;
        _verb = verb;
        _onExamine = onExamine;
        _log = log;
    }

    public string GetHoverName()
    {
        HoverObserved = true;
        return _name;
    }

    public string GetHoverText()
    {
        HoverObserved = true;
        try
        {
            // Through the game's localization so $KEY_Use becomes the player's
            // own binding, gamepad included.
            return _name + "\n" + Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>]") +
                " " + _verb;
        }
        catch (Exception)
        {
            return _name;
        }
    }

    /// <summary>How high above the object the prompt floats.</summary>
    public float GetHoverOffset() => 0.35f;

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        // A held key repeats every frame; an introduction is opened once.
        if (hold)
        {
            return false;
        }

        try
        {
            _onExamine?.Invoke();
        }
        catch (Exception exception)
        {
            _log?.Invoke("The keepsake could not open its introduction (" + exception.GetType().Name + ").");
        }

        return true;
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
}
