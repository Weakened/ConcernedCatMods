using System;
using System.Collections.Generic;
using TheConcernedCat.Ladders;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Ladders;

/// <summary>The ladders near a character, measured once and remembered.
///
/// Vanilla keeps no list of its ladders (unlike carts, which register
/// themselves), so they have to be found. Finding them must cost nothing when
/// nobody is climbing, which rules out any scene-wide search: this asks physics
/// for the colliders inside a few metres of the character, four times a second,
/// into a buffer it already owns. A tower of ladders with nobody on it is never
/// touched at all, because nothing is near a character.
///
/// Measuring a piece — its ends, its centreline, which face the rungs are on,
/// how wide it is, how far apart the rungs are — costs mesh bounds and a couple
/// of short rays, and is done once per piece and again only if that piece moves.
/// The measurement is handed to <see cref="LadderGeometry"/>, which is where
/// every decision about it is made.</summary>
internal sealed class LadderSurvey
{
    /// <summary>Where ladders live. The two buildable pieces are on `piece`;
    /// the props the runtime audit may add (CF-LAD-004 decides which count) can
    /// be on the other three, so they are included rather than discovered
    /// missing in somebody's world.</summary>
    private static readonly string[] SurveyLayers =
    {
        "piece", "piece_nonsolid", "Default", "static_solid", "Default_small",
    };

    /// <summary>What a ray is allowed to call a wall, a floor or a roof. The
    /// same set vanilla uses for its own ground and blocking rays.</summary>
    private static readonly string[] SolidLayers =
    {
        "piece", "Default", "static_solid", "Default_small", "terrain", "blocker", "vehicle",
    };

    private readonly ClimbOptions _options;
    private readonly Action<string> _log;
    private readonly Collider[] _overlap = new Collider[128];
    private readonly RaycastHit[] _rays = new RaycastHit[16];
    private readonly Dictionary<int, MeasuredLadder> _measured = new Dictionary<int, MeasuredLadder>();
    private readonly List<MeasuredLadder> _nearby = new List<MeasuredLadder>();
    private readonly List<int> _stale = new List<int>();
    private readonly List<LadderGeometry> _stack = new List<LadderGeometry>();
    private readonly List<Ladder> _stackPieces = new List<Ladder>();

    private int _surveyLayerMask;
    private int _solidLayerMask;
    private float _sinceLastLook;
    private bool _truncatedOnce;

