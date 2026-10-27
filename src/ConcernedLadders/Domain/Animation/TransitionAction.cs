using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Animation;

/// <summary>Which authored transition a moment of a mount, catch, exit or entry plays (owner spec 2026-10-27 section 5).</summary>
internal enum TransitionKind
{
    GroundMount,
    AirCatch,
    TopEntry,
    TopExit,
    SideExit,
    BottomStepOff,
}

/// <summary>One authored transition, sampled over its progress 0..1 (exported from AnimationSources/LadderGait.blend;
/// see the README there). Progress is the runtime route's progress for exits and the top entry (so visual contacts never
/// lead the real capsule), and elapsed time over <see cref="Seconds"/> for a ground mount or a catch (the climb is
/// already accepted; the presentation catches up).
///
/// Per limb (LH, RH, LF, RF), each table has one value per sample:
/// <list type="bullet">
/// <item><see cref="Weight"/>: how much the authored contact owns the limb (0 = the game's own pose).</item>
/// <item><see cref="Reach"/>: 0 = the limb's climbing contact on the ladder, 1 = its destination contact (the landing
/// floor, the ground, or for an entry the floor it starts from).</item>
/// <item><see cref="Arc"/>: extra ladder-space offset [lat, out, up] in metres (a knee-led foot lift over a lip, a hand
/// moving ahead onto a floor).</item>
/// </list>
/// Body: <see cref="BodyWeight"/> (1 = climbing posture, 0 = the game's own standing/walking pose), <see cref="BodyUp"/>
/// and <see cref="BodyToward"/> (metres), <see cref="Lean"/> (degrees toward the ladder / landing).</summary>
internal sealed class TransitionAction
{
    internal TransitionKind Kind;
    internal float Seconds = 0.6f;
    internal float[][] Weight = new float[4][];
    internal float[][] Reach = new float[4][];
    internal float[][][] Arc = new float[4][][];
    internal float[] BodyWeight = Array.Empty<float>();
    internal float[] BodyUp = Array.Empty<float>();
    internal float[] BodyToward = Array.Empty<float>();
    internal float[] Lean = Array.Empty<float>();

    internal int Samples => BodyWeight.Length;

    internal bool Valid
    {
        get
        {
            int n = BodyWeight.Length;
            if (n < 2 || n > 1024 || BodyUp.Length != n || BodyToward.Length != n || Lean.Length != n || !(Seconds > 0.05f && Seconds < 5f)) { return false; }
            if (Weight.Length != 4 || Reach.Length != 4 || Arc.Length != 4) { return false; }
            for (int limb = 0; limb < 4; limb++)
            {
                if (Weight[limb] == null || Reach[limb] == null || Arc[limb] == null || Weight[limb].Length != n || Reach[limb].Length != n || Arc[limb].Length != n) { return false; }
                for (int i = 0; i < n; i++)
                {
                    if (!Unit(Weight[limb][i]) || !Unit(Reach[limb][i]) || Arc[limb][i] == null || Arc[limb][i].Length != 3) { return false; }
                    foreach (float v in Arc[limb][i]) { if (!Small(v)) { return false; } }
                }
            }

            for (int i = 0; i < n; i++) { if (!Unit(BodyWeight[i]) || !Small(BodyUp[i]) || !Small(BodyToward[i]) || !(Math.Abs(Lean[i]) <= 90f)) { return false; } }
            return true;
        }
    }

    internal float WeightAt(int limb, float progress) => ClimbLoop.Sample(Weight[limb], progress);
    internal float ReachAt(int limb, float progress) => ClimbLoop.Sample(Reach[limb], progress);
    internal float ArcAt(int limb, float progress, int axis) => ClimbLoop.Sample(Arc[limb], progress, axis);
    internal float BodyWeightAt(float progress) => ClimbLoop.Sample(BodyWeight, progress);
    internal float BodyUpAt(float progress) => ClimbLoop.Sample(BodyUp, progress);
    internal float BodyTowardAt(float progress) => ClimbLoop.Sample(BodyToward, progress);
    internal float LeanAt(float progress) => ClimbLoop.Sample(Lean, progress);

    private static bool Unit(float v) => !float.IsNaN(v) && v >= -0.001f && v <= 1.001f;
    private static bool Small(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) <= 2f;

