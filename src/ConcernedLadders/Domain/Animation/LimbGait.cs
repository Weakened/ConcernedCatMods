using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Animation;

/// <summary>Editable gait values (owner references 2026-10-26). A cycle is one move of every limb; the body completes
/// <see cref="StrideRungs"/> rungs per cycle, so cycle time = StrideRungs × pitch ÷ climb speed. Each limb has its own
/// release (<see cref="Start"/>) and swing length (<see cref="Length"/>) as fractions of the cycle; the windows may
/// overlap. Index order is LH, RH, LF, RF. Heights are metres relative to the body root. The shaped curves
/// (<see cref="SwingCurve"/>, <see cref="ArcOut"/>, <see cref="ArcUp"/>, <see cref="Dwell"/>) are sampled tables,
/// generated from the Blender source (AnimationSources/README.md) when it is exported.</summary>
internal sealed class GaitShape
{
    /// <summary>Rungs each limb travels per move, and the body per cycle.</summary>
    internal int StrideRungs = 3;

    /// <summary>Release phase and swing length per limb (LH, RH, LF, RF). Default order follows the mannequin loop
    /// (ScreenRecording 17-00-36): a hand reaches, the opposite foot recovers knee first, then the other hand, then the
    /// other foot; overlapping so three contacts hold most of the time.</summary>
    internal float[] Start = { 0.50f, 0.00f, 0.20f, 0.70f };
    internal float[] Length = { 0.30f, 0.30f, 0.28f, 0.28f };

    /// <summary>Rung centre relative to the body root when a foot leaves it (the lowest a planted foot gets).</summary>
    internal float FootLowRel = 0.06f;

    /// <summary>The middle of a hand's planted range relative to the shoulder height the rig reports.</summary>
    internal float HandMidFromShoulder = 0.0f;

    /// <summary>How much slower the torso rises while a foot recovers (0 = no dwell, 0.6 = 40 % of the climb speed),
    /// catching up while the leg extends (IMG_1946 4.4–5.7 s, IMG_1947 6.8–8.0 s). The visual offset this makes is
    /// bounded and averages to zero over a cycle; the gameplay body is not moved.</summary>
    internal float DwellSlow = 0.55f;

    /// <summary>Progress of a swing over its window (0..1 in, 0..1 out), sampled evenly. A limb leaves slowly, travels
    /// and settles onto the rung.</summary>
    internal float[] SwingCurve = Smoothstep(33);

    /// <summary>Hand/foot clearance during a swing, as a function of swing progress: outward from the rungs and upward,
    /// in metres, per limb kind (index 0 hands, 1 feet).</summary>
    internal float[][] ArcOut = { Bell(33, 0.09f), Bell(33, 0.16f) };
    internal float[][] ArcUp = { Bell(33, 0.02f), Bell(33, 0.05f) };

    /// <summary>When the climber stops, each limb settles on the nearest rung within this band: hands relative to the
    /// shoulder, feet relative to FootLowRel.</summary>
    internal float HandSettleBelow = 0.38f;
    internal float HandSettleAbove = 0.38f;
    internal float FootSettleBelow = 0.1f;
    internal float FootSettleAbove = 0.75f;

    /// <summary>Seconds for a swinging limb to settle when the climber stops, and to rejoin the gait on resuming.</summary>
    internal float SettleSeconds = 0.18f;
    internal float RejoinSeconds = 0.1f;

    internal bool Valid
    {
        get
        {
            if (StrideRungs < 2 || StrideRungs > 6 || Start == null || Length == null || Start.Length != 4 || Length.Length != 4) { return false; }
            for (int limb = 0; limb < 4; limb++)
            {
                if (!Between(Start[limb], 0f, 1f) || !Between(Length[limb], 0.1f, 0.6f)) { return false; }
            }

            return Between(FootLowRel, -0.5f, 0.5f) && Between(HandMidFromShoulder, -0.6f, 0.6f) && Between(DwellSlow, 0f, 0.9f) &&
                   Table(SwingCurve) && ArcOut != null && ArcUp != null && ArcOut.Length == 2 && ArcUp.Length == 2 &&
                   Table(ArcOut[0]) && Table(ArcOut[1]) && Table(ArcUp[0]) && Table(ArcUp[1]) &&
                   Between(SettleSeconds, 0.02f, 1f) && Between(RejoinSeconds, 0.01f, 1f);
        }
    }

