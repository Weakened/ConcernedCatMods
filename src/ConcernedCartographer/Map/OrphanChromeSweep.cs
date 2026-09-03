using System;
using System.Collections.Generic;
using System.Text;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>RC13 polish 3: hides the orphaned decorative chrome the RC11
/// per-group rail hiding cannot reach — a vanilla backplate that frames
/// already-hidden CC-replaced controls from OUTSIDE their button groups
/// (the empty rectangle at the large map's bottom-right in the owner's
/// RC12 smoke, most plausibly the visible-to-others toggle's plate).
/// From each already-hidden rail object it climbs a bounded number of
/// parents and hides the HIGHEST ancestor the pure
/// <see cref="OrphanChromeRule"/> verdict allows: no protected object
/// (map image, hint bars, shared-map hint, pin roots, biome label), no
/// control that would still be visible, no text-bearing graphic — pure
/// decoration only. Everything hidden is tracked and restored exactly
/// (SetActive only, never destruction) the moment any vanilla fallback
/// applies and on teardown. Every decision is summarized in
/// <see cref="LastDiagnostics"/> so a smoke run can see what was hidden
/// or why nothing was. Fail-soft: any exception leaves vanilla
/// untouched.</summary>
internal sealed class OrphanChromeSweep
{
    private const float SweepCooldownSeconds = 1f;

    private readonly List<GameObject> _hidden = new();
    private float _nextSweepTime;

    /// <summary>Stable summary of the latest sweep, for changed-only
    /// logging by the runtime.</summary>
    public string LastDiagnostics { get; private set; } = "";

    /// <summary>Climbs from each currently hidden rail object and hides
    /// safe orphaned chrome. Call only while NO vanilla fallback wants
    /// the rail visible; throttled internally.</summary>
    public void Sweep(List<GameObject?> hiddenRailObjects)
    {
        if (Time.unscaledTime < _nextSweepTime)
        {
            return;
        }

        _nextSweepTime = Time.unscaledTime + SweepCooldownSeconds;

        try
        {
            Minimap minimap = Minimap.instance;
            if (minimap == null || minimap.m_largeRoot == null)
            {
                return;
            }

            Transform largeRoot = minimap.m_largeRoot.transform;
            List<Transform> protectedRoots = CollectProtectedRoots(minimap);

            var summary = new StringBuilder();
            foreach (GameObject? start in hiddenRailObjects)
            {
                if (start == null || start.activeSelf)
                {
                    // Only objects CC actually hid seed a climb.
                    continue;
                }

                Transform? candidate = start.transform.parent;
                Transform? highestSafe = null;
                string stopReason = "large root reached";
                for (int step = 0; candidate != null && step < OrphanChromeRule.MaxClimbSteps; step++)
                {
                    OrphanChromeRule.CandidateFacts facts =
                        GatherFacts(candidate, largeRoot, protectedRoots, out string keepReason);
                    if (!OrphanChromeRule.MayHide(facts))
                    {
                        stopReason = $"'{candidate.name}': {keepReason}";
                        break;
                    }

                    highestSafe = candidate;
                    candidate = candidate.parent;
                }

                if (highestSafe != null)
                {
                    HideTracked(highestSafe.gameObject, summary);
                }
                else if (summary.Length == 0)
                {
                    // Keep the first refusal so smoke runs see WHY the
                    // sweep left something visible.
                    summary.Append($"no orphan above '{start.name}' ({stopReason})");
                }
            }

            LastDiagnostics = summary.Length > 0 ? summary.ToString() : "nothing to hide";
        }
        catch (Exception exception)
        {
            // LastDiagnostics is echoed into the log by the runtime, so the
            // exception text follows the CC-098 scrubbing contract.
            LastDiagnostics = $"sweep failed: {SafeLogText.Brief(exception)}";
        }
    }

    /// <summary>Restores every chrome object this sweep ever hid (exact
    /// vanilla state; dead references from map teardown are dropped).
    /// Safe to call unconditionally.</summary>
    public void RestoreAll()
    {
        foreach (GameObject hidden in _hidden)
        {
            if (hidden != null && !hidden.activeSelf)
            {
                hidden.SetActive(true);
            }
        }

        if (_hidden.Count > 0)
        {
            _hidden.Clear();
            LastDiagnostics = "";
            _nextSweepTime = 0f;
        }
    }

