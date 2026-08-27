using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Quick context pins: one keypress pins the object the player is
/// looking at, using the hover target only — never a scan, never a live
/// creature (no radar behavior). Fails safely with a HUD message when no
/// valid target exists, and respects the configured duplicate radius.</summary>
internal sealed class QuickPinCapture
{
    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;

    public QuickPinCapture(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>Attempts a quick pin for the local player's hover target.
    /// Returns true when a pin was created.</summary>
    public bool TryCapture(PinStore store, out string message)
    {
        message = "";
        try
        {
            Player player = Player.m_localPlayer;
            if (player is null)
            {
                return false;
            }

            GameObject? target = player.GetHoverObject();
            if (target == null)
            {
                message = "Nothing targeted to pin.";
                return false;
            }

            if (target.GetComponentInParent<Character>() != null)
            {
                message = "Creatures are not pinned.";
                return false;
            }

            string hoverName = "";
            var hoverable = target.GetComponentInParent<Hoverable>();
            if (hoverable is not null)
            {
                try
                {
                    hoverName = hoverable.GetHoverName() ?? "";
                }
                catch
                {
                    hoverName = "";
                }
            }

            QuickPinSuggester.Suggestion suggestion = QuickPinSuggester.Suggest(hoverName, target.name);
            Vector3 position = target.transform.position;
            var point = new RoadPoint(position.x, position.y, position.z);

            float radius = _settings.QuickPinDuplicateRadius.Value;
            if (radius > 0f)
            {
                foreach (AtlasPin existing in store.Living)
                {
                    if (!existing.Archived &&
                        string.Equals(existing.Name, suggestion.Name, StringComparison.OrdinalIgnoreCase) &&
                        existing.Position.HorizontalDistanceTo(point) <= radius)
                    {
                        message = $"\"{suggestion.Name}\" is already pinned {existing.Position.HorizontalDistanceTo(point):0.#} m away.";
                        return false;
                    }
                }
            }

            AtlasPin pin = store.Create(created =>
            {
                created.Name = suggestion.Name;
                created.IconId = suggestion.IconId;
                created.Category = suggestion.Category;
                created.Source = AtlasPinSource.Generated;
                created.Position = point;
            });

            _log.LogInfo($"Quick pin {pin.Id}: \"{suggestion.Name}\" ({suggestion.IconId}).");
            message = $"Pinned \"{suggestion.Name}\".";
            return true;
        }
        catch (Exception exception)
        {
            _log.LogError($"Quick pin failed: {exception}");
            message = "Quick pin failed; see the log.";
            return false;
        }
    }
}
