using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Animation;

/// <summary>Editable gait values (owner correction 2026-10-26). Heights are metres relative to the body root (the
/// capsule base the motor moves); swing values are fractions of one gait cycle. A cycle is one move of every limb:
/// the right hand and right foot, then the left hand and left foot, half a cycle apart. Each limb travels
/// <see cref="StrideRungs"/> rungs per move and the body completes the same distance per cycle, so limb cadence is
/// body speed ÷ (StrideRungs × pitch). See AnimationSources/README.md for how to adjust these.</summary>
internal sealed class GaitShape
{
    /// <summary>Rungs each limb travels per move. Even, so the two sides alternate evenly on real rungs.</summary>
    internal int StrideRungs = 4;

    /// <summary>Fraction of a cycle a hand is off its rung. Above 0.5 the two sides overlap, as in the reference.</summary>
    internal float HandSwing = 0.55f;

    /// <summary>Fraction of a cycle a foot is off its rung, and how far behind its hand it leaves.</summary>
    internal float FootSwing = 0.6f;
    internal float FootLag = 0.06f;

    /// <summary>Rung centre relative to the body root when a foot leaves it (the lowest a planted foot gets).</summary>
    internal float FootLowRel = 0.06f;

    /// <summary>The middle of a hand's planted range relative to the shoulder height the rig reports.</summary>
    internal float HandMidFromShoulder = 0.02f;

    /// <summary>When the climber stops, each limb settles on the nearest rung within this band: hands relative to the
    /// shoulder, feet relative to FootLowRel.</summary>
    internal float HandSettleBelow = 0.5f;
    internal float HandSettleAbove = 0.4f;
    internal float FootSettleBelow = 0.1f;
    internal float FootSettleAbove = 0.7f;

    /// <summary>Seconds for a swinging limb to settle onto the nearer rung when the climber stops.</summary>
    internal float SettleSeconds = 0.14f;

    /// <summary>Seconds for a limb that settled during a stop to rejoin the gait when travel resumes.</summary>
    internal float RejoinSeconds = 0.08f;

    internal bool Valid =>
        StrideRungs >= 2 && StrideRungs <= 8 && StrideRungs % 2 == 0 &&
        Between(HandSwing, 0.2f, 0.8f) && Between(FootSwing, 0.2f, 0.8f) && Between(FootLag, 0f, 0.4f) &&
        FootLag + FootSwing <= 1f && Between(FootLowRel, -0.5f, 0.5f) && Between(HandMidFromShoulder, -0.6f, 0.6f) &&
        Between(SettleSeconds, 0.02f, 1f) && Between(RejoinSeconds, 0.01f, 1f);

    internal GaitShape Copy() => (GaitShape)MemberwiseClone();

    private static bool Between(float value, float min, float max) => !float.IsNaN(value) && value >= min && value <= max;
}

/// <summary>Original same-side ladder gait (owner reference 2026-10-26: the right hand reaches while the right knee
/// recovers, then the left hand with the left knee, with overlapping timing). Contacts are a pure function of the
/// body's height on the run, so the same height always gives the same pose: climbing down retraces climbing up,
/// nothing drifts and nothing depends on frame rate. Every planted contact is a real rung. A stop settles a limb
/// that is between rungs onto the nearer rung; when travel resumes it rejoins the gait. This controls four contact
/// targets only, never the player.</summary>
internal sealed class SameSideGait
{
    internal const int LeftHand = 0, RightHand = 1, LeftFoot = 2, RightFoot = 3;

    private readonly float[] _shown = new float[4];
    private readonly float[] _clearance = new float[4];
    private readonly float[] _settled = new float[4];
    private readonly bool[] _synced = new bool[4];
    private GaitShape _shape = new GaitShape();
    private float _base, _pitch, _origin, _cycle, _shoulderRel;
    private int _first, _last, _handGap;
    private bool _holding;

    internal bool Ready { get; private set; }

    /// <summary>Rungs between a side's foot rung and its hand rung.</summary>
    internal int HandGap => _handGap;

    /// <summary>+1 while the right side swings (weight on the left), −1 for the left side, 0 when all are planted.</summary>
    internal float Sway { get; private set; }

    internal GaitShape Shape => _shape;

