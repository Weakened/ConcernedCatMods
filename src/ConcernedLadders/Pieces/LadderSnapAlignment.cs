using System;
using System.Collections.Generic;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>Scoped placement adapter (owner correction 2026-10-26). A ghost of one of this product's ladders is moved
/// onto the connected column it belongs to, so a stacked section is always one aligned column whatever rotation the
/// hammer last used. The selected snap mode is classified first:
/// <list type="bullet">
/// <item>a wall-corner mode keeps corner semantics: when the game has put the selected corner on another piece's snap
/// point, the ladder takes that piece's orientation, faces the player and is centred on it; aimed at or snapped onto
/// one of this product's ladders (which has no wall corner) it is refused; otherwise the game's own ghost stands;</item>
/// <item>Auto, Bottom and Top: when the game has snapped the ghost's bottom-centre onto exactly one placed ladder's
/// top-centre (or its top-centre onto a bottom-centre), the ghost takes that ladder's rotation and lies exactly on it;
/// otherwise, when the game's own placement ray hits a placed ladder's rails (the game found no snap point within its
/// 0.5 m radius and would leave the ghost overlapping or beside the ladder), the ghost extends that column at the end
/// the mode names (Bottom stacks on top, Top hangs below, Auto takes the nearer end and never buries a section under a
/// column that stands on the ground).</item>
/// </list>
/// An extension whose joint lies beyond the game's own placement reach is refused: the ghost is marked invalid rather
/// than moved out of reach. The move happens inside the game's placement update, after its snapping and its earlier
/// surface and duplicate-overlap checks, and before its no-build, ward, player-overlap, biome and clipping checks, which
/// all read the moved ghost. Held "toggle snapping" (free placement) is respected. Other pieces, costs and rules are the
/// game's. Any mismatch in the installed game's placement API leaves native snapping untouched.</summary>
internal sealed class LadderSnapAlignment : IDisposable
{
    /// <summary>The game's placement ray layers (Player.m_placeRayMask, 1.0.20).</summary>
    private static readonly string[] PlaceRayLayers = { "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle" };