    internal LadderSurvey(ClimbOptions options, Action<string> log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Which pieces count as climbable. Everything that carries a
    /// <c>Ladder</c> and measures as a ladder, until CF-LAD-004's in-game audit
    /// narrows it; that workstream sets this rather than editing this file.</summary>
    internal Func<Ladder, bool>? Admits { get; set; }

    /// <summary>How many ladders are currently remembered. Zero when nobody is
    /// near one, which is the state a tower of ladders leaves the survey in.</summary>
    internal int Known => _measured.Count;

    /// <summary>The one recurring cost of the feature: one physics overlap
    /// every <see cref="ClimbOptions.SurveyIntervalSeconds"/>, into a buffer
    /// that is allocated once.</summary>
    internal void Look(float deltaSeconds, Vector3 around, bool force = false)
    {
        _sinceLastLook += Math.Max(0f, deltaSeconds);
        if (!force && _sinceLastLook < _options.SurveyIntervalSeconds)
        {
            return;
        }

        _sinceLastLook = 0f;
        EnsureMasks();
        _nearby.Clear();

        int found = Physics.OverlapSphereNonAlloc(
            around,
            Math.Max(1f, _options.SurveyRadiusMetres),
            _overlap,
            _surveyLayerMask,
            QueryTriggerInteraction.Ignore);

        if (found >= _overlap.Length && !_truncatedOnce)
        {
            // Said once, not once a frame: a base dense enough to fill the
            // buffer would otherwise fill the log too.
            _truncatedOnce = true;
            _log(
                "Ladders: more than " + _overlap.Length + " colliders within " +
                _options.SurveyRadiusMetres.ToString("0.#") +
                " m, so a ladder in a very dense build may not be seen from every angle. " +
                "Step towards it and it will be.");
        }

        for (int index = 0; index < found; index++)
        {
            Collider collider = _overlap[index];
            if (collider == null)
            {
                continue;
            }

            Ladder ladder = collider.GetComponentInParent<Ladder>();
            if (ladder == null || (Admits != null && !Admits(ladder)))
            {
                continue;
            }

            MeasuredLadder? measured = Measure(ladder);
            if (measured != null && !_nearby.Contains(measured))
            {
                _nearby.Add(measured);
            }
        }

        DropDestroyed();
    }

    /// <summary>Forget everything. The world is going away, or ladders were
    /// switched off: nothing measured against a scene that no longer exists may
    /// survive into the next one.</summary>
    internal void Forget()
    {
        _measured.Clear();
        _nearby.Clear();
        _sinceLastLook = float.MaxValue;
        _truncatedOnce = false;
    }

    /// <summary>The run this character may climb from where it stands, and the
    /// pieces it is made of, or the reason it may not.
    ///
    /// <paramref name="refusal"/> is <see cref="MountRefusal.Unspecified"/> when
    /// there is simply no ladder near enough to be talking about, which is the
    /// case that must stay silent.</summary>
    internal bool TryPlanMount(
        in ClimberState climber,
        ClimbLimits limits,
        bool laddersEnabled,
        bool byDeliberateUse,
        Ladder? only,
        out LadderRun run,
        out IReadOnlyList<Ladder> pieces,
        out MountRefusal refusal)
    {
        run = null!;
        pieces = Array.Empty<Ladder>();
        refusal = MountRefusal.Unspecified;

        MeasuredLadder? best = null;
        LadderGeometry bestGeometry = default;
        float bestReach = float.MaxValue;

        for (int index = 0; index < _nearby.Count; index++)
        {
            MeasuredLadder candidate = _nearby[index];
            if (candidate.Ladder == null || (only != null && candidate.Ladder != only))
            {
                continue;
            }

            if (!candidate.TryGeometry(climber.Feet, out LadderGeometry geometry))
            {
                refusal = Worse(refusal, MountRefusal.NotClimbable);
                continue;
            }

            MountDecision decision = LadderMount.Decide(
                geometry, climber, limits, laddersEnabled, byDeliberateUse);
            if (!decision.Allowed)
            {
                refusal = Worse(refusal, decision.Refusal);
                continue;
            }

            float reach = geometry.ReachFrom(climber.Feet);
            if (reach < bestReach)
            {
                bestReach = reach;
                best = candidate;
                bestGeometry = geometry;
            }
        }

        if (best == null)
        {
            return false;
        }

        refusal = MountRefusal.Unspecified;
        BuildStack(best, bestGeometry, climber.Feet);
        if (_stack.Count > 1 && LadderRun.TryJoin(_stack, out LadderRun joined))
        {
            run = joined;
            pieces = _stackPieces.ToArray();
            return true;
        }

        run = LadderRun.Single(bestGeometry);
        pieces = new[] { best.Ladder };
        return true;
    }

    /// <summary>Is there room to stand at the top of this run: a floor at about
    /// the right height, and space for the character's own capsule above it.
    ///
    /// The point handed back is the floor's height carried at the head of the
    /// ladder, because <see cref="ClimbExit.OverTheTop"/> is what steps it in
    /// over the edge and up onto the surface. Probing the spot the climber will
    /// actually stand on, rather than the ladder's own line, is the whole point
    /// of the probe: a ladder ending at a wall and a ladder ending at a floor
    /// look the same from the ladder.</summary>
    internal TopLanding ProbeTop(in LadderGeometry run, ClimbLimits limits, CapsuleCollider? body)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        EnsureMasks();

        ClimbHeading inwards = run.FacingWhileClimbing;
        if (!inwards.IsKnown)
        {
            return TopLanding.None;
        }

        var head = new Vector3(run.Top.X, run.Top.Y, run.Top.Z);
        var step = new Vector3(inwards.X, 0f, inwards.Z);
        Vector3 standing = head + (step * limits.TopStepInMetres);

        float above = Math.Max(0.1f, _options.TopFloorAboveMetres);
        float below = Math.Max(0.1f, _options.TopFloorBelowMetres);
        var from = new Vector3(standing.x, head.y + above + 0.05f, standing.z);
        int hits = Physics.RaycastNonAlloc(
            from,
            Vector3.down,
            _rays,
            above + below + 0.1f,
            _solidLayerMask,
            QueryTriggerInteraction.Ignore);

        // Every surface under the step, highest first, not just the first one
        // the ray met: a ladder that ends on a platform with a roof over it, or
        // beside a railing, would otherwise be told there is nowhere to stand
        // because of the wrong surface. The highest one the character actually
        // fits on is the landing.
        float best = float.NaN;
        for (int taken = 0; taken < hits; taken++)
        {
            float candidate = float.NaN;
            for (int index = 0; index < hits; index++)
            {
                float y = _rays[index].point.y;
                if (y > head.y + above || y < head.y - below)
                {
                    continue;
                }

                if (!float.IsNaN(best) && y >= best)
                {
                    continue;
                }

                if (float.IsNaN(candidate) || y > candidate)
                {
                    candidate = y;
                }
            }

            if (float.IsNaN(candidate))
            {
                break;
            }

            if (HasRoomToStand(new Vector3(standing.x, candidate, standing.z), body))
            {
                return new TopLanding(true, new ClimbPoint(run.Top.X, candidate, run.Top.Z));
            }

            best = candidate;
        }

        // Nothing to step onto: the ladder ends in open air, against a wall, or
        // under something too low to stand in. The climber stays on it.
        return TopLanding.None;
    }