    private void HideTracked(GameObject chrome, StringBuilder summary)
    {
        bool alreadyTracked = false;
        foreach (GameObject hidden in _hidden)
        {
            if (ReferenceEquals(hidden, chrome))
            {
                alreadyTracked = true;
                break;
            }
        }

        if (!alreadyTracked)
        {
            _hidden.Add(chrome);
        }

        if (chrome.activeSelf)
        {
            chrome.SetActive(false);
        }

        string entry = $"hid '{chrome.name}'";
        if (!summary.ToString().Contains(entry))
        {
            if (summary.Length > 0)
            {
                summary.Append("; ");
            }

            summary.Append(entry);
        }
    }

    private static List<Transform> CollectProtectedRoots(Minimap minimap)
    {
        var roots = new List<Transform>();
        if (minimap.m_mapImageLarge != null)
        {
            roots.Add(minimap.m_mapImageLarge.transform);
        }

        if (minimap.m_sharedMapHint != null)
        {
            roots.Add(minimap.m_sharedMapHint.transform);
        }

        if (minimap.m_hints != null)
        {
            foreach (GameObject hint in minimap.m_hints)
            {
                if (hint != null)
                {
                    roots.Add(hint.transform);
                }
            }
        }

        MinimapReflection.GetLargeMapProtectedRoots(
            out RectTransform? pinRoot, out RectTransform? pinNameRoot, out Transform? biomeLabel);
        if (pinRoot != null)
        {
            roots.Add(pinRoot);
        }

        if (pinNameRoot != null)
        {
            roots.Add(pinNameRoot);
        }

        if (biomeLabel != null)
        {
            roots.Add(biomeLabel);
        }

        return roots;
    }

    private static OrphanChromeRule.CandidateFacts GatherFacts(
        Transform candidate, Transform largeRoot, List<Transform> protectedRoots, out string keepReason)
    {
        keepReason = "";
        bool isLargeRootOrAbove = candidate == largeRoot || !candidate.IsChildOf(largeRoot);
        if (isLargeRootOrAbove)
        {
            keepReason = "is (or escapes) the large root";
        }

        bool containsProtected = false;
        if (!isLargeRootOrAbove)
        {
            foreach (Transform protectedRoot in protectedRoots)
            {
                if (protectedRoot != null &&
                    (protectedRoot == candidate || protectedRoot.IsChildOf(candidate)))
                {
                    containsProtected = true;
                    keepReason = $"contains protected '{protectedRoot.name}'";
                    break;
                }
            }
        }

        bool hasLiveControl = false;
        bool hasLiveText = false;
        if (!isLargeRootOrAbove && !containsProtected)
        {
            foreach (Selectable control in candidate.GetComponentsInChildren<Selectable>(includeInactive: true))
            {
                if (WouldBeVisibleUnder(candidate, control.transform))
                {
                    hasLiveControl = true;
                    keepReason = $"contains live control '{control.name}'";
                    break;
                }
            }

            if (!hasLiveControl)
            {
                foreach (Graphic graphic in candidate.GetComponentsInChildren<Graphic>(includeInactive: true))
                {
                    // Plain Image/RawImage is decoration; anything else
                    // (Text, TMP, custom) means visible content.
                    if (graphic is Image || graphic is RawImage)
                    {
                        continue;
                    }

                    if (WouldBeVisibleUnder(candidate, graphic.transform))
                    {
                        hasLiveText = true;
                        keepReason = $"contains text '{graphic.name}'";
                        break;
                    }
                }
            }
        }

        return new OrphanChromeRule.CandidateFacts(
            isLargeRootOrAbove, containsProtected, hasLiveControl, hasLiveText);
    }

    /// <summary>Whether an element would be visible if the candidate
    /// itself were active: every activeSelf from the element up to (but
    /// excluding) the candidate is on. Deliberately independent of the
    /// candidate's own current state, so already-hidden chrome evaluates
    /// identically on every sweep.</summary>
    private static bool WouldBeVisibleUnder(Transform candidate, Transform element)
    {
        for (Transform? node = element; node != null && node != candidate; node = node.parent)
        {
            if (!node.gameObject.activeSelf)
            {
                return false;
            }
        }

        return true;
    }
}