    internal GaitShape Copy()
    {
        var copy = (GaitShape)MemberwiseClone();
        copy.Start = (float[])Start.Clone();
        copy.Length = (float[])Length.Clone();
        copy.SwingCurve = (float[])SwingCurve.Clone();
        copy.ArcOut = new[] { (float[])ArcOut[0].Clone(), (float[])ArcOut[1].Clone() };
        copy.ArcUp = new[] { (float[])ArcUp[0].Clone(), (float[])ArcUp[1].Clone() };
        return copy;
    }

    internal static float Sample(float[] table, float x)
    {
        float position = LimbGait.Clamp(x, 0f, 1f) * (table.Length - 1);
        int index = (int)Math.Floor(position);
        if (index >= table.Length - 1) { return table[table.Length - 1]; }
        float f = position - index;
        return table[index] + (table[index + 1] - table[index]) * f;
    }

    private static float[] Smoothstep(int count)
    {
        var table = new float[count];
        for (int i = 0; i < count; i++) { table[i] = LimbGait.Ease((float)i / (count - 1)); }
        return table;
    }

    private static float[] Bell(int count, float height)
    {
        var table = new float[count];
        for (int i = 0; i < count; i++) { float s = (float)Math.Sin(Math.PI * i / (count - 1)); table[i] = height * s * s; }
        return table;
    }

    private static bool Table(float[] table)
    {
        if (table == null || table.Length < 2 || table.Length > 1024) { return false; }
        foreach (float value in table) { if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 2f) { return false; } }
        return true;
    }

    private static bool Between(float value, float min, float max) => !float.IsNaN(value) && value >= min && value <= max;
}

/// <summary>Original ladder gait with independent limb timing (owner references 2026-10-26). Each limb has its own
/// release, travel and landing window; planted contacts are real rungs and stay still while other limbs move. Contacts
/// are a pure function of the body's height on the run, so the same height gives the same pose: climbing down retraces
/// climbing up, nothing drifts, nothing depends on frame rate, and a limb's recovery always completes as the body
/// rises through its window. A stop settles a limb between rungs onto the nearest reachable rung; travel rejoins the
/// gait. A bounded torso dwell (<see cref="BodyOffset"/>) slows the visible rise while a foot recovers and catches up
/// while the leg extends. This controls contact targets and a visual torso offset only, never the player.</summary>
internal sealed class LimbGait
{
    internal const int LeftHand = 0, RightHand = 1, LeftFoot = 2, RightFoot = 3;
    private const int DwellSamples = 120;

    private readonly float[] _shown = new float[4];
    private readonly float[] _clearance = new float[4];
    private readonly float[] _swing = new float[4];
    private readonly float[] _settled = new float[4];
    private readonly bool[] _synced = new bool[4];
    private readonly int[] _baseRung = new int[4];
    private readonly float[] _dwell = new float[DwellSamples + 1];
    private GaitShape _shape = new GaitShape();
    private float _base, _pitch, _cycle, _shoulderRel;
    private int _first, _last;
    private bool _holding;

    internal bool Ready { get; private set; }
    internal GaitShape Shape => _shape;

    /// <summary>+1 while the right side is mid-swing more than the left (weight shifts left), −1 the other way.</summary>
    internal float Sway { get; private set; }

    /// <summary>Metres per cycle of body travel.</summary>
    internal float CycleMetres => _cycle;

    /// <summary>Fix the gait to this run. <paramref name="rungBaseY"/> is rung 0's centre; rungs exist from
    /// <paramref name="firstRung"/> to <paramref name="lastRung"/>. Phase is anchored to rung 0 in world height, so an
    /// extension of the run never shifts it.</summary>
    internal bool Bind(GaitShape shape, float rungBaseY, float pitch, int firstRung, int lastRung, float shoulderRel, float feetY)
    {
        Ready = false;
        if (shape == null || !shape.Valid || !Finite(rungBaseY) || !Finite(pitch) || pitch < 0.1f || pitch > 1f ||
            firstRung < -4096 || lastRung > 4096 || lastRung - firstRung < 1 || !Finite(shoulderRel) || shoulderRel < 0.5f ||
            shoulderRel > 3f || !Finite(feetY)) { return false; }
        _shape = shape.Copy();
        _base = rungBaseY;
        _pitch = pitch;
        _first = firstRung;
        _last = lastRung;
        _shoulderRel = shoulderRel;
        _cycle = _shape.StrideRungs * pitch;
        for (int limb = 0; limb < 4; limb++)
        {
            // The rung a limb leaves at its release, relative to the body there: feet at FootLowRel, hands so their
            // planted range is centred on the shoulder.
            bool hand = limb < 2;
            float plantedRange = (1f - _shape.Length[limb]) * _cycle;
            float lowRel = hand ? shoulderRel + _shape.HandMidFromShoulder - plantedRange * 0.5f : _shape.FootLowRel;
            _baseRung[limb] = (int)Math.Round((_cycle * _shape.Start[limb] + lowRel) / pitch);
        }

        BuildDwell();
        _holding = false;
        Ready = true;
        Pose(feetY, true);
        for (int limb = 0; limb < 4; limb++) { _synced[limb] = true; }
        return true;
    }

