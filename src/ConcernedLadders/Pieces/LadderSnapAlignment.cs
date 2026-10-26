using System;
using System.Collections.Generic;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>Scoped placement adapter (owner decisions 2026-10-26). Cycle Snap offers Auto, Bottom and Top. A ghost of
/// one of this product's ladders is placed, in order:
/// <list type="bullet">
/// <item>on a ladder the game has snapped it to end to end (bottom-centre on a placed ladder's top-centre, or top on
/// bottom): it takes that ladder's rotation and lies exactly on it, one straight column;</item>
/// <item>aimed at a placed ladder's rails: it extends that column at the end the mode names (Bottom stacks on top, Top
/// hangs below, Auto takes the nearer end and never buries a section under a column that stands on the ground);</item>
/// <item>aimed at a floor or a wall: in the middle of it, the way walls snap to floors (<see cref="LadderEdgeSnap"/>):
/// centred on the floor edge nearest the aim, hanging below it (Auto, Top) or standing on it (Bottom); or centred on the
/// wall face toward the player, standing on its bottom edge (Auto, Bottom) or hanging from its top edge (Top). Flush
/// against the surface, climbing side outward, whatever rotation the hammer was left at.</item>
/// </list>
/// Anything else stays the game's own placement. A placement whose joint lies beyond the game's own placement reach
/// is refused: the ghost is marked invalid rather than moved out of reach. The move happens inside the game's placement
/// update, after its snapping and its earlier surface and duplicate-overlap checks, and before its no-build, ward,
/// player-overlap, biome and clipping checks, which all read the moved ghost. Held "toggle snapping" (free placement) is
/// respected. Any mismatch in the installed game's placement API leaves native snapping untouched.</summary>
internal sealed class LadderSnapAlignment : IDisposable
{
    /// <summary>The game's placement ray layers (Player.m_placeRayMask, 1.0.20).</summary>
    private static readonly string[] PlaceRayLayers = { "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle" };

    private static AccessTools.FieldRef<Player, GameObject>? _ghost;
    private static AccessTools.FieldRef<Player, int>? _manual;
    private static AccessTools.FieldRef<Player, List<Transform>>? _snapPoints;
    private static AccessTools.FieldRef<Player, bool>? _altPlace;
    private static AccessTools.FieldRef<Player, Player.PlacementStatus>? _status;
    private static Action<Player, bool>? _paintGhost;
    private static Player? _updating;
    private static Player? _refused;
    private static Action<string>? _say;
    private static string _decision = "";
    private static int _placeMask;
    private static readonly List<LadderSegment> Run = new List<LadderSegment>();
    private static readonly List<Transform> TargetPoints = new List<Transform>();
    private static readonly List<ClimbPoint> Corners = new List<ClimbPoint>();
    private readonly Action<string> _log;
    private Harmony? _harmony;

    internal LadderSnapAlignment(Action<string> log) => _log = log;