    /// <summary>Builds an action from simple keyframes (progress → value), linearly resampled; used for the defaults
    /// and by tests. The Blender exporter writes fully sampled tables instead.</summary>
    internal sealed class Builder
    {
        private readonly TransitionAction _action;
        private readonly int _n;

        internal Builder(TransitionKind kind, float seconds, int samples = 41)
        {
            _n = samples;
            _action = new TransitionAction { Kind = kind, Seconds = seconds };
            for (int limb = 0; limb < 4; limb++)
            {
                _action.Weight[limb] = Fill(1f);
                _action.Reach[limb] = Fill(0f);
                _action.Arc[limb] = new float[samples][];
                for (int i = 0; i < samples; i++) { _action.Arc[limb][i] = new float[3]; }
            }

            _action.BodyWeight = Fill(1f);
            _action.BodyUp = Fill(0f);
            _action.BodyToward = Fill(0f);
            _action.Lean = Fill(0f);
        }

        internal Builder Weight(int limb, params float[] keys) { Keys(_action.Weight[limb], keys); return this; }
        internal Builder Reach(int limb, params float[] keys) { Keys(_action.Reach[limb], keys); return this; }
        internal Builder Body(params float[] keys) { Keys(_action.BodyWeight, keys); return this; }
        internal Builder BodyUp(params float[] keys) { Keys(_action.BodyUp, keys); return this; }
        internal Builder BodyToward(params float[] keys) { Keys(_action.BodyToward, keys); return this; }
        internal Builder Lean(params float[] keys) { Keys(_action.Lean, keys); return this; }

        /// <summary>A bell-shaped arc between two progress values with peak offsets [lat, out, up].</summary>
        internal Builder Arc(int limb, float from, float to, float lat, float outward, float up)
        {
            for (int i = 0; i < _n; i++)
            {
                float t = (float)i / (_n - 1);
                if (t <= from || t >= to) { continue; }
                float s = (float)Math.Sin(Math.PI * (t - from) / (to - from));
                s *= s;
                _action.Arc[limb][i][0] += lat * s;
                _action.Arc[limb][i][1] += outward * s;
                _action.Arc[limb][i][2] += up * s;
            }

            return this;
        }

        internal TransitionAction Build() => _action;

        private float[] Fill(float value)
        {
            var table = new float[_n];
            for (int i = 0; i < _n; i++) { table[i] = value; }
            return table;
        }

        /// <summary>keys = p0, v0, p1, v1, ... (progress ascending); smoothstep between keys, held outside.</summary>
        private void Keys(float[] table, float[] keys)
        {
            if (keys.Length < 2 || keys.Length % 2 != 0) { throw new ArgumentException("keys are progress/value pairs"); }
            for (int i = 0; i < _n; i++)
            {
                float t = (float)i / (_n - 1);
                float value = keys[1];
                for (int k = 0; k + 2 < keys.Length; k += 2)
                {
                    if (t >= keys[k] && t <= keys[k + 2])
                    {
                        float u = keys[k + 2] > keys[k] ? (t - keys[k]) / (keys[k + 2] - keys[k]) : 1f;
                        u = u * u * (3f - 2f * u);
                        value = keys[k + 1] + (keys[k + 3] - keys[k + 1]) * u;
                        break;
                    }

                    if (t > keys[k + 2]) { value = keys[k + 3]; }
                }

                table[i] = value;
            }
        }
    }
}

/// <summary>The default authored transitions (references 17-00-06 mount, 16-59-05 and 17-01-40 exits, IMG_1944-1947
/// lowering). Values are fractions of progress; see TransitionAction for the channels.</summary>
internal static class DefaultTransitions
{
    private const int LH = 0, RH = 1, LF = 2, RF = 3;