    /// <summary>Whether the character's own capsule fits, standing on that
    /// floor. Its own capsule, not a guessed one: a mod that assumes a player's
    /// size is a mod that stops working when the game changes it.</summary>
    private bool HasRoomToStand(Vector3 floor, CapsuleCollider? body)
    {
        float radius = body != null ? Math.Max(0.05f, body.radius * 0.85f) : 0.25f;
        float height = body != null ? Math.Max(radius * 2f, body.height) : 1.8f;
        Vector3 bottom = floor + (Vector3.up * (radius + 0.05f));
        Vector3 top = floor + (Vector3.up * Math.Max(radius + 0.06f, height - radius));
        return !Physics.CheckCapsule(bottom, top, radius, _solidLayerMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Pieces end to end are one climb. Greedy from the piece the
    /// climber is at, up and then down, so a stack of five is one run and two
    /// ladders on opposite walls never become one.</summary>
    private void BuildStack(MeasuredLadder anchor, in LadderGeometry anchorGeometry, ClimbPoint from)
    {
        _stack.Clear();
        _stackPieces.Clear();
        _stack.Add(anchorGeometry);
        _stackPieces.Add(anchor.Ladder);

        bool grew = true;
        while (grew && _stack.Count < 16)
        {
            grew = false;
            for (int index = 0; index < _nearby.Count; index++)
            {
                MeasuredLadder candidate = _nearby[index];
                if (candidate.Ladder == null || _stackPieces.Contains(candidate.Ladder))
                {
                    continue;
                }

                if (!candidate.TryGeometry(from, out LadderGeometry geometry))
                {
                    continue;
                }

                if (!Joins(geometry))
                {
                    continue;
                }

                _stack.Add(geometry);
                _stackPieces.Add(candidate.Ladder);
                grew = true;
            }
        }

        // TryJoin sorts by height internally; the piece list has to follow it,
        // or the runtime would watch the wrong piece for destruction.
        SortStackByHeight();
    }

    private bool Joins(in LadderGeometry candidate)
    {
        for (int index = 0; index < _stack.Count; index++)
        {
            LadderGeometry piece = _stack[index];
            if (piece.StandingSide.Agreement(candidate.StandingSide) < 0.98f)
            {
                continue;
            }

            if (Math.Abs(candidate.Bottom.Y - piece.Top.Y) <= 0.35f &&
                candidate.Bottom.HorizontalDistanceTo(piece.Top) <= 0.35f)
            {
                return true;
            }

            if (Math.Abs(piece.Bottom.Y - candidate.Top.Y) <= 0.35f &&
                piece.Bottom.HorizontalDistanceTo(candidate.Top) <= 0.35f)
            {
                return true;
            }
        }

        return false;
    }

    private void SortStackByHeight()
    {
        for (int outer = 1; outer < _stack.Count; outer++)
        {
            LadderGeometry geometry = _stack[outer];
            Ladder piece = _stackPieces[outer];
            int inner = outer - 1;
            while (inner >= 0 && _stack[inner].Bottom.Y > geometry.Bottom.Y)
            {
                _stack[inner + 1] = _stack[inner];
                _stackPieces[inner + 1] = _stackPieces[inner];
                inner--;
            }

            _stack[inner + 1] = geometry;
            _stackPieces[inner + 1] = piece;
        }
    }

    private MeasuredLadder? Measure(Ladder ladder)
    {
        int id = ladder.GetInstanceID();
        if (_measured.TryGetValue(id, out MeasuredLadder existing) && !existing.HasMoved())
        {
            return existing;
        }

        MeasuredLadder? measured = MeasuredLadder.Of(ladder, _rays, _solidLayerMask);
        if (measured == null)
        {
            _measured.Remove(id);
            return null;
        }

        _measured[id] = measured;
        return measured;
    }

    private void DropDestroyed()
    {
        _stale.Clear();
        foreach (KeyValuePair<int, MeasuredLadder> entry in _measured)
        {
            if (entry.Value.Ladder == null)
            {
                _stale.Add(entry.Key);
            }
        }

        for (int index = 0; index < _stale.Count; index++)
        {
            _measured.Remove(_stale[index]);
        }
    }

    private void EnsureMasks()
    {
        if (_surveyLayerMask == 0)
        {
            _surveyLayerMask = LayerMask.GetMask(SurveyLayers);
        }

        if (_solidLayerMask == 0)
        {
            _solidLayerMask = LayerMask.GetMask(SolidLayers);
        }
    }

    /// <summary>The refusal worth telling a player about when several ladders
    /// all say no: the closest one to being a yes.</summary>
    private static MountRefusal Worse(MountRefusal known, MountRefusal candidate)
    {
        if (known == MountRefusal.Unspecified)
        {
            return candidate;
        }

        return Rank(candidate) > Rank(known) ? candidate : known;
    }

    private static int Rank(MountRefusal refusal)
    {
        switch (refusal)
        {
            case MountRefusal.LookingAway:
                return 6;
            case MountRefusal.WrongSide:
                return 5;
            case MountRefusal.OffToTheSide:
                return 4;
            case MountRefusal.OutOfSpan:
                return 3;
            case MountRefusal.TooFar:
                return 2;
            case MountRefusal.NotClimbable:
                return 1;
            default:
                return 7;
        }
    }

    /// <summary>One measured ladder piece, and the transform it was measured
    /// against so a piece that moves is measured again instead of quietly
    /// lying.</summary>
    private sealed class MeasuredLadder
    {
        private MeasuredLadder(
            Ladder ladder,
            Vector3 position,
            Quaternion rotation,
            Vector3 foot,
            Vector3 head,
            Vector3 depthAxis,
            float width,
            float rungPitch,
            int openSide)
        {
            Ladder = ladder;
            Position = position;
            Rotation = rotation;
            Foot = foot;
            Head = head;
            DepthAxis = depthAxis;
            Width = width;
            RungPitch = rungPitch;
            OpenSide = openSide;
        }

        public Ladder Ladder { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        /// <summary>The foot of the ladder, on its centreline.</summary>
        public Vector3 Foot { get; }

        public Vector3 Head { get; }

        /// <summary>The horizontal direction across the ladder's thin side: one
        /// of its two faces is the rungs, the other is the wall.</summary>
        public Vector3 DepthAxis { get; }

        public float Width { get; }

        public float RungPitch { get; }

        /// <summary>+1 when the open air is along <see cref="DepthAxis"/>, -1
        /// when it is the other way, 0 when the probe could not tell and the
        /// climber's own side decides.</summary>
        public int OpenSide { get; }

        public bool HasMoved() =>
            Ladder == null ||
            (Ladder.transform.position - Position).sqrMagnitude > 0.0001f ||
            Quaternion.Angle(Ladder.transform.rotation, Rotation) > 0.5f;

        /// <summary>The domain's view of this piece, from where a character
        /// stands. The standing side is the measurement's when the probe was
        /// sure, and otherwise the side the character is already on, which is
        /// the only honest answer for a free-standing ladder.</summary>
        public bool TryGeometry(ClimbPoint from, out LadderGeometry geometry)
        {
            geometry = default;
            if (Ladder == null)
            {
                return false;
            }

            int side = OpenSide;
            if (side == 0)
            {
                Vector3 centre = (Foot + Head) * 0.5f;
                float towards = ((from.X - centre.x) * DepthAxis.x) + ((from.Z - centre.z) * DepthAxis.z);
                side = towards >= 0f ? 1 : -1;
            }

            ClimbHeading standingSide = ClimbHeading.FromXz(DepthAxis.x * side, DepthAxis.z * side);
            return LadderGeometry.TryMeasure(
                new ClimbPoint(Foot.x, Foot.y, Foot.z),
                new ClimbPoint(Head.x, Head.y, Head.z),
                standingSide,
                Width,
                RungPitch,
                out geometry);
        }

        /// <summary>Measures a piece from the game: its mesh bounds in its own
        /// frame give the ends, the width and the thin face; a short ray on each
        /// face says which one is against something.</summary>
        public static MeasuredLadder? Of(Ladder ladder, RaycastHit[] rays, int solidMask)
        {
            Transform transform = ladder.transform;
            if (!TryLocalBounds(ladder.gameObject, transform, out Bounds local))
            {
                return null;
            }

            float depthX = local.size.x;
            float depthZ = local.size.z;
            bool thinInX = depthX <= depthZ;
            Vector3 localAxis = thinInX ? Vector3.right : Vector3.forward;
            float width = thinInX ? depthZ : depthX;
            if (width <= 0f || width > 4f)
            {
                return null;
            }

            Vector3 depthAxis = transform.TransformDirection(localAxis);
            depthAxis.y = 0f;
            if (depthAxis.sqrMagnitude <= 1e-6f)
            {
                return null;
            }

            depthAxis.Normalize();

            Vector3 foot = transform.TransformPoint(new Vector3(local.center.x, local.min.y, local.center.z));
            Vector3 head = transform.TransformPoint(new Vector3(local.center.x, local.max.y, local.center.z));
            if (head.y - foot.y < LadderGeometry.MinimumClimbableHeight)
            {
                return null;
            }

            return new MeasuredLadder(
                ladder,
                transform.position,
                transform.rotation,
                foot,
                head,
                depthAxis,
                width,
                MeasureRungs(ladder, head.y - foot.y),
                ProbeOpenSide(ladder, (foot + head) * 0.5f, depthAxis, rays, solidMask));
        }

        /// <summary>The piece's own bounds, in its own frame, from its meshes.
        /// Colliders are the fallback for a piece whose art is not a mesh
        /// renderer; a piece with neither cannot be measured and is not
        /// climbable.</summary>
        private static bool TryLocalBounds(GameObject root, Transform frame, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Matrix4x4 toLocal = frame.worldToLocalMatrix;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                Matrix4x4 meshToLocal = toLocal * filter.transform.localToWorldMatrix;
                Encapsulate(ref bounds, ref any, filter.sharedMesh.bounds, meshToLocal);
            }

            if (!any)
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    if (collider == null || collider.isTrigger)
                    {
                        continue;
                    }

                    // World bounds, brought into the piece's frame: less exact
                    // than a mesh, and only used when there is no mesh.
                    Encapsulate(ref bounds, ref any, collider.bounds, toLocal);
                }
            }

            return any;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Bounds source, Matrix4x4 into)
        {
            Vector3 centre = source.center;
            Vector3 extents = source.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    centre.x + (((corner & 1) == 0) ? -extents.x : extents.x),
                    centre.y + (((corner & 2) == 0) ? -extents.y : extents.y),
                    centre.z + (((corner & 4) == 0) ? -extents.z : extents.z));
                Vector3 local = into.MultiplyPoint3x4(point);
                if (!any)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    any = true;
                    continue;
                }

