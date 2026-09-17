using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Risk;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;

/// <summary>Decides whether a loaded cart fits a line Gunnar can walk
/// (CART-05), and where along it the cart may be left standing.
///
/// The line is sampled every <see cref="HaulLimits.SampleSpacingMetres"/>. At
/// each sample the cart's pose is predicted with <see cref="CartTrackPredictor"/>
/// - the trailing cart cuts inside turns - and checked, in this order, for
/// loaded ground, water and lava, something solid under Gunnar, the axle and
/// both wheels, ledges and drops, the tilt across the cart, the running grade
/// (uphill and downhill: a loaded cart is as dangerous going down), the
/// calibrated climb and descent data when given, doorways, a turn sharper than
/// a hitch follows, and finally a swept box of the cart's width plus
/// <see cref="HaulLimits.SideClearanceMetres"/> each side from the previous
/// pose. The first problem along the route decides.
///
/// A blocked sweep is not always the end. The navmesh keeps a walker only its
/// own radius from walls, and a cart is wider; so when a sweep is blocked the
/// free room either side is measured, and if the passage is wide enough the line
/// is moved sideways to centre the cart in it, smoothly and well before the
/// obstacle, and checked again from where it changed. A route is corrected at
/// most once per cart-and-hitch length of it (plus one), and each correction
/// spends probes, so this always ends. The ends of the route never move.
///
/// Game-free: every question goes through <see cref="ICartTerrainProbe"/>, each
/// clearance question is paid for from the plan's <see cref="CartProbeAllowance"/>,
/// and an exception from a probe becomes a refusal, never an escape.</summary>
internal sealed class CartRouteEvaluator
{
    private readonly HaulLimits _limits;
    private readonly ICartTerrainProbe _probe;
    private readonly LoadModel? _climb;
    private readonly RiskModel? _descent;