    internal bool Install(string harmonyId)
    {
        try
        {
            var update = AccessTools.Method(typeof(Player), "UpdatePlacementGhost", new[] { typeof(bool) });
            var noBuild = AccessTools.Method(typeof(Location), "IsInsideNoBuildLocation", new[] { typeof(Vector3) });
            if (update == null || noBuild == null || noBuild.ReturnType != typeof(bool) ||
                !Is<GameObject>("m_placementGhost") || !Is<int>("m_manualSnapPoint") ||
                !Is<List<Transform>>("m_tempSnapPoints1") || !Is<bool>("m_altPlace") ||
                !Is<Player.PlacementStatus>("m_placementStatus") ||
                AccessTools.Method(typeof(Player), "SetPlacementGhostValid", new[] { typeof(bool) }) == null)
            {
                _log("Ladder snapping unavailable: the installed game's placement API did not match. Native snapping is unchanged.");
                return false;
            }

            _ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
            _manual = AccessTools.FieldRefAccess<Player, int>("m_manualSnapPoint");
            _snapPoints = AccessTools.FieldRefAccess<Player, List<Transform>>("m_tempSnapPoints1");
            _altPlace = AccessTools.FieldRefAccess<Player, bool>("m_altPlace");
            _status = AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
            _paintGhost = AccessTools.MethodDelegate<Action<Player, bool>>(
                AccessTools.Method(typeof(Player), "SetPlacementGhostValid", new[] { typeof(bool) }));
            _placeMask = LayerMask.GetMask(PlaceRayLayers);
            _say = _log;
            _harmony = new Harmony(harmonyId);
            _harmony.Patch(update, prefix: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(BeforeGhostUpdate)),
                finalizer: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(AfterGhostUpdate)));
            _harmony.Patch(noBuild, prefix: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(BeforeValidity)));
            return true;
        }
        catch (Exception exception)
        {
            _log("Ladder snapping unavailable (" + SafeFailure.Brief(exception) + "). Native snapping is unchanged.");
            Dispose();
            return false;
        }
    }

    private static bool Is<T>(string name)
    {
        var field = AccessTools.Field(typeof(Player), name);
        return field != null && field.FieldType == typeof(T);
    }

    private static void BeforeGhostUpdate(Player __instance)
    {
        _updating = ReferenceEquals(__instance, Player.m_localPlayer) ? __instance : null;
        _refused = null;
    }

    /// <summary>A refused placement (its joint beyond the game's placement reach) leaves the game's ghost where it
    /// was and marks it invalid, so neither the game's own overlap nor a far placement can be built.</summary>
    private static Exception? AfterGhostUpdate(Player __instance, Exception? __exception)
    {
        _updating = null;
        Player? refused = _refused;
        _refused = null;
        if (__exception == null && refused != null && ReferenceEquals(refused, __instance) && _status != null && _paintGhost != null)
        {
            try
            {
                _status(__instance) = Player.PlacementStatus.Invalid;
                _paintGhost(__instance, false);
            }
            catch
            {
                // Best effort: the game's own status stands.
            }
        }

        return __exception;
    }

    /// <summary>The game's placement update calls the no-build test exactly once, after it has snapped the ghost and
    /// before its no-build, ward, player-overlap, biome and clipping checks; the test itself then reads the aligned
    /// position.</summary>
    private static void BeforeValidity(ref Vector3 __0)
    {
        Player? player = _updating;
        _updating = null; // Once per update; any other caller of the no-build test is untouched.
        if (player == null) { return; }
        try
        {
            if (Align(player, out Vector3 aligned)) { __0 = aligned; }
            else if (_refused == null) { Decide("native"); }
        }
        catch
        {
            // On any fault the game's own ghost stands unchanged.
        }
    }

    private static bool Align(Player player, out Vector3 aligned)
    {
        aligned = default;
        if (_ghost == null || SnappingSuppressed(player)) { return false; }
        GameObject ghost = _ghost(player);
        if (ghost == null || !ghost.activeSelf) { return false; }
        LadderSegment? section = ghost.GetComponent<LadderSegment>();
        Piece? piece = ghost.GetComponent<Piece>();
        if (section == null || piece == null || !TrySelectedMode(player, section, out LadderSnapMode mode)) { return false; }
        Transform g = ghost.transform;
        float reach = player.m_maxPlaceDistance + piece.m_extraPlacementDistance;
        ClimbPoint eye = LadderSegment.Point(player.GetEyePoint());

        // 1. Snapped end to end onto exactly one placed ladder: one straight column.
        Vector3 bottom = g.TransformPoint(section.LocalBottom);
        Vector3 top = g.TransformPoint(section.LocalTop);
        LadderSegment? partner = LadderSegment.FindStackPartner(bottom, top, LadderSnap.Coincident, out _);
        if (partner != null)
        {
            Transform p = partner.transform;
            Vector3 partnerBottom = p.TransformPoint(partner.LocalBottom);
            if (LadderSnap.TryAlignStack(LadderSegment.Point(bottom), LadderSegment.Point(top),
                    LadderSegment.Point(partnerBottom), LadderSegment.Point(p.TransformPoint(partner.LocalTop)),
                    out var origin, out bool stacked))
            {
                // In game 2026-10-26: Auto paired the ghost's top with the foot of a ladder standing on the ground and
                // buried the new section. Auto then stacks on the column's top instead, within the game's reach; an
                // explicit Top mode is honoured.
                if (!stacked && mode == LadderSnapMode.Auto && StandsOnSomething(partnerBottom) &&
                    LadderSegment.TryRun(partner, Run, out var grounded) &&
                    LadderSnap.TryExtendColumn(grounded.Top.Y, grounded.Bottom, grounded.Top, LadderSnap.Extend.Above, true, out var onTop, out _))
                {
                    if (!LadderSnap.WithinPlacementReach(eye, grounded.Top, reach)) { _refused = player; return false; }
                    origin = onTop;
                }

                Decide("stack on " + partner.name);
                return Place(g, p.rotation, LadderSegment.Vector(origin), out aligned);
            }
        }

        if (!AimedPiece(out RaycastHit hit, out Piece? aimed) || aimed == null) { Decide("native: aim hits no piece"); return false; }

        // 2. Aimed at a placed ladder's rails: extend that column.
        LadderSegment? column = aimed.GetComponent<LadderSegment>();
        if (column != null)
        {
            if (!column.IsLive || !LadderSegment.TryRun(column, Run, out var run)) { return false; }
            LadderSnap.Extend extend = mode == LadderSnapMode.Bottom ? LadderSnap.Extend.Above :
                mode == LadderSnapMode.Top ? LadderSnap.Extend.Below : LadderSnap.Extend.Nearest;
            if (!LadderSnap.TryExtendColumn(hit.point.y, run.Bottom, run.Top, extend, StandsOnSomething(LadderSegment.Vector(run.Bottom)),
                    out var end, out bool extendsAbove))
            {
                return false;
            }

            if (!LadderSnap.WithinPlacementReach(eye, extendsAbove ? run.Top : run.Bottom, reach)) { _refused = player; Decide("refused: column end out of reach"); return false; }
            Decide("extend column " + (extendsAbove ? "above" : "below"));
            return Place(g, column.transform.rotation, LadderSegment.Vector(end), out aligned);
        }

        // 3. Aimed at a floor or a wall: in the middle of it, the way walls snap to floors.
        Transform t = aimed.transform;
        TargetPoints.Clear();
        aimed.GetSnapPoints(TargetPoints);
        Corners.Clear();
        foreach (Transform point in TargetPoints)
        {
            if (point != null) { Corners.Add(LadderSegment.Point(t.InverseTransformPoint(point.position))); }
        }

        Vector3 viewer = GameCamera.instance != null ? GameCamera.instance.transform.position : player.GetEyePoint();
        if (!LadderEdgeSnap.TryPlace(Corners, LadderSegment.Point(t.InverseTransformPoint(hit.point)), LadderSegment.Point(t.InverseTransformPoint(viewer)),
                mode, out ClimbPoint localOrigin, out float facingX, out float facingZ, out ClimbPoint localJoint))
        {
            Decide("native: " + aimed.name + " is neither floor nor wall (" + Corners.Count + " snap points)");
            return false;
        }

        Vector3 joint = t.TransformPoint(LadderSegment.Vector(localJoint));
        Vector3 facingWorld = t.TransformDirection(new Vector3(facingX, 0f, facingZ));
        if (mode == LadderSnapMode.Auto && localOrigin.Y < localJoint.Y - 1f &&
            Physics.Raycast(joint + facingWorld * 0.15f + Vector3.up * 0.05f, Vector3.down, LadderEdgeSnap.SectionHeight - 0.1f, _placeMask, QueryTriggerInteraction.Ignore))
        {
            // Auto would hang the ladder below a floor edge with no room under it (a floor on the ground): it stands
            // on the edge instead, like Bottom.
            if (!LadderEdgeSnap.TryPlace(Corners, LadderSegment.Point(t.InverseTransformPoint(hit.point)), LadderSegment.Point(t.InverseTransformPoint(viewer)),
                    LadderSnapMode.Bottom, out localOrigin, out facingX, out facingZ, out localJoint))
            {
                return false;
            }

            joint = t.TransformPoint(LadderSegment.Vector(localJoint));
        }

        if (!LadderSnap.WithinPlacementReach(eye, LadderSegment.Point(joint), reach)) { _refused = player; Decide("refused: " + aimed.name + " edge out of reach"); return false; }
        Vector3 facing = t.TransformDirection(new Vector3(facingX, 0f, facingZ));
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f) { return false; }
        Decide("centre on " + aimed.name + " at " + t.TransformPoint(LadderSegment.Vector(localOrigin)).ToString("F2"));
        return Place(g, Quaternion.LookRotation(facing.normalized, Vector3.up), t.TransformPoint(LadderSegment.Vector(localOrigin)), out aligned);
    }

    private static void Decide(string decision)
    {
        if (!Runtime.Animation.ClimbTuning.LogSnapDecisions || decision == _decision) { return; }
        _decision = decision;
        try { _say?.Invoke("Ladder snap: " + decision); } catch { /* Diagnostics only. */ }
    }

    /// <summary>The piece the game's own placement ray (same origin, direction, length and layers) hits first.</summary>
    private static bool AimedPiece(out RaycastHit hit, out Piece? piece)
    {
        hit = default;
        piece = null;
        if (GameCamera.instance == null) { return false; }
        Transform camera = GameCamera.instance.transform;
        if (!Physics.Raycast(camera.position, camera.forward, out hit, 50f, _placeMask) || hit.collider == null ||
            hit.collider.attachedRigidbody != null)
        {
            return false;
        }

        piece = hit.collider.GetComponentInParent<Piece>();
        if (piece == null) { return false; }
        LadderSegment? ladder = piece.GetComponent<LadderSegment>();
        return ladder == null || ladder.IsLive; // The ghost itself never counts.
    }

    private static readonly RaycastHit[] Under = new RaycastHit[16];

    /// <summary>Ground or a floor within 0.3 m under a column's foot, ignoring this product's own ladders. A saturated
    /// buffer counts as standing on something, so Auto then stacks upward.</summary>
    private static bool StandsOnSomething(Vector3 foot)
    {
        int count = Physics.RaycastNonAlloc(foot + Vector3.up * 0.05f, Vector3.down, Under, 0.35f, _placeMask, QueryTriggerInteraction.Ignore);
        if (count >= Under.Length) { return true; }
        for (int index = 0; index < count; index++)
        {
            Collider collider = Under[index].collider;
            if (collider != null && collider.GetComponentInParent<LadderSegment>() == null) { return true; }
        }

        return false;
    }

    /// <summary>The game's own "toggle snapping" hold (Player.UpdatePlacementGhost's free-placement flag).</summary>
    private static bool SnappingSuppressed(Player player)
    {
        if (ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive()) { return _altPlace == null || _altPlace(player); }
        return ZInput.GetButton("AltPlace") || (ZInput.GetButton("JoyAltPlace") && !ZInput.GetButton("JoyRotate"));
    }

    private static bool Place(Transform ghost, Quaternion rotation, Vector3 origin, out Vector3 placed)
    {
        placed = origin;
        if (!LadderSegment.Finite(origin)) { return false; }
        ghost.rotation = rotation;
        ghost.position = origin;
        return true;
    }

    /// <summary>The selected snap mode, from the selected snap point's authored position: Auto (-1), bottom-centre or
    /// top-centre. False when the game's selection could not be read.</summary>
    private static bool TrySelectedMode(Player player, LadderSegment section, out LadderSnapMode mode)
    {
        mode = LadderSnapMode.Auto;
        if (_manual == null || _snapPoints == null) { return false; }
        int index = _manual(player);
        if (index == -1) { return true; }
        List<Transform> points = _snapPoints(player);
        if (index < 0 || points == null || index >= points.Count || points[index] == null) { return false; }
        Vector3 local = points[index].localPosition;
        if ((local - section.LocalBottom).sqrMagnitude < 1e-4f) { mode = LadderSnapMode.Bottom; return true; }
        if ((local - section.LocalTop).sqrMagnitude < 1e-4f) { mode = LadderSnapMode.Top; return true; }
        return false;
    }

    public void Dispose()
    {
        try { _harmony?.UnpatchSelf(); } catch { /* Own patch only. */ }
        _harmony = null;
        _ghost = null;
        _manual = null;
        _snapPoints = null;
        _altPlace = null;
        _status = null;
        _paintGhost = null;
        _updating = null;
        _refused = null;
        _say = null;
    }
}