    /// <summary>A run that was extended or shortened without moving the climber: only the usable rung span changes.</summary>
    internal bool RefreshBounds(float rungBaseY, int firstRung, int lastRung)
    {
        if (!Ready || !Finite(rungBaseY) || lastRung - firstRung < 1) { Ready = false; return false; }
        float offset = (rungBaseY - _base) / _pitch;
        int whole = (int)Math.Round(offset);
        if (Math.Abs(offset - whole) > 0.002f) { Ready = false; return false; }
        _first = firstRung + whole;
        _last = lastRung + whole;
        return true;
    }

    /// <summary>Advance to the body's current height. <paramref name="hold"/> means accepted gameplay is holding still.</summary>
    internal bool Update(float feetY, float seconds, bool hold)
    {
        if (!Ready || !Finite(feetY) || !Finite(seconds) || seconds < 0f || seconds > 0.5f) { return false; }
        if (hold && !_holding)
        {
            for (int limb = 0; limb < 4; limb++)
            {
                bool hand = limb < 2;
                float low = feetY + (hand ? _shoulderRel - _shape.HandSettleBelow : _shape.FootLowRel - _shape.FootSettleBelow);
                float high = feetY + (hand ? _shoulderRel + _shape.HandSettleAbove : _shape.FootLowRel + _shape.FootSettleAbove);
                _settled[limb] = NearestRung(_shown[limb], low, high);
            }
        }

        _holding = hold;
        float sway = 0f;
        for (int limb = 0; limb < 4; limb++)
        {
            float clearance = 0f, swing = 0f;
            float target = hold ? _settled[limb] : Target(limb, feetY, false, out clearance, out swing);
            sway += (limb == RightHand || limb == RightFoot ? 0.5f : -0.5f) * (swing > 0f && swing < 1f ? 1f : 0f) * (float)Math.Sin(Math.PI * swing);
            float tau = hold ? _shape.SettleSeconds / 3f : _shape.RejoinSeconds / 3f;
            if (_synced[limb] && !hold && Math.Abs(_shown[limb] - target) < _pitch * 0.5f)
            {
                _shown[limb] = target;
            }
            else
            {
                float k = seconds <= 0f ? 0f : 1f - (float)Math.Exp(-seconds / tau);
                _shown[limb] += (target - _shown[limb]) * k;
                _synced[limb] = !hold && Math.Abs(_shown[limb] - target) < 0.002f;
                if (_synced[limb]) { _shown[limb] = target; }
            }

            _clearance[limb] = _synced[limb] ? clearance : 0f;
            _swing[limb] = _synced[limb] ? swing : 0f;
        }

        Sway = Clamp(sway, -1f, 1f);
        return true;
    }

    /// <summary>All four contacts planted on real rungs for a body at <paramref name="feetY"/>.</summary>
    internal void Pose(float feetY, bool planted)
    {
        for (int limb = 0; limb < 4; limb++)
        {
            _shown[limb] = Target(limb, feetY, planted, out float clearance, out float swing);
            _clearance[limb] = planted ? 0f : clearance;
            _swing[limb] = planted ? 0f : swing;
        }

        Sway = 0f;
    }

    /// <summary>Contact of one limb: height, clearance (0..1 of the arc) and swing progress (0..1, 0 when planted).</summary>
    internal OriginalLimbContact Contact(int limb)
    {
        if (!Ready || limb < 0 || limb > 3) { return default; }
        return new OriginalLimbContact(_shown[limb], _clearance[limb], _clearance[limb] <= 0.0001f, _swing[limb]);
    }