    public CartRouteEvaluator(HaulLimits limits, ICartTerrainProbe probe, LoadModel? climb = null, RiskModel? descent = null)
    {
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits))).Validate();
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _climb = climb;
        _descent = descent;
    }

    public HaulLimits Limits => _limits;

    /// <summary>Evaluates Gunnar's <paramref name="route"/> for a cart of
    /// <paramref name="footprint"/> weighing <paramref name="loadedMassKg"/>.
    /// <paramref name="cartPosition"/>, when known, is where the cart stands now,
    /// so the first turn is predicted from the cart's real side.
    /// <paramref name="ignoreStartOverlaps"/> lets the very first sweep start
    /// inside things the cart is already touching. Never throws.</summary>
    public CartRouteAssessment Evaluate(
        IReadOnlyList<WorkPoint> route,
        CartFootprint footprint,
        float loadedMassKg,
        WorkPoint? cartPosition,
        bool ignoreStartOverlaps,
        CartProbeAllowance allowance)
    {
        if (allowance == null)
        {
            throw new ArgumentNullException(nameof(allowance));
        }

        var run = new Run(this, route, footprint, loadedMassKg, cartPosition, ignoreStartOverlaps, allowance);
        try
        {
            return run.Execute();
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            return run.Fault();
        }
    }

    private struct PoseGround
    {
        public bool Checked;
        public float Puller;
        public float Axle;
        public float Cross;
        public bool HasGrade;
        public float Grade;
    }

    private sealed class Run
    {
        private readonly CartRouteEvaluator _owner;
        private readonly IReadOnlyList<WorkPoint> _route;
        private readonly CartFootprint _footprint;
        private readonly float _mass;
        private readonly WorkPoint? _cartPosition;
        private readonly bool _ignoreStartOverlaps;
        private readonly CartProbeAllowance _allowance;
        private readonly int _probesAtStart;
        private readonly List<CartDoorway> _doorways = new List<CartDoorway>();
        private readonly CartGroundSample[] _samples = new CartGroundSample[4];
        private readonly WorkPoint[] _samplePoints = new WorkPoint[4];

        private float _width;
        private float _length;
        private float _hitch;
        private float _halfCorridor;
        private float _spacing;
        private float _maxGrade;
        private float _routeLength;

        private WorkPoint[] _base = Array.Empty<WorkPoint>();
        private WorkPoint[] _puller = Array.Empty<WorkPoint>();
        private float[] _shiftX = Array.Empty<float>();
        private float[] _shiftZ = Array.Empty<float>();
        private float[] _along = Array.Empty<float>();
        private CartPose[] _poses = Array.Empty<CartPose>();
        private PoseGround[] _ground = Array.Empty<PoseGround>();
        private CartPose[] _subPoses = new CartPose[4];

        private int _repairsLeft;
        private int _repairs;
        private int _groundSamples;
        private int _doorScans;
        private bool _faulted;

        public Run(
            CartRouteEvaluator owner,
            IReadOnlyList<WorkPoint> route,
            CartFootprint footprint,
            float mass,
            WorkPoint? cartPosition,
            bool ignoreStartOverlaps,
            CartProbeAllowance allowance)
        {
            _owner = owner;
            _route = route;
            _footprint = footprint;
            _mass = mass;
            _cartPosition = cartPosition;
            _ignoreStartOverlaps = ignoreStartOverlaps;
            _allowance = allowance;
            _probesAtStart = allowance.Used;
        }

        public CartRouteAssessment Execute()
        {
            if (_route == null || _route.Count < 2)
            {
                return Refuse(CartRouteFinding.InvalidRequest, null, 0f);
            }

            for (int index = 0; index < _route.Count; index++)
            {
                if (!_route[index].IsFinite)
                {
                    return Refuse(CartRouteFinding.InvalidRequest, null, 0f);
                }
            }

            _width = _footprint.WidthMetres;
            _length = _footprint.LengthMetres;
            _hitch = _footprint.HitchLengthMetres;
            if (!(_width > 0f) || !(_length > 0f) || !(_hitch >= 0f) ||
                !CartRouteGeometry.IsFiniteValue(_width) || !CartRouteGeometry.IsFiniteValue(_length) ||
                !CartRouteGeometry.IsFiniteValue(_hitch))
            {
                return Refuse(CartRouteFinding.InvalidRequest, null, 0f);
            }

            HaulLimits limits = _owner._limits;
            _maxGrade = limits.MaxGradeRatio;
            _spacing = limits.SampleSpacingMetres;
            _halfCorridor = (_width * 0.5f) + limits.SideClearanceMetres;

            _routeLength = 0f;
            for (int index = 1; index < _route.Count; index++)
            {
                _routeLength += CartRouteGeometry.FlatDistance(_route[index - 1], _route[index]);
            }

            if (_routeLength > limits.MaxLegMetres + 1e-3f)
            {
                return Refuse(CartRouteFinding.LegTooLong, _route[_route.Count - 1], _routeLength);
            }

            BuildSamples();
            int count = _base.Length;
            if (count < 2)
            {
                return Refuse(CartRouteFinding.InvalidRequest, _route[0], 0f);
            }

            _repairsLeft = (int)Math.Ceiling(_routeLength / Math.Max(_spacing, _hitch + _length)) + 1;

            if (!ScanDoors())
            {
                return Refuse(
                    _faulted ? CartRouteFinding.ProbeFaulted : CartRouteFinding.DoorScanUnreadable, _route[0], 0f);
            }

            PredictFrom(0);

            int sample = 0;
            while (sample < count)
            {
                CartRouteAssessment? refusal = CheckGround(sample);
                if (refusal != null)
                {
                    return refusal;
                }

                if (sample > 0)
                {
                    refusal = CheckDoors(sample);
                    if (refusal != null)
                    {
                        return refusal;
                    }

                    if (_poses[sample].ArticulationDegrees > CartRouteGeometry.MaxArticulationDegrees)
                    {
                        return Refuse(CartRouteFinding.TurnTooSharp, _puller[sample], _along[sample]);
                    }

                    refusal = SweepSegment(sample, out int restartAt);
                    if (refusal != null)
                    {
                        return refusal;
                    }

                    if (restartAt >= 0)
                    {
                        sample = restartAt;
                        continue;
                    }
                }

                sample++;
            }

            return Finish();
        }

        public CartRouteAssessment Fault()
        {
            return CartRouteAssessment.Refused(CartRouteFinding.ProbeFaulted, null, 0f, string.Empty, Costs());
        }

        private void BuildSamples()
        {
            var points = new List<WorkPoint>(_route.Count + (int)Math.Ceiling(_routeLength / _spacing) + 1)
            {
                _route[0],
            };

            for (int index = 1; index < _route.Count; index++)
            {
                WorkPoint from = points[points.Count - 1];
                WorkPoint to = _route[index];
                float distance = CartRouteGeometry.FlatDistance(from, to);
                if (distance < CartRouteGeometry.SamePointMetres)
                {
                    continue;
                }

                int interior = (int)Math.Floor((distance - CartRouteGeometry.SamePointMetres) / _spacing);
                for (int step = 1; step <= interior; step++)
                {
                    points.Add(CartRouteGeometry.Lerp(from, to, _spacing * step / distance));
                }

                points.Add(to);
            }

            int count = points.Count;
            _base = points.ToArray();
            _puller = points.ToArray();
            _shiftX = new float[count];
            _shiftZ = new float[count];
            _along = new float[count];
            _poses = new CartPose[count];
            _ground = new PoseGround[count];
            RecomputeAlong(1);
        }

        private void RecomputeAlong(int from)
        {
            for (int index = Math.Max(1, from); index < _puller.Length; index++)
            {
                _along[index] = _along[index - 1] + CartRouteGeometry.FlatDistance(_puller[index - 1], _puller[index]);
            }
        }

        private void PredictFrom(int start)
        {
            if (start <= 0)
            {
                _poses[0] = CartTrackPredictor.Start(_puller, _hitch, _cartPosition);
                start = 1;
            }

            for (int index = start; index < _puller.Length; index++)
            {
                _poses[index] = CartTrackPredictor.Advance(_poses[index - 1], _puller[index], _hitch);
            }
        }

        private bool ScanDoors()
        {
            float minX = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxZ = float.MinValue;
            float sumY = 0f;
            for (int index = 0; index < _route.Count; index++)
            {
                WorkPoint point = _route[index];
                minX = Math.Min(minX, point.X);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxZ = Math.Max(maxZ, point.Z);
                sumY += point.Y;
            }

            var centre = new WorkPoint((minX + maxX) * 0.5f, sumY / _route.Count, (minZ + maxZ) * 0.5f);
            float halfDiagonal = CartRouteGeometry.FlatLength(maxX - minX, maxZ - minZ) * 0.5f;

            // Far enough to see any doorway a corrected line or the cart's box
            // could still reach: the route itself, one hitch and cart length
            // beyond it, the corridor, and a door piece's own generous half width.
            float radius = halfDiagonal + _hitch + _length + (2f * _halfCorridor) + 2f;

            _doorScans++;
            try
            {
                return _owner._probe.TryFindDoorways(centre, radius, _doorways);
            }
            catch (Exception)
            {
                _faulted = true;
                _doorways.Clear();
                return false;
            }
        }

        private CartRouteAssessment? CheckGround(int index)
        {
            CartPose pose = _poses[index];
            WorkPoint puller = _puller[index];
            float nearPuller = _base[index].Y;
            float nearAxle = index > 0 && _ground[index - 1].Checked ? _ground[index - 1].Axle : nearPuller;

            CartGroundSample atPuller = Ground(puller.X, puller.Z, nearPuller);
            CartGroundSample atAxle = Ground(pose.Axle.X, pose.Axle.Z, nearAxle);
            float nearWheels = atAxle.Status == CartGroundStatus.Surface ? atAxle.Height : nearAxle;

            float halfWidth = _width * 0.5f;
            float rightX = pose.HeadingZ;
            float rightZ = -pose.HeadingX;
            var left = new WorkPoint(pose.Axle.X - (rightX * halfWidth), nearWheels, pose.Axle.Z - (rightZ * halfWidth));
            var right = new WorkPoint(pose.Axle.X + (rightX * halfWidth), nearWheels, pose.Axle.Z + (rightZ * halfWidth));
            CartGroundSample atLeft = Ground(left.X, left.Z, nearWheels);
            CartGroundSample atRight = Ground(right.X, right.Z, nearWheels);

            _samples[0] = atPuller;
            _samples[1] = atAxle;
            _samples[2] = atLeft;
            _samples[3] = atRight;
            _samplePoints[0] = CartRouteGeometry.WithHeight(puller, nearPuller);
            _samplePoints[1] = CartRouteGeometry.WithHeight(pose.Axle, nearAxle);
            _samplePoints[2] = left;
            _samplePoints[3] = right;
            float along = _along[index];

            for (int which = 0; which < 4; which++)
            {
                if (_samples[which].Status == CartGroundStatus.Unloaded)
                {
                    return Refuse(CartRouteFinding.Unloaded, _samplePoints[which], along);
                }
            }

            for (int which = 0; which < 4; which++)
            {
                CartGroundSample sample = _samples[which];
                bool unreadable = sample.Status == CartGroundStatus.Unreadable ||
                    sample.Status == CartGroundStatus.Unspecified ||
                    (sample.Status == CartGroundStatus.Surface && !CartRouteGeometry.IsFiniteValue(sample.Height)) ||
                    !CartRouteGeometry.IsFiniteValue(sample.LiquidDepthMetres);
                if (unreadable)
                {
                    return Refuse(
                        _faulted ? CartRouteFinding.ProbeFaulted : CartRouteFinding.GroundUnreadable,
                        _samplePoints[which], along);
                }
            }

            for (int which = 0; which < 4; which++)
            {
                if (_samples[which].Lava)
                {
                    return Refuse(CartRouteFinding.Lava, _samplePoints[which], along);
                }

                if (_samples[which].LiquidDepthMetres > 0f)
                {
                    return Refuse(CartRouteFinding.Water, _samplePoints[which], along);
                }
            }

            if (atPuller.Status == CartGroundStatus.NoSurface)
            {
                return Refuse(CartRouteFinding.NoSurface, _samplePoints[0], along);
            }

            if (atAxle.Status == CartGroundStatus.NoSurface)
            {
                return Refuse(CartRouteFinding.NoSurface, _samplePoints[1], along);
            }

            if (atLeft.Status == CartGroundStatus.NoSurface)
            {
                return Refuse(CartRouteFinding.WheelUnsupported, left, along);
            }

            if (atRight.Status == CartGroundStatus.NoSurface)
            {
                return Refuse(CartRouteFinding.WheelUnsupported, right, along);
            }

            // A wheel far below the axle hangs over an edge; far above it rides a
            // ledge. Within a step plus what the grade allows across half the
            // track, it is only a tilt, judged next.
            float wheelAllowance = CartRouteGeometry.StepMetres + (_maxGrade * halfWidth);
            if (atAxle.Height - atLeft.Height > wheelAllowance)
            {
                return Refuse(CartRouteFinding.WheelUnsupported, left, along);
            }

            if (atAxle.Height - atRight.Height > wheelAllowance)
            {
                return Refuse(CartRouteFinding.WheelUnsupported, right, along);
            }

            float cross = (atLeft.Height - atRight.Height) / _width;
            if (atLeft.Height - atAxle.Height > wheelAllowance || atRight.Height - atAxle.Height > wheelAllowance ||
                Math.Abs(cross) > _maxGrade)
            {
                return Refuse(CartRouteFinding.CrossSlopeTooSteep, _samplePoints[1], along);
            }

            if (index > 0)
            {
                PoseGround previous = _ground[index - 1];
                CartPose previousPose = _poses[index - 1];

                CartRouteAssessment? step = CheckStep(
                    previous.Axle, atAxle.Height,
                    CartRouteGeometry.FlatDistance(previousPose.Axle, pose.Axle),
                    CartRouteGeometry.WithHeight(pose.Axle, atAxle.Height), along);
                if (step != null)
                {
                    return step;
                }

                step = CheckStep(
                    previous.Puller, atPuller.Height,
                    CartRouteGeometry.FlatDistance(_puller[index - 1], puller),
                    CartRouteGeometry.WithHeight(puller, atPuller.Height), along);
                if (step != null)
                {
                    return step;
                }
            }

            var ground = new PoseGround
            {
                Checked = true,
                Puller = atPuller.Height,
                Axle = atAxle.Height,
                Cross = cross,
            };

            if (index > 0)
            {
                // The grade over about twice the sample spacing behind the axle:
                // the same three-metre run Teamster's Cart Status readout uses at
                // the default spacing, so a player can check a refusal with it.
                int back = 0;
                float run = 0f;
                for (int earlier = index - 1; earlier >= 0; earlier--)
                {
                    float distance = CartRouteGeometry.FlatDistance(_poses[earlier].Axle, pose.Axle);
                    back = earlier;
                    run = distance;
                    if (distance >= 2f * _spacing)
                    {
                        break;
                    }
                }

                if (run >= 0.5f * _spacing)
                {
                    ground.HasGrade = true;
                    ground.Grade = (atAxle.Height - _ground[back].Axle) / run;
                    if (Math.Abs(ground.Grade) > _maxGrade)
                    {
                        return Refuse(
                            CartRouteFinding.RunningGradeTooSteep,
                            CartRouteGeometry.WithHeight(pose.Axle, atAxle.Height), along);
                    }

                    CartRouteAssessment? calibrated = CheckCalibration(ground.Grade, pose, atAxle.Height, along);
                    if (calibrated != null)
                    {
                        return calibrated;
                    }
                }
            }

            _ground[index] = ground;
            return null;
        }

        private CartRouteAssessment? CheckStep(float fromHeight, float toHeight, float run, WorkPoint at, float along)
        {
            float allowance = CartRouteGeometry.StepMetres + (_maxGrade * run);
            float rise = toHeight - fromHeight;
            if (rise < -allowance)
            {
                return Refuse(CartRouteFinding.Drop, at, along);
            }

            if (rise > allowance)
            {
                return Refuse(CartRouteFinding.LedgeUp, at, along);
            }

            return null;
        }

        private CartRouteAssessment? CheckCalibration(float grade, CartPose pose, float height, float along)
        {
            if (!(_mass > 0f) || !CartRouteGeometry.IsFiniteValue(_mass))
            {
                return null;
            }

            if (grade > 0f && _owner._climb != null)
            {
                LoadVerdict verdict = _owner._climb.Query(grade * 100f, _mass);
                if (verdict.Climbability == Climbability.No)
                {
                    return Refuse(
                        CartRouteFinding.CalibratedClimbRefused, CartRouteGeometry.WithHeight(pose.Axle, height),
                        along, verdict.Explanation);
                }
            }

            if (grade < 0f && _owner._descent != null)
            {
                // Every leg starts from standing and Gunnar never runs, so the
                // descent is asked about at walking entry: the slowest rows.
                RiskVerdict risk = _owner._descent.Query(-grade * 100f, _mass, 0f);
                if (risk.Level == RiskLevel.Caution || risk.Level == RiskLevel.Danger)
                {
                    return Refuse(
                        CartRouteFinding.CalibratedDescentRefused, CartRouteGeometry.WithHeight(pose.Axle, height),
                        along, risk.Explanation);
                }
            }

            return null;
        }

        private CartRouteAssessment? CheckDoors(int index)
        {
            if (_doorways.Count == 0)
            {
                return null;
            }

            WorkPoint pullerFrom = CartRouteGeometry.WithHeight(_puller[index - 1], _ground[index - 1].Puller);
            WorkPoint pullerTo = CartRouteGeometry.WithHeight(_puller[index], _ground[index].Puller);
            WorkPoint boxFrom = _poses[index - 1].BoxCentre(_length, _ground[index - 1].Axle);
            WorkPoint boxTo = _poses[index].BoxCentre(_length, _ground[index].Axle);

            for (int door = 0; door < _doorways.Count; door++)
            {
                CartDoorway doorway = _doorways[door];
                if (doorway.IsCrossedBy(pullerFrom, pullerTo, _halfCorridor) ||
                    doorway.IsCrossedBy(boxFrom, boxTo, _halfCorridor))
                {
                    return Refuse(CartRouteFinding.Doorway, doorway.FloorCentre, _along[index]);
                }
            }

            return null;
        }

        private CartRouteAssessment? SweepSegment(int index, out int restartAt)
        {
            restartAt = -1;
            CartPose from = _poses[index - 1];
            CartPose to = _poses[index];
            float turn = CartRouteGeometry.AngleDegrees(from.HeadingX, from.HeadingZ, to.HeadingX, to.HeadingZ);
            int parts = Math.Max(1, (int)Math.Ceiling(turn / CartRouteGeometry.MaxSweepTurnDegrees));
            if (parts > 1)
            {
                if (_subPoses.Length < parts)
                {
                    _subPoses = new CartPose[parts];
                }

                CartPose end = CartTrackPredictor.Advance(from, _puller[index], _hitch, _subPoses, parts);
                parts = Math.Min(parts, Math.Max(1, (int)Math.Ceiling(
                    CartRouteGeometry.FlatDistance(from.Puller, end.Puller) / CartRouteGeometry.PredictionStepMetres)));
            }

            float heightFrom = _ground[index - 1].Axle;
            float heightTo = _ground[index].Axle;
            float alongFrom = _along[index - 1];
            float alongTo = _along[index];

            for (int part = 0; part < parts; part++)
            {
                CartPose a = part == 0 ? from : _subPoses[part - 1];
                CartPose b = part == parts - 1 ? to : _subPoses[part];
                float ta = (float)part / parts;
                float tb = (float)(part + 1) / parts;
                WorkPoint centreA = a.BoxCentre(_length, heightFrom + ((heightTo - heightFrom) * ta));
                WorkPoint centreB = b.BoxCentre(_length, heightFrom + ((heightTo - heightFrom) * tb));

                float headingX = a.HeadingX + b.HeadingX;
                float headingZ = a.HeadingZ + b.HeadingZ;
                float headingLength = CartRouteGeometry.FlatLength(headingX, headingZ);
                if (headingLength > 1e-4f)
                {
                    headingX /= headingLength;
                    headingZ /= headingLength;
                }
                else
                {
                    headingX = b.HeadingX;
                    headingZ = b.HeadingZ;
                }

                // A box turning on the way is widened by how far its ends swing
                // off the average heading, so the one straight sweep still covers it.
                float partTurn = CartRouteGeometry.AngleDegrees(a.HeadingX, a.HeadingZ, b.HeadingX, b.HeadingZ);
                float swing = (float)Math.Sin(partTurn * Math.PI / 360d);
                float halfWidth = _halfCorridor + (0.5f * _length * swing);
                float halfLength = (0.5f * _length) + (_halfCorridor * swing);
                bool ignoreStart = _ignoreStartOverlaps && index == 1 && part == 0;

                if (!_allowance.TryTake())
                {
                    return Refuse(CartRouteFinding.ProbeBudget, centreA, alongFrom + ((alongTo - alongFrom) * ta));
                }

                CartClearanceSample result = Sweep(centreA, centreB, headingX, headingZ, halfWidth, halfLength, ignoreStart);
                if (result.Status == CartClearanceStatus.Clear)
                {
                    continue;
                }

                float alongA = alongFrom + ((alongTo - alongFrom) * ta);
                if (result.Status != CartClearanceStatus.Blocked || !CartRouteGeometry.IsFiniteValue(result.BlockedAtMetres))
                {
                    return Refuse(
                        _faulted ? CartRouteFinding.ProbeFaulted : CartRouteFinding.ClearanceUnreadable, centreA, alongA);
                }

                float sweepLength = Distance3D(centreA, centreB);
                float fraction = sweepLength > 1e-4f ? Clamp01(result.BlockedAtMetres / sweepLength) : 0f;
                WorkPoint hit = CartRouteGeometry.Lerp(centreA, centreB, fraction);
                float alongHit = alongFrom + ((alongTo - alongFrom) * (ta + ((tb - ta) * fraction)));
                bool turning = Math.Max(a.ArticulationDegrees, b.ArticulationDegrees) > CartRouteGeometry.MaxSweepTurnDegrees;
                return Repair(
                    index, hit, headingX, headingZ, halfWidth, halfLength, alongHit, turning, result.Obstacle, out restartAt);
            }

            return null;
        }

        /// <summary>Measures the free room either side of the box that was
        /// blocked - with the same extents the sweep used, widened for a turn -
        /// and, when it fits, moves the line to centre the box in that room.
        /// </summary>
        private CartRouteAssessment? Repair(
            int index, WorkPoint hit, float headingX, float headingZ, float needHalfWidth, float needHalfLength,
            float alongHit, bool turning, string obstacle, out int restartAt)
        {
            restartAt = -1;
            CartRouteFinding blocked = turning ? CartRouteFinding.InsideCornerClipped : CartRouteFinding.Obstructed;
            if (_repairsLeft <= 0)
            {
                return Refuse(CartRouteFinding.RepairsExhausted, hit, alongHit, obstacle);
            }

            if (!_allowance.TryTake(2))
            {
                return Refuse(CartRouteFinding.ProbeBudget, hit, alongHit, obstacle);
            }

            float leftX = -headingZ;
            float leftZ = headingX;
            float reach = 2f * needHalfWidth;
            CartClearanceSample towardsLeft = Sweep(
                hit, CartRouteGeometry.Offset(hit, leftX, leftZ, reach), headingX, headingZ,
                CartRouteGeometry.LateralSlabHalfMetres, needHalfLength, false);
            CartClearanceSample towardsRight = Sweep(
                hit, CartRouteGeometry.Offset(hit, -leftX, -leftZ, reach), headingX, headingZ,
                CartRouteGeometry.LateralSlabHalfMetres, needHalfLength, false);
            if (!IsMeasured(towardsLeft) || !IsMeasured(towardsRight))
            {
                return Refuse(
                    _faulted ? CartRouteFinding.ProbeFaulted : CartRouteFinding.ClearanceUnreadable, hit, alongHit, obstacle);
            }

            float freeLeft = FreeRoom(towardsLeft, reach);
            float freeRight = FreeRoom(towardsRight, reach);
            if (freeLeft + freeRight < 2f * needHalfWidth)
            {
                return Refuse(CartRouteFinding.MeasuredTooNarrow, hit, alongHit, obstacle);
            }

            // Centre the cart in the room measured. A trailing cart touches an
            // obstacle first at its shallowest, so the least shift that clears the
            // first contact rarely clears the turn; the middle of the free room
            // does, and it never leaves the room that was measured clear.
            // The sweep touched something; a side measured within a few
            // centimetres of the box's own half width is that contact, seen at
            // the probes' resolution.
            float shift = (freeLeft - freeRight) * 0.5f;
            float tighter = Math.Min(freeLeft, freeRight);
            if (tighter >= needHalfWidth + CartRouteGeometry.SamePointMetres ||
                Math.Abs(shift) < CartRouteGeometry.SamePointMetres)
            {
                // There is room either side, so what blocks the cart is ahead of
                // it, not beside it: moving the line sideways cannot help.
                return Refuse(blocked, hit, alongHit, obstacle);
            }

            int firstChanged = ApplyShift(leftX * shift, leftZ * shift, alongHit);
            if (firstChanged < 0)
            {
                return Refuse(blocked, hit, alongHit, obstacle);
            }

            _repairsLeft--;
            _repairs++;
            restartAt = Math.Min(firstChanged, index);
            PredictFrom(restartAt);
            return null;
        }

        /// <summary>Moves Gunnar's line by (<paramref name="dx"/>,
        /// <paramref name="dz"/>) around <paramref name="alongHit"/>: fully from
        /// three hitch lengths and a cart length before it (so the trailing cart
        /// has settled onto the new line by then) to a cart length after it (so
        /// the cart's tail clears), easing in and out over a hitch length. The
        /// ends of the route never move. Returns the first sample moved, or -1.
        /// </summary>
        private int ApplyShift(float dx, float dz, float alongHit)
        {
            float ramp = Math.Max(_hitch, _spacing);
            float fullStart = alongHit - (3f * _hitch) - _length;
            float fullEnd = alongHit + _length;
            int last = _puller.Length - 1;
            int first = -1;

            for (int index = 1; index < last; index++)
            {
                float along = _along[index];
                float weight;
                if (along < fullStart - ramp || along > fullEnd + ramp)
                {
                    continue;
                }

                if (along < fullStart)
                {
                    weight = (along - (fullStart - ramp)) / ramp;
                }
                else if (along > fullEnd)
                {
                    weight = ((fullEnd + ramp) - along) / ramp;
                }
                else
                {
                    weight = 1f;
                }

                if (!(weight > 0f))
                {
                    continue;
                }

                _shiftX[index] += dx * weight;
                _shiftZ[index] += dz * weight;
                _puller[index] = new WorkPoint(
                    _base[index].X + _shiftX[index], _base[index].Y, _base[index].Z + _shiftZ[index]);
                if (first < 0)
                {
                    first = index;
                }
            }

            if (first >= 0)
            {
                RecomputeAlong(first);
            }

            return first;
        }

        private CartRouteAssessment Finish()
        {
            int count = _puller.Length;
            float steepest = 0f;
            float widestArticulation = 0f;
            for (int index = 1; index < count; index++)
            {
                if (_ground[index].HasGrade)
                {
                    steepest = Math.Max(steepest, Math.Abs(_ground[index].Grade));
                }

                widestArticulation = Math.Max(widestArticulation, _poses[index].ArticulationDegrees);
            }

            float level = CartRouteGeometry.LevelGradeRatio;
            int stop = -1;
            for (int index = count - 1; index >= 1; index--)
            {
                PoseGround ground = _ground[index];
                if (ground.HasGrade && Math.Abs(ground.Grade) <= level && Math.Abs(ground.Cross) <= level)
                {
                    stop = index;
                    break;
                }
            }

            if (stop < 0)
            {
                return Refuse(
                    CartRouteFinding.NoLevelStop,
                    CartRouteGeometry.WithHeight(_puller[count - 1], _ground[count - 1].Puller),
                    _along[count - 1]);
            }

            var pullerTrack = new WorkPoint[count];
            var cartTrack = new WorkPoint[count];
            for (int index = 0; index < count; index++)
            {
                pullerTrack[index] = CartRouteGeometry.WithHeight(_puller[index], _ground[index].Puller);
                cartTrack[index] = CartRouteGeometry.WithHeight(_poses[index].Axle, _ground[index].Axle);
            }

            var kept = new List<int>(count) { 0 };
            for (int index = 1; index < count - 1; index++)
            {
                if (index == stop || Bends(kept[kept.Count - 1], index))
                {
                    kept.Add(index);
                }
            }

            kept.Add(count - 1);
            var waypoints = new WorkPoint[kept.Count];
            int stopWaypoint = -1;
            for (int slot = 0; slot < kept.Count; slot++)
            {
                waypoints[slot] = pullerTrack[kept[slot]];
                if (kept[slot] == stop)
                {
                    stopWaypoint = slot;
                }
            }

            return CartRouteAssessment.Suitable(
                waypoints,
                stopWaypoint,
                pullerTrack,
                cartTrack,
                stop,
                _along[count - 1],
                steepest,
                2f * _halfCorridor,
                _along[count - 1] - _along[stop],
                widestArticulation,
                Costs());
        }

        /// <summary>Whether dropping every sample after
        /// <paramref name="keptIndex"/> up to and including
        /// <paramref name="index"/> would move the line: some such sample lies
        /// off the straight line from the kept one to the sample after.</summary>
        private bool Bends(int keptIndex, int index)
        {
            WorkPoint from = _puller[keptIndex];
            WorkPoint to = _puller[index + 1];
            for (int between = keptIndex + 1; between <= index; between++)
            {
                if (CartRouteGeometry.FlatDistanceToSegment(_puller[between], from, to, out _) >
                    CartRouteGeometry.SamePointMetres)
                {
                    return true;
                }
            }

            return false;
        }

        private CartGroundSample Ground(float x, float z, float nearHeight)
        {
            _groundSamples++;
            try
            {
                return _owner._probe.SampleGround(x, z, nearHeight);
            }
            catch (Exception)
            {
                _faulted = true;
                return CartGroundSample.Missing(CartGroundStatus.Unreadable);
            }
        }

        private CartClearanceSample Sweep(
            WorkPoint from, WorkPoint to, float headingX, float headingZ, float halfWidth, float halfLength,
            bool ignoreStartOverlaps)
        {
            try
            {
                return _owner._probe.SweepBox(from, to, headingX, headingZ, halfWidth, halfLength, ignoreStartOverlaps);
            }
            catch (Exception)
            {
                _faulted = true;
                return CartClearanceSample.Unreadable;
            }
        }

        private static bool IsMeasured(CartClearanceSample sample)
        {
            return sample.Status == CartClearanceStatus.Clear ||
                (sample.Status == CartClearanceStatus.Blocked && CartRouteGeometry.IsFiniteValue(sample.BlockedAtMetres));
        }

        private static float FreeRoom(CartClearanceSample sample, float reach)
        {
            float travelled = sample.Status == CartClearanceStatus.Blocked
                ? Math.Max(0f, Math.Min(reach, sample.BlockedAtMetres))
                : reach;
            return travelled + CartRouteGeometry.LateralSlabHalfMetres;
        }

        private static float Distance3D(WorkPoint a, WorkPoint b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float dz = b.Z - a.Z;
            return (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private CartRouteAssessment Refuse(CartRouteFinding finding, WorkPoint? at, float along, string obstacle = "")
        {
            return CartRouteAssessment.Refused(finding, at, along, obstacle, Costs());
        }

        private CartRouteCosts Costs()
        {
            return new CartRouteCosts(_groundSamples, _allowance.Used - _probesAtStart, _doorScans, _repairs);
        }
    }
}