    internal static TransitionAction[] All() => new[]
    {
        // Grounded mount (17-00-06 frames 90-150): a hand reaches, a low foot plants, the other hand goes higher, the
        // other foot rises; the body stays quiet at first, then settles into the climbing posture.
        new TransitionAction.Builder(TransitionKind.GroundMount, 0.9f)
            .Weight(RH, 0.08f, 0f, 0.4f, 1f).Weight(LF, 0.15f, 0f, 0.5f, 1f).Weight(LH, 0.3f, 0f, 0.65f, 1f).Weight(RF, 0.5f, 0f, 0.95f, 1f)
            .Arc(RH, 0.08f, 0.45f, 0f, 0.08f, 0.04f).Arc(LH, 0.3f, 0.7f, 0f, 0.08f, 0.04f).Arc(LF, 0.15f, 0.55f, 0f, 0.1f, 0.06f).Arc(RF, 0.5f, 1f, 0f, 0.12f, 0.08f)
            .Body(0f, 0f, 0.25f, 0.2f, 0.9f, 1f).Build(),
        // Midair catch: hands first from the airborne pose, feet find rungs after.
        new TransitionAction.Builder(TransitionKind.AirCatch, 0.35f)
            .Weight(LH, 0f, 0f, 0.35f, 1f).Weight(RH, 0.1f, 0f, 0.45f, 1f).Weight(LF, 0.3f, 0f, 0.8f, 1f).Weight(RF, 0.45f, 0f, 1f, 1f)
            .Arc(LF, 0.3f, 0.8f, 0f, 0.12f, 0.05f).Arc(RF, 0.45f, 1f, 0f, 0.12f, 0.05f)
            .Body(0f, 0.2f, 0.8f, 1f).Build(),
        new TransitionAction.Builder(TransitionKind.TopExit, 1f)
            // Owner 2026-10-27: no distinct animation - the climber comes off the ladder super fast (the runtime route
            // takes about 0.45 s) and each limb simply hands back to the game's own pose, a few hundredths apart.
            .Weight(LH, 0f, 1f, 0.4f, 0f).Weight(RH, 0.02f, 1f, 0.42f, 0f).Weight(LF, 0.04f, 1f, 0.44f, 0f).Weight(RF, 0.06f, 1f, 0.46f, 0f)
            .Body(0f, 1f, 0.5f, 0f).Build(),
        // Intermediate-floor exit: the same support transfer sideways or behind at the current height.
        new TransitionAction.Builder(TransitionKind.SideExit, 1f)
            // Owner 2026-10-27: no distinct animation - the climber comes off the ladder super fast (the runtime route
            // takes about 0.45 s) and each limb simply hands back to the game's own pose, a few hundredths apart.
            .Weight(LH, 0f, 1f, 0.4f, 0f).Weight(RH, 0.02f, 1f, 0.42f, 0f).Weight(LF, 0.04f, 1f, 0.44f, 0f).Weight(RF, 0.06f, 1f, 0.46f, 0f)
            .Body(0f, 1f, 0.5f, 0f).Build(),
        // Bottom step-off: the leading foot lowers back onto the ground, support transfers, the hands let go as the
        // body leaves the ladder (in game 2026-10-27 the route is a 0.7 m step back at ground level, so they must be
        // free by a quarter of it), the second foot follows, then normal standing.
        new TransitionAction.Builder(TransitionKind.BottomStepOff, 1f)
            .Reach(LF, 0.08f, 0f, 0.45f, 1f).Arc(LF, 0.08f, 0.45f, 0f, 0.06f, 0.03f).Weight(LF, 0.5f, 1f, 0.68f, 0f)
            .Reach(RF, 0.4f, 0f, 0.72f, 1f).Arc(RF, 0.4f, 0.72f, 0f, 0.06f, 0.03f).Weight(RF, 0.66f, 1f, 0.85f, 0f)
            .Weight(RH, 0f, 1f, 0.09f, 0f).Weight(LH, 0.04f, 1f, 0.13f, 0f)
            .Body(0f, 1f, 0.4f, 0.9f, 0.9f, 0f).Build(),
        new TransitionAction.Builder(TransitionKind.TopEntry, 1f)
            // Owner 2026-10-27: no distinct animation - the climber is put onto the ladder super fast (about 0.45 s)
            // from the game's own pose, and each limb takes its rung as the body arrives, a few hundredths apart.
            .Weight(LF, 0.5f, 0f, 0.9f, 1f).Weight(RF, 0.52f, 0f, 0.92f, 1f).Weight(LH, 0.54f, 0f, 0.94f, 1f).Weight(RH, 0.56f, 0f, 0.96f, 1f)
            .Body(0f, 0f, 0.5f, 0f, 1f, 1f).Build(),
    };
}