    /// <summary>The arc a swinging limb follows, in metres outward from the rungs and upward, at swing progress
    /// <paramref name="swing"/>.</summary>
    internal void Arc(int limb, float swing, out float outward, out float upward)
    {
        int kind = limb < 2 ? 0 : 1;
        outward = GaitShape.Sample(_shape.ArcOut[kind], swing);
        upward = GaitShape.Sample(_shape.ArcUp[kind], swing);
    }

    /// <summary>The torso's visual vertical offset from steady travel at this body height (bounded, zero mean).</summary>
    internal float BodyOffset(float feetY)
    {
        if (!Ready || !Finite(feetY)) { return 0f; }
        float u = (feetY - _base) / _cycle;
        float t = u - (float)Math.Floor(u);
        float position = t * DwellSamples;
        int index = Math.Min(DwellSamples - 1, (int)Math.Floor(position));
        float f = position - index;
        return _dwell[index] + (_dwell[index + 1] - _dwell[index]) * f;
    }

    /// <summary>Contact height of one limb for a body at <paramref name="feetY"/>.</summary>
    internal float Target(int limb, float feetY, bool planted, out float clearance, out float swing)
    {
        float u = (feetY - _base) / _cycle - _shape.Start[limb];
        float cycle = (float)Math.Floor(u);
        float t = u - cycle;
        float w = Clamp(t / _shape.Length[limb], 0f, 1f);
        if (planted) { w = w < 0.5f ? 0f : 1f; }
        long from = _baseRung[limb] + (long)cycle * _shape.StrideRungs;
        int a = ClampRung(from), b = ClampRung(from + _shape.StrideRungs);
        bool moving = a != b && w > 0f && w < 1f;
        swing = moving ? w : 0f;
        clearance = moving ? (float)Math.Sin(Math.PI * w) : 0f;
        return _base + (a + (b - a) * GaitShape.Sample(_shape.SwingCurve, w)) * _pitch;
    }

    private void BuildDwell()
    {
        // Torso speed relative to the climb: slower while either foot recovers, faster otherwise, mean exactly 1; the
        // offset is its running integral minus its mean (bounded and periodic over the cycle).
        float[] speed = new float[DwellSamples];
        float slowTime = 0f;
        for (int i = 0; i < DwellSamples; i++)
        {
            float t = (i + 0.5f) / DwellSamples;
            speed[i] = InWindow(LeftFoot, t) || InWindow(RightFoot, t) ? 1f - _shape.DwellSlow : 1f;
            if (speed[i] < 1f) { slowTime += 1f / DwellSamples; }
        }

        float fast = slowTime >= 1f ? 1f : 1f + _shape.DwellSlow * slowTime / (1f - slowTime);
        float sum = 0f;
        _dwell[0] = 0f;
        for (int i = 0; i < DwellSamples; i++)
        {
            float s = speed[i] < 1f ? speed[i] : fast;
            _dwell[i + 1] = _dwell[i] + (s - 1f) * _cycle / DwellSamples;
            sum += _dwell[i + 1];
        }

        float mean = sum / DwellSamples;
        for (int i = 0; i <= DwellSamples; i++) { _dwell[i] -= mean; }
    }

    private bool InWindow(int limb, float t)
    {
        float d = t - _shape.Start[limb];
        d -= (float)Math.Floor(d);
        return d < _shape.Length[limb];
    }

    private float NearestRung(float height, float low, float high)
    {
        long rung = (long)Math.Round((height - _base) / _pitch);
        long lowest = (long)Math.Ceiling((low - _base) / _pitch - 1e-4);
        long highest = (long)Math.Floor((high - _base) / _pitch + 1e-4);
        if (lowest <= highest) { rung = Math.Max(lowest, Math.Min(highest, rung)); }
        return _base + ClampRung(rung) * _pitch;
    }

    private int ClampRung(long rung) => (int)Math.Max(_first, Math.Min(_last, rung));

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    internal static float Ease(float value) { float t = Clamp(value, 0f, 1f); return t * t * t * (10f + t * (-15f + 6f * t)); }
}

internal readonly struct OriginalLimbContact
{
    internal OriginalLimbContact(float height, float clearance, bool planted, float swing)
    { Height = height; Clearance = clearance; Planted = planted; Swing = swing; }
    internal float Height { get; }
    internal float Clearance { get; }
    internal bool Planted { get; }

    /// <summary>Swing progress 0..1 while moving between rungs; 0 when planted.</summary>
    internal float Swing { get; }
}