    /// <summary>Fix the gait to this run. <paramref name="rungBaseY"/> is rung 0's centre; rungs exist from
    /// <paramref name="firstRung"/> to <paramref name="lastRung"/> (indices relative to rung 0). The phase origin is
    /// fixed here in world height, so a later extension of the run never shifts it.</summary>
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
        _cycle = _shape.StrideRungs * pitch;
        _origin = rungBaseY - _shape.FootLowRel - _cycle * _shape.FootLag;
        float handLowRel = _shape.FootLowRel + _cycle * _shape.FootLag;
        float handRange = (1f - _shape.HandSwing) * _cycle;
        float wanted = shoulderRel + _shape.HandMidFromShoulder - handRange * 0.5f - handLowRel;
        _handGap = Math.Max(1, Math.Min(12, (int)Math.Round(wanted / pitch)));
        _shoulderRel = shoulderRel;
        _holding = false;
        Ready = true;
        Pose(feetY, true);
        for (int limb = 0; limb < 4; limb++) { _synced[limb] = true; }
        return true;
    }

    /// <summary>A run that was extended or shortened without moving the climber: only the usable rung span changes.
    /// Rung positions keep their original index base, so the phase never shifts.</summary>
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
            // In game 2026-10-26: settling a mid-swing limb onto the rung it left or the rung it was heading for put
            // it up to half a swing (0.37 m with a four-rung stride) away from the body, out of reach. It takes the
            // real rung nearest to where it is instead: never more than half a rung away.
            // In game 2026-10-26 a hand stopping after a descent settled one rung past its reach; the rung chosen is the
            // nearest one inside the limb's comfortable band relative to the body.
            for (int limb = 0; limb < 4; limb++)
            {
                bool hand = limb == LeftHand || limb == RightHand;
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
            sway += (limb == RightHand || limb == RightFoot ? 0.5f : -0.5f) * swing;
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
        }

        Sway = Clamp(sway, -1f, 1f);
        return true;
    }

    /// <summary>All four contacts planted on real rungs for a body at <paramref name="feetY"/>: the pose a mount or
    /// catch fits to before the gait starts.</summary>
    internal void Pose(float feetY, bool planted)
    {
        for (int limb = 0; limb < 4; limb++)
        {
            _shown[limb] = Target(limb, feetY, planted, out float clearance, out _);
            _clearance[limb] = planted ? 0f : clearance;
        }

        Sway = 0f;
    }

    internal OriginalLimbContact Contact(int limb)
    {
        if (!Ready || limb < 0 || limb > 3) { return default; }
        return new OriginalLimbContact(_shown[limb], _clearance[limb], _clearance[limb] <= 0.0001f);
    }

    /// <summary>Contact height of one limb for a body at <paramref name="feetY"/>. Hands and feet of the right side
    /// move in the first half of the cycle, the left side's half a cycle later; a hand leads its foot by FootLag.</summary>
    internal float Target(int limb, float feetY, bool planted, out float clearance, out float swing)
    {
        bool hand = limb == LeftHand || limb == RightHand;
        bool right = limb == RightHand || limb == RightFoot;
        float u = (feetY - _origin) / _cycle - (right ? 0f : 0.5f);
        float cycle = (float)Math.Floor(u);
        float t = u - cycle;
        float start = hand ? 0f : _shape.FootLag;
        float length = hand ? _shape.HandSwing : _shape.FootSwing;
        float w = Clamp((t - start) / length, 0f, 1f);
        if (planted) { w = w < 0.5f ? 0f : 1f; }
        int sideBase = (right ? 0 : _shape.StrideRungs / 2) + (hand ? _handGap : 0);
        long from = sideBase + (long)cycle * _shape.StrideRungs;
        int a = ClampRung(from), b = ClampRung(from + _shape.StrideRungs);
        swing = a == b ? 0f : Arc(w);
        clearance = swing;
        return _base + (a + (b - a) * Ease(w)) * _pitch;
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
    private static float Arc(float value) { float s = (float)Math.Sin(Math.PI * Clamp(value, 0f, 1f)); return s * s; }
}

internal readonly struct OriginalLimbContact
{
    internal OriginalLimbContact(float height, float clearance, bool planted)
    { Height = height; Clearance = clearance; Planted = planted; }
    internal float Height { get; }
    internal float Clearance { get; }
    internal bool Planted { get; }
}