                bounds.Encapsulate(local);
            }
        }

        /// <summary>Rung spacing from the piece's own child transforms, which is
        /// a measurement and not a promise: a piece whose rungs are not named
        /// falls back to the conventional spacing, and the lead's `cf_ladders`
        /// audit is what settles the real number.</summary>
        private static float MeasureRungs(Ladder ladder, float height)
        {
            var heights = new List<float>();
            foreach (Transform child in ladder.GetComponentsInChildren<Transform>())
            {
                if (child == null || child.gameObject == ladder.gameObject)
                {
                    continue;
                }

                string name = child.name.ToLowerInvariant();
                if (name.Contains("rung") || name.Contains("step") || name.Contains("bar"))
                {
                    heights.Add(child.position.y);
                }
            }

            if (heights.Count < 2)
            {
                return LadderGeometry.ConventionalRungPitch;
            }

            heights.Sort();
            float total = 0f;
            for (int index = 1; index < heights.Count; index++)
            {
                total += heights[index] - heights[index - 1];
            }

            float pitch = total / (heights.Count - 1);
            if (pitch <= 0.05f || pitch > 1f || pitch > height)
            {
                return LadderGeometry.ConventionalRungPitch;
            }

            return pitch;
        }

        /// <summary>Which face has the open air. A ladder is fixed to something,
        /// and the side that something is on has no rungs to climb.</summary>
        private static int ProbeOpenSide(
            Ladder ladder, Vector3 middle, Vector3 depthAxis, RaycastHit[] rays, int solidMask)
        {
            bool forwardBlocked = Blocked(ladder, middle, depthAxis, rays, solidMask);
            bool backBlocked = Blocked(ladder, middle, -depthAxis, rays, solidMask);
            if (forwardBlocked == backBlocked)
            {
                return 0;
            }

            return forwardBlocked ? -1 : 1;
        }

        private static bool Blocked(
            Ladder ladder, Vector3 middle, Vector3 direction, RaycastHit[] rays, int solidMask)
        {
            // Started clear of the piece's own colliders, because a ray that
            // begins inside a collider does not report it and the answer would
            // depend on the mesh.
            Vector3 origin = middle + (direction * 0.25f);
            int hits = Physics.RaycastNonAlloc(
                origin, direction, rays, 0.5f, solidMask, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hits; index++)
            {
                Collider collider = rays[index].collider;
                if (collider == null)
                {
                    continue;
                }

                if (collider.GetComponentInParent<Ladder>() == ladder)
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
