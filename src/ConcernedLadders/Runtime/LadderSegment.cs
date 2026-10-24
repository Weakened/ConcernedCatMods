using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>Authored endpoints and interaction for this product's pieces only. Never a vanilla Ladder component.</summary>
internal sealed class LadderSegment : MonoBehaviour, Hoverable, Interactable
{
    public Vector3 LocalBottom = Vector3.zero;
    public Vector3 LocalTop = new Vector3(0f, 2f, 0f);
    public Vector3 LocalOutward = Vector3.forward;
    public float Width = 0.8f;
    public float RungPitch = 1f / 3f;
    public float FrontFaceDepth = 0.24f;
    public string DisplayName = "Vertical ladder";

    private static readonly HashSet<LadderSegment> Registered = new HashSet<LadderSegment>();

    private void OnEnable() => Registered.Add(this);
    private void OnDisable() => Registered.Remove(this);
    private void OnDestroy() => Registered.Remove(this);

    public string GetHoverName() => Localization.instance != null ? Localization.instance.Localize(DisplayName) : DisplayName;

    public string GetHoverText()
    {
        string action = LadderRuntime.Current?.IsClimbing == true ? "Dismount" : "Climb";
        string text = GetHoverName() + "\n[<color=yellow><b>$KEY_Use</b></color>] " + action;
        return Localization.instance != null ? Localization.instance.Localize(text) : text;
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || alt || user != Player.m_localPlayer)
        {
            return false;
        }

        LadderRuntime? runtime = LadderRuntime.Current;
        if (runtime == null) { return false; }
        return runtime.Use(this);
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

    internal bool TryGeometry(out LadderGeometry geometry)
    {
        geometry = default;
        if (this == null || !isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            return false;
        }

        ZNetView view = GetComponentInParent<ZNetView>();
        if (view == null || !view.IsValid()) { return false; } // Ghosts and cached prefabs never participate.
        Vector3 bottom = transform.TransformPoint(LocalBottom);
        Vector3 top = transform.TransformPoint(LocalTop);
        Vector3 outward = transform.TransformDirection(LocalOutward);
        if (!Finite(bottom) || !Finite(top) || !Finite(outward) ||
            top.y - bottom.y < 1.95f || top.y - bottom.y > 2.05f ||
            Math.Abs(top.x - bottom.x) > 0.02f || Math.Abs(top.z - bottom.z) > 0.02f ||
            Math.Abs(outward.y) > 0.01f || !LadderRules.Finite(Width) || !LadderRules.Finite(RungPitch) ||
            !LadderRules.Finite(FrontFaceDepth) || FrontFaceDepth < 0f || FrontFaceDepth > 0.5f)
        {
            return false;
        }

        return LadderGeometry.TryMeasure(Point(bottom), Point(top), ClimbHeading.FromXz(outward.x, outward.z),
            Width, RungPitch, out geometry);
    }

    internal static bool TryRun(LadderSegment anchor, List<LadderSegment> segments, out LadderGeometry run)
    {
        segments.Clear();
        run = default;
        if (Registered.Count > LadderRules.MaximumRegisteredSegments) { return false; }
        var samples = new List<LadderSample>();
        var instances = new Dictionary<int, LadderSegment>();
        foreach (LadderSegment candidate in Registered)
        {
            if (candidate == null || !candidate.TryGeometry(out LadderGeometry geometry)) { continue; }
            int id = candidate.GetInstanceID();
            samples.Add(new LadderSample(id, geometry));
            instances[id] = candidate;
        }

        if (!LadderRules.TryBuildRun(samples, anchor.GetInstanceID(), out List<int> ids, out run)) { return false; }
        foreach (int id in ids) { segments.Add(instances[id]); }
        return true;
    }

    internal static ClimbPoint Point(Vector3 point) => new ClimbPoint(point.x, point.y, point.z);
    internal static Vector3 Vector(ClimbPoint point) => new Vector3(point.X, point.Y, point.Z);
    internal static bool Finite(Vector3 point) => LadderRules.Finite(point.x) && LadderRules.Finite(point.y) && LadderRules.Finite(point.z);
}
