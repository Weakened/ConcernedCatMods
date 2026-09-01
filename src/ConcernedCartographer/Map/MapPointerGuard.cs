using System;
using Jotunn.Managers;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>RC8-9: "pointer over any CC panel/control must NEVER add route
/// points — drawing only on uncovered map." One place answers whether the
/// pointer currently sits over Concerned Cartographer UI:
///
/// - any active top-level child of Jötunn's CustomGUIFront (every CC side
///   panel, the drawer, and the Pin Workbench dock there — as would any
///   other Jötunn mod's panel, which is equally "covered map");
/// - the registered large-map widgets (CC toolbar, contextual pin button,
///   the marker palette), which live on Minimap.m_largeRoot.
///
/// Fails open (not over UI) so a UI hiccup can never freeze route input
/// entirely; the worst failure mode is the pre-RC8 behavior.</summary>
internal static class MapPointerGuard
{
    private static readonly System.Collections.Generic.List<Func<GameObject?>> LargeMapWidgets = new();

    /// <summary>Registers a large-map widget root whose rect blocks route
    /// input while active. The provider may return null at any time.</summary>
    public static void RegisterWidget(Func<GameObject?> widget)
    {
        LargeMapWidgets.Add(widget);
    }

    /// <summary>Drops all registrations (plugin teardown).</summary>
    public static void Clear()
    {
        LargeMapWidgets.Clear();
    }

    public static bool IsPointerOverCcUi(Vector2 screenPosition)
    {
        try
        {
            GameObject? front = GUIManager.CustomGUIFront;
            if (front != null)
            {
                Transform frontRoot = front.transform;
                for (int index = 0; index < frontRoot.childCount; index++)
                {
                    Transform child = frontRoot.GetChild(index);
                    if (child.gameObject.activeInHierarchy &&
                        child is RectTransform rect &&
                        ContainsScreenPoint(rect, screenPosition))
                    {
                        return true;
                    }
                }
            }

            foreach (Func<GameObject?> provider in LargeMapWidgets)
            {
                GameObject? widget = provider();
                if (widget != null && widget.activeInHierarchy &&
                    widget.transform is RectTransform rect &&
                    ContainsScreenPoint(rect, screenPosition))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fail open: a guard failure must never eat map input.
        }

        return false;
    }

    private static bool ContainsScreenPoint(RectTransform rect, Vector2 screenPosition)
    {
        Canvas? canvas = rect.GetComponentInParent<Canvas>();
        Camera? camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.rootCanvas.worldCamera
            : null;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, camera);
    }
}
