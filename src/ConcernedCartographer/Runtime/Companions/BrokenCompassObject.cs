using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>The Broken Compass in the world: a real object a player can walk
/// up to, hover and examine.
///
/// It is local-only in the strongest sense available — there is no
/// <c>ZNetView</c> on it, so it has no ZDO, takes no part in the save, is
/// never replicated, and an unmodded peer standing in the same spot sees
/// nothing at all. It is also not an inventory item: examining it records a
/// memento in the mod's own sidecar and never touches a single inventory slot.
///
/// Its collider exists to be <i>looked at</i>, not walked into. It is placed on
/// a layer that vanilla already treats as non-solid for characters, and if no
/// such layer is available it becomes a trigger instead, so under every
/// resolution of that question the player can still walk through the spot the
/// compass occupies.</summary>
internal sealed class BrokenCompassObject : MonoBehaviour, Hoverable, Interactable
{
    /// <summary>Layers that can be raycast for interaction but do not stop a
    /// body, in preference order.</summary>
    private static readonly string[] NonSolidLayers = { "piece_nonsolid", "item" };

    private Action? _onExamine;
    private ManualLogSource? _log;

    /// <summary>True when the object sits on a layer known not to collide with
    /// characters. False means it fell back to a trigger collider, which is
    /// also non-blocking but reaches vanilla hovering less reliably.</summary>
    public bool OnNonSolidLayer { get; private set; }

    /// <summary>True when vanilla's own hover raycast has actually reached
    /// this object at least once. The console tool reports it, which is how
    /// the pending "does hovering work on this build" evidence row gets closed
    /// by observation instead of assumption.</summary>
    public bool HoverObserved { get; private set; }

    public static BrokenCompassObject Attach(GameObject root, Action onExamine, ManualLogSource log)
    {
        var component = root.AddComponent<BrokenCompassObject>();
        component._onExamine = onExamine;
        component._log = log;
        component.BuildCollider();
        return component;
    }

    public string GetHoverName()
    {
        HoverObserved = true;
        return AtlasStrings.Get("companion.compass.name");
    }

    public string GetHoverText()
    {
        HoverObserved = true;
        try
        {
            return AtlasStrings.Get("companion.compass.name") + "\n[<color=yellow><b>$KEY_Use</b></color>] " +
                AtlasStrings.Get("companion.compass.hoverVerb");
        }
        catch
        {
            return AtlasStrings.Get("companion.compass.name");
        }
    }

    public float GetHoverOffset()
    {
        return 0.35f;
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        // A held key repeats every frame; the introduction is not something to
        // open sixty times a second.
        if (hold)
        {
            return false;
        }

        Examine();
        return true;
    }

    /// <summary>Nothing is craftable, repairable or usable on the compass. It
    /// is deliberately inert to items so no tool, food or weapon interaction
    /// can produce a state this mod did not design.</summary>
    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        return false;
    }

    /// <summary>The one entry point. Both the vanilla hover path and the
    /// proximity fallback call this, and it is idempotent at the quest layer,
    /// so a player who triggers both in one frame still gets one
    /// introduction.</summary>
    public void Examine()
    {
        try
        {
            _onExamine?.Invoke();
        }
        catch (Exception exception)
        {
            _log?.LogError(
                $"The Broken Compass could not open its introduction: {SafeLogText.Describe(exception)}");
        }
    }

    private void BuildCollider()
    {
        try
        {
            var collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = 0.4f;
            collider.center = new Vector3(0f, 0.2f, 0f);

            int layer = ResolveNonSolidLayer();
            if (layer >= 0)
            {
                OnNonSolidLayer = true;
                SetLayerRecursively(gameObject, layer);
                return;
            }

            // No non-solid layer on this build: a trigger cannot block a body
            // either, so the player can still walk through. It may or may not
            // be reachable by vanilla hovering, which is exactly why the
            // proximity prompt exists.
            collider.isTrigger = true;
            _log?.LogInfo(
                "No non-solid interaction layer on this build, so the Broken Compass uses a " +
                "pass-through collider. Walk up to it and the prompt still appears.");
        }
        catch (Exception exception)
        {
            _log?.LogInfo(
                "The Broken Compass could not take a collider, so it is examined from the proximity " +
                $"prompt only: {SafeLogText.Brief(exception)}");
        }
    }

    private static int ResolveNonSolidLayer()
    {
        foreach (string name in NonSolidLayers)
        {
            try
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0)
                {
                    return layer;
                }
            }
            catch
            {
                // Try the next name.
            }
        }

        return -1;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
