using System;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body.Dressing;

/// <summary>Talking to a presentation figure through the game's own
/// <c>Hoverable</c> and <c>Interactable</c> (#295 S3). Internal: only
/// <see cref="NpcFigureDresser.EnableTalk"/> puts one on, and only on the figure
/// it dresses. Using it calls the role back; nothing here changes the world.
/// </summary>
internal sealed class NpcFigureTalk : MonoBehaviour, Hoverable, Interactable
{
    private string _name = string.Empty;
    private string _verb = string.Empty;
    private Action? _onTalk;
    private Action<string>? _log;

    /// <summary>True once the game's own hover raycast has reached it.</summary>
    internal bool HoverObserved { get; private set; }

    internal void Configure(string name, string verb, Action onTalk, Action<string>? log)
    {
        _name = name;
        _verb = verb;
        _onTalk = onTalk;
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

    public float GetHoverOffset() => 2.1f;

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        // A held key repeats every frame; a line is said once per press.
        if (hold)
        {
            return false;
        }

        try
        {
            _onTalk?.Invoke();
        }
        catch (Exception exception)
        {
            _log?.Invoke("Talking to the figure failed (" + exception.GetType().Name + ").");
        }

        return true;
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
}