    private static AccessTools.FieldRef<Player, GameObject>? _ghost;
    private static AccessTools.FieldRef<Player, int>? _manual;
    private static AccessTools.FieldRef<Player, List<Transform>>? _snapPoints;
    private static AccessTools.FieldRef<Player, bool>? _altPlace;
    private static AccessTools.FieldRef<Player, List<Transform>>? _targets;
    private static AccessTools.FieldRef<Player, Player.PlacementStatus>? _status;
    private static Action<Player, bool>? _paintGhost;
    private static Player? _updating;
    private static Player? _refused;
    private static int _placeMask;
    private static readonly List<LadderSegment> Run = new List<LadderSegment>();
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
                !Is<List<Transform>>("m_tempSnapPoints1") || !Is<List<Transform>>("m_tempSnapPoints2") || !Is<bool>("m_altPlace") ||
                !Is<Player.PlacementStatus>("m_placementStatus") ||
                AccessTools.Method(typeof(Player), "SetPlacementGhostValid", new[] { typeof(bool) }) == null)
            {
                _log("Ladder stack alignment unavailable: the installed game's placement API did not match. Native snapping is unchanged.");
                return false;
            }

            _ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
            _manual = AccessTools.FieldRefAccess<Player, int>("m_manualSnapPoint");
            _snapPoints = AccessTools.FieldRefAccess<Player, List<Transform>>("m_tempSnapPoints1");
            _altPlace = AccessTools.FieldRefAccess<Player, bool>("m_altPlace");
            _targets = AccessTools.FieldRefAccess<Player, List<Transform>>("m_tempSnapPoints2");
            _status = AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
            _paintGhost = AccessTools.MethodDelegate<Action<Player, bool>>(
                AccessTools.Method(typeof(Player), "SetPlacementGhostValid", new[] { typeof(bool) }));
            _placeMask = LayerMask.GetMask(PlaceRayLayers);
            _harmony = new Harmony(harmonyId);
            _harmony.Patch(update, prefix: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(BeforeGhostUpdate)),
                finalizer: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(AfterGhostUpdate)));
            _harmony.Patch(noBuild, prefix: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(BeforeValidity)));
            return true;
        }
        catch (Exception exception)
        {
            _log("Ladder stack alignment unavailable (" + SafeFailure.Brief(exception) + "). Native snapping is unchanged.");
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

    /// <summary>A refused extension (its joint beyond the game's placement reach) leaves the game's ghost where it
    /// was and marks it invalid, so neither the overlap the game would allow nor a far placement can be built.</summary>
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
        if (section == null || piece == null || !TrySelectedMode(player, section, out Transform? corner, out LadderSnap.Extend extend)) { return false; }
        Transform g = ghost.transform;

        // A wall-corner mode keeps its own semantics and never becomes a centre stack or a column extension. A ladder
        // has no wall corner: in game 2026-10-26 the game put the selected corner on a placed ladder's centre anchor
        // and showed the displaced ghost the owner rejected. Aimed at, or snapped onto, a ladder, a corner mode is
        // refused (the ghost is marked invalid); Auto, Bottom and Top extend ladders.
        if (corner != null)
        {
            if (TryWallCorner(player, g, corner, out aligned)) { return true; }
            if (CornerOnLadder(player, corner) || AimedLadder(out _) != null) { _refused = player; }
            return false;
        }

        Vector3 bottom = g.TransformPoint(section.LocalBottom);
        Vector3 top = g.TransformPoint(section.LocalTop);
        float reach = player.m_maxPlaceDistance + piece.m_extraPlacementDistance;
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
                if (!stacked && extend == LadderSnap.Extend.Nearest && StandsOnSomething(partnerBottom) &&
                    LadderSegment.TryRun(partner, Run, out var grounded) &&
                    LadderSnap.TryExtendColumn(grounded.Top.Y, grounded.Bottom, grounded.Top, LadderSnap.Extend.Above, true, out var onTop, out _))
                {
                    if (!LadderSnap.WithinPlacementReach(LadderSegment.Point(player.GetEyePoint()), grounded.Top, reach))
                    {
                        _refused = player;
                        return false;
                    }

                    origin = onTop;
                }

                return Place(g, p.rotation, LadderSegment.Vector(origin), out aligned);
            }
        }

        // Owner session 2026-10-26: aimed at the rails of a placed ladder, the game finds no snap point within its
        // 0.5 m radius and leaves the ghost overlapping or beside the ladder. The game's own placement ray (same
        // origin, direction, length and layers) says which piece is aimed at.
        LadderSegment? column = AimedLadder(out RaycastHit hit);
        if (column == null || !LadderSegment.TryRun(column, Run, out var run)) { return false; }
        if (!LadderSnap.TryExtendColumn(hit.point.y, run.Bottom, run.Top, extend, StandsOnSomething(LadderSegment.Vector(run.Bottom)),
                out var end, out bool extendsAbove))
        {
            return false;
        }

        if (!LadderSnap.WithinPlacementReach(LadderSegment.Point(player.GetEyePoint()), extendsAbove ? run.Top : run.Bottom, reach))
        {
            _refused = player;
            return false;
        }

        return Place(g, column.transform.rotation, LadderSegment.Vector(end), out aligned);
    }

    /// <summary>A corner mode whose snap point the game has put on another piece's snap point: the ladder takes that
    /// piece's orientation, faces the viewer and is centred on it (owner correction 2026-10-26). Every coincident target
    /// is considered, not just the first: targets on this product's ladders and targets that cannot centre the ladder
    /// are skipped, and two suitable targets that disagree are ambiguous. Anything else stays native.</summary>
    private static bool TryWallCorner(Player player, Transform ghost, Transform corner, out Vector3 aligned)
    {
        aligned = default;
        if (_targets == null || GameCamera.instance == null) { return false; }
        List<Transform> targets = _targets(player);
        if (targets == null) { return false; }
        Vector3 at = corner.position;
        ClimbPoint viewer = LadderSegment.Point(GameCamera.instance.transform.position);
        bool found = false;
        float chosenYaw = 0f;
        ClimbPoint chosenOrigin = default;
        foreach (Transform target in targets)
        {
            if (target == null || (target.position - at).sqrMagnitude > LadderSnap.Coincident * LadderSnap.Coincident) { continue; }
            Piece piece = target.GetComponentInParent<Piece>();
            if (piece == null || piece.GetComponent<LadderSegment>() != null) { continue; }
            Transform t = piece.transform;
            if (!LadderSnap.TryWallCornerYaw(t.eulerAngles.y, LadderSegment.Point(corner.localPosition), LadderSegment.Point(target.position),
                    LadderSegment.Point(t.position), viewer, out float yaw, out var origin))
            {
                continue;
            }

            if (found && (Math.Abs(Mathf.DeltaAngle(yaw, chosenYaw)) > 0.5f || origin.HorizontalDistanceTo(chosenOrigin) > LadderSnap.Coincident))
            {
                return false;
            }

            found = true;
            chosenYaw = yaw;
            chosenOrigin = origin;
        }

        return found && Place(ghost, Quaternion.Euler(0f, chosenYaw, 0f), LadderSegment.Vector(chosenOrigin), out aligned);
    }

    /// <summary>The placed ladder the game's own placement ray (same origin, direction, length and layers) hits first.</summary>
    private static LadderSegment? AimedLadder(out RaycastHit hit)
    {
        hit = default;
        if (GameCamera.instance == null) { return null; }
        Transform camera = GameCamera.instance.transform;
        if (!Physics.Raycast(camera.position, camera.forward, out hit, 50f, _placeMask) || hit.collider == null ||
            hit.collider.attachedRigidbody != null)
        {
            return null;
        }

        LadderSegment? column = hit.collider.GetComponentInParent<LadderSegment>();
        return column != null && column.IsLive ? column : null;
    }

    /// <summary>The game has put the selected corner on a snap point of one of this product's ladders.</summary>
    private static bool CornerOnLadder(Player player, Transform corner)
    {
        if (_targets == null) { return false; }
        List<Transform> targets = _targets(player);
        if (targets == null) { return false; }
        Vector3 at = corner.position;
        foreach (Transform target in targets)
        {
            if (target == null || (target.position - at).sqrMagnitude > LadderSnap.Coincident * LadderSnap.Coincident) { continue; }
            LadderSegment? ladder = target.GetComponentInParent<LadderSegment>();
            if (ladder != null && ladder.IsLive) { return true; }
        }

        return false;
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

    /// <summary>The selected snap mode, from the selected snap point's authored position: Auto (-1) takes the nearer
    /// end of an aimed-at column, bottom-centre stacks above, top-centre hangs below, and any other point is a wall
    /// corner (<paramref name="corner"/>). False when the game's selection could not be read.</summary>
    private static bool TrySelectedMode(Player player, LadderSegment section, out Transform? corner, out LadderSnap.Extend extend)
    {
        corner = null;
        extend = LadderSnap.Extend.Nearest;
        if (_manual == null || _snapPoints == null) { return false; }
        int index = _manual(player);
        if (index == -1) { return true; }
        List<Transform> points = _snapPoints(player);
        if (index < 0 || points == null || index >= points.Count || points[index] == null) { return false; }
        Vector3 local = points[index].localPosition;
        if ((local - section.LocalBottom).sqrMagnitude < 1e-4f) { extend = LadderSnap.Extend.Above; }
        else if ((local - section.LocalTop).sqrMagnitude < 1e-4f) { extend = LadderSnap.Extend.Below; }
        else { corner = points[index]; }
        return true;
    }

    public void Dispose()
    {
        try { _harmony?.UnpatchSelf(); } catch { /* Own patch only. */ }
        _harmony = null;
        _ghost = null;
        _manual = null;
        _snapPoints = null;
        _altPlace = null;
        _targets = null;
        _status = null;
        _paintGhost = null;
        _updating = null;
        _refused = null;
    }
}
