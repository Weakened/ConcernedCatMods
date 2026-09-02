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
                message = AtlasStrings.Get("hud.quickPinNothing");
                return false;
            }

            if (target.GetComponentInParent<Character>() != null)
            {
                message = AtlasStrings.Get("hud.quickPinCreature");
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

            // RC10 feedback 15: offer every identity in preference order —
            // the raw hover object, the owning ZNetView prefab root, the
            // transform root — so a technical child name ("Collider (1)")
            // never becomes the pin name while the real prefab still can.
            var nameCandidates = new System.Collections.Generic.List<string?> { target.name };
            var view = target.GetComponentInParent<ZNetView>();
            if (view != null && view.gameObject != null)
            {
                nameCandidates.Add(view.gameObject.name);
            }

            if (target.transform.root != null)
            {
                nameCandidates.Add(target.transform.root.name);
            }

            QuickPinSuggester.Suggestion suggestion = QuickPinSuggester.Suggest(hoverName, nameCandidates);
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
                        message = AtlasStrings.Format("hud.quickPinDuplicate", suggestion.Name, existing.Position.HorizontalDistanceTo(point).ToString("0.#"));
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
            message = AtlasStrings.Format("hud.quickPinned", suggestion.Name);
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
