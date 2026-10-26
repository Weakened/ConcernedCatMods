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
    public float BackFaceDepth = 0.08f;
    public string DisplayName = "Vertical Ladder";

    private static readonly HashSet<LadderSegment> Registered = new HashSet<LadderSegment>();

    private void OnEnable() => Registered.Add(this);
    private void OnDisable() => Registered.Remove(this);
    private void OnDestroy() => Registered.Remove(this);

    public string GetHoverName() => Localization.instance != null ? Localization.instance.Localize(DisplayName) : DisplayName;

    /// <summary>The prompt uses the same connected-run reach result as the Use press, so "Climb" is offered
    /// exactly where a press can reach the rungs, and a player too far away is told to step closer.</summary>
    public string GetHoverText()
    {
        LadderRuntime? runtime = LadderRuntime.Current;
        string text;
        if (runtime?.IsClimbing == true) { text = GetHoverName() + "\n[<color=yellow><b>$KEY_Use</b></color>] $cl_ladder_dismount"; }
        else if (runtime != null && !runtime.InReach(this)) { text = GetHoverName() + "\n$cl_hover_reach"; }
        else { text = GetHoverName() + "\n[<color=yellow><b>$KEY_Use</b></color>] $cl_ladder_climb"; }
        return Localization.instance != null ? Localization.instance.Localize(text) : text;
    }

    /// <summary>Valheim 1.0.20 adds this to the interact reach. Zero keeps the vanilla Ladder's default reach.</summary>
    public float GetHoverOffset() => 0f;

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        // Alt-interact is Shift+Use, and Shift is also run: a sprinting player still means "climb".
        if (hold || user != Player.m_localPlayer)
        {
            return false;
        }

        // Always false, like the vanilla Ladder. A true return makes Player.Interact play the interact
        // animation, whose minor-action state would release the climb that this call just started.
        LadderRuntime.Current?.Use(this);
        return false;
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
            !LadderRules.Finite(FrontFaceDepth) || FrontFaceDepth < 0f || FrontFaceDepth > 0.5f ||
            !LadderRules.Finite(BackFaceDepth) || BackFaceDepth < -0.5f || BackFaceDepth >= FrontFaceDepth)
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

    /// <summary>Observer-side lane test: the loaded section whose climbing lane, on either face, this body
    /// stands in while facing the rails. Bounded by the registry limit; called a few times a second.</summary>
    internal static LadderSegment? FindLane(Vector3 feet, Vector3 forward, float radius, out LadderFace face)
    {
        face = LadderFace.Front;
        if (!Finite(feet) || !Finite(forward) || !LadderRules.Finite(radius) || radius < 0.05f || radius > 1f ||
            Registered.Count > LadderRules.MaximumRegisteredSegments)
        {
            return null;
        }

        ClimbPoint point = Point(feet);
        foreach (LadderSegment candidate in Registered)
        {
            if (candidate == null || !candidate.TryGeometry(out LadderGeometry geometry)) { continue; }
            if (feet.y < geometry.Bottom.Y - 1.6f || feet.y > geometry.Top.Y + 0.1f) { continue; }
            if (geometry.SidewaysOffsetOf(point) > 0.25f) { continue; }
            float depth = geometry.ReachFrom(point);
            foreach (LadderFace side in Faces)
            {
                float lane = ClimbPolicy.LaneDepth(side, radius, candidate.BackFaceDepth, candidate.FrontFaceDepth);
                if (Math.Abs(depth - lane) > 0.15f) { continue; }
                float facing = -(float)side;
                if (forward.x * geometry.StandingSide.X * facing + forward.z * geometry.StandingSide.Z * facing < 0.85f) { continue; }
                face = side;
                return candidate;
            }
        }

        return null;
    }

    private static readonly LadderFace[] Faces = { LadderFace.Front, LadderFace.Rear };

    internal static ClimbPoint Point(Vector3 point) => new ClimbPoint(point.x, point.y, point.z);
    internal static Vector3 Vector(ClimbPoint point) => new Vector3(point.X, point.Y, point.Z);
    internal static bool Finite(Vector3 point) => LadderRules.Finite(point.x) && LadderRules.Finite(point.y) && LadderRules.Finite(point.z);
}
