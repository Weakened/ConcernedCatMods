using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Animation;

/// <summary>One authored climbing loop (owner spec 2026-10-27): ascent and descent are separate instances. A cycle is
/// one move of every limb; the body travels <see cref="StrideRungs"/> rungs per cycle. All curves are sampled tables
/// exported from AnimationSources/LadderGait.blend (see the README there): the limb tables are indexed by that limb's own
/// swing progress 0..1; the body tables by cycle phase 0..1 (phase 0 = the body's feet level with rung 0's centre).
/// Index order of limbs is LH, RH, LF, RF. Offsets are metres in ladder space: lat = toward the limb's own side,
/// out = away from the rungs toward the climber, up.</summary>
internal sealed class ClimbLoop
{
    internal string Name = "climb_up";
    internal int StrideRungs = 3;

    /// <summary>Release phase and swing length per limb as fractions of a cycle (windows may overlap).</summary>
    internal float[] Start = { 0.50f, 0.00f, 0.20f, 0.70f };
    internal float[] Length = { 0.40f, 0.40f, 0.46f, 0.46f };

    /// <summary>Swing progress → fraction of the rung-to-rung travel (the ease of a reach), shared by all limbs.</summary>
    internal float[] Swing = Smooth(33);

    /// <summary>Per limb, sampled over swing progress: [sample][0 lat, 1 out, 2 up].</summary>
    internal float[][][] Arc = { Arc3(33, 0f, 0.07f, 0.02f), Arc3(33, 0f, 0.07f, 0.02f), Arc3(33, 0.01f, 0.14f, 0.05f), Arc3(33, 0.01f, 0.14f, 0.05f) };

    /// <summary>Body offset over the cycle, [sample][0 up, 1 toward the ladder], relative to steady travel. Zero mean
    /// upward; the body rises slowly while a foot clears and seats, then faster as the supporting leg extends.</summary>
    internal float[][] Body = Dwell(61, 0.6f, new[] { 0.20f, 0.70f }, 0.46f, 0.3f);

    /// <summary>Extra torso pitch toward the ladder over the cycle, degrees.</summary>
    internal float[] Lean = Flat(61, 0f);

    internal bool Valid
    {
        get
        {
            if (StrideRungs < 1 || StrideRungs > 6 || !Four(Start) || !Four(Length) || !Table(Swing) || Arc == null || Arc.Length != 4 ||
                Body == null || Body.Length < 2 || !Table(Lean)) { return false; }
            for (int limb = 0; limb < 4; limb++)
            {
                if (!Between(Start[limb], 0f, 1f) || !Between(Length[limb], 0.05f, 0.7f) || !Table3(Arc[limb])) { return false; }
            }

            foreach (float[] row in Body) { if (row == null || row.Length != 2 || !Small(row[0]) || !Small(row[1])) { return false; } }
            return true;
        }
    }

    internal ClimbLoop Copy()
    {
        var copy = (ClimbLoop)MemberwiseClone();
        copy.Start = (float[])Start.Clone();
        copy.Length = (float[])Length.Clone();
        copy.Swing = (float[])Swing.Clone();
        copy.Arc = new float[4][][];
        for (int limb = 0; limb < 4; limb++) { copy.Arc[limb] = Clone2(Arc[limb]); }
        copy.Body = Clone2(Body);
        copy.Lean = (float[])Lean.Clone();
        return copy;
    }

    internal static float Sample(float[] table, float x)
    {
        float position = LimbGait.Clamp(x, 0f, 1f) * (table.Length - 1);
        int index = Math.Min(table.Length - 2, (int)Math.Floor(position));
        return table[index] + (table[index + 1] - table[index]) * (position - index);
    }

    internal static float Sample(float[][] table, float x, int column)
    {
        float position = LimbGait.Clamp(x, 0f, 1f) * (table.Length - 1);
        int index = Math.Min(table.Length - 2, (int)Math.Floor(position));
        return table[index][column] + (table[index + 1][column] - table[index][column]) * (position - index);
    }

    /// <summary>A smooth slow-then-rise body profile: within each foot window the body speed is
    /// 1 − amount·cos(2π(u − slowAt)) (slowest at <paramref name="slowAt"/> of the window), 1 elsewhere; the table is
    /// its zero-mean running integral over one cycle of <paramref name="cycleMetres"/>.</summary>
    internal static float[][] Dwell(int samples, float amount, float[] footStarts, float footLength, float slowAt, float cycleMetres = 1f)
    {
        var up = new float[samples];
        float step = 1f / (samples - 1), offset = 0f, sum = 0f;
        for (int i = 1; i < samples; i++)
        {
            float t = (i - 0.5f) * step;
            float speed = 0f;
            foreach (float start in footStarts)
            {
                float d = t - start; d -= (float)Math.Floor(d);
                if (d < footLength) { speed -= (float)Math.Cos(2 * Math.PI * (d / footLength - slowAt)); }
            }

            offset += speed * amount * cycleMetres * step;
            up[i] = offset;
            sum += offset;
        }

        float mean = sum / (samples - 1);
        var table = new float[samples][];
        for (int i = 0; i < samples; i++) { table[i] = new[] { up[i] - mean, 0f }; }
        table[0][0] = table[samples - 1][0];
        return table;
    }

    internal static float[] Smooth(int count)
    {
        var table = new float[count];
        for (int i = 0; i < count; i++) { float t = (float)i / (count - 1); table[i] = t * t * (3f - 2f * t); }
        return table;
    }

    internal static float[][] Arc3(int count, float lat, float outward, float up)
    {
        var table = new float[count][];
        for (int i = 0; i < count; i++)
        {
            float s = (float)Math.Sin(Math.PI * i / (count - 1));
            table[i] = new[] { lat * s * s, outward * s * s, up * s * s };
        }

        return table;
    }

    internal static float[] Flat(int count, float value)
    {
        var table = new float[count];
        for (int i = 0; i < count; i++) { table[i] = value; }
        return table;
    }

    private static float[][] Clone2(float[][] table)
    {
        var copy = new float[table.Length][];
        for (int i = 0; i < table.Length; i++) { copy[i] = (float[])table[i].Clone(); }
        return copy;
    }

    private static bool Four(float[] values) => values != null && values.Length == 4;
    private static bool Small(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 2f;

    private static bool Table(float[] table)
    {
        if (table == null || table.Length < 2 || table.Length > 1024) { return false; }
        foreach (float value in table) { if (!Small(value) && !(Math.Abs(value) <= 90f)) { return false; } }
        return true;
    }

    private static bool Table3(float[][] table)
    {
        if (table == null || table.Length < 2 || table.Length > 1024) { return false; }
        foreach (float[] row in table) { if (row == null || row.Length != 3 || !Small(row[0]) || !Small(row[1]) || !Small(row[2])) { return false; } }
        return true;
    }

    private static bool Between(float value, float min, float max) => !float.IsNaN(value) && value >= min && value <= max;
}

/// <summary>Values shared by both loops: how a stance sits on the rungs, settling and rejoining.</summary>
internal sealed class GaitShape
{
    internal ClimbLoop Up = new ClimbLoop();
    internal ClimbLoop Down = DefaultDown();

    /// <summary>Rung centre relative to the body root when a foot leaves it climbing up (its lowest planted height).</summary>
    internal float FootLowRel = 0.06f;

    /// <summary>The middle of a hand's planted range relative to the shoulder height the rig reports.</summary>
    internal float HandMidFromShoulder = 0.0f;

    /// <summary>When the climber stops, each limb settles on the nearest rung within this band: hands relative to the
    /// shoulder, feet relative to FootLowRel.</summary>
    internal float HandSettleBelow = 0.38f;
    internal float HandSettleAbove = 0.38f;
    internal float FootSettleBelow = 0.1f;
    internal float FootSettleAbove = 0.75f;

    /// <summary>Seconds for a swinging limb to settle when the climber stops, and to rejoin the gait on resuming or
    /// reversing.</summary>
    internal float SettleSeconds = 0.18f;
    internal float RejoinSeconds = 0.12f;

    /// <summary>Seconds over which contacts glide to the other loop after a reversal (after the best phase match).</summary>
    internal float ReverseSeconds = 0.3f;

    internal bool Valid =>
        Up != null && Down != null && Up.Valid && Down.Valid && Up.StrideRungs == Down.StrideRungs &&
        Between(FootLowRel, -0.5f, 0.5f) && Between(HandMidFromShoulder, -0.6f, 0.6f) &&
        Between(SettleSeconds, 0.02f, 1f) && Between(RejoinSeconds, 0.01f, 1f) && Between(ReverseSeconds, 0.01f, 2f);

    internal GaitShape Copy()
    {
        var copy = (GaitShape)MemberwiseClone();
        copy.Up = Up.Copy();
        copy.Down = Down.Copy();
        return copy;
    }

    /// <summary>Descent (owner spec: a distinct controlled lowering, not ascent retraced): the lower foot reaches down
    /// first while the supporting leg bends and the body lowers, then the opposite hand comes down; feet travel close
    /// to the rungs, pointing down, without the knee-led lift of an ascent.</summary>
    internal static ClimbLoop DefaultDown() => new ClimbLoop
    {
        Name = "climb_down",
        Start = new[] { 0.10f, 0.60f, 0.62f, 0.12f },
        Length = new[] { 0.40f, 0.40f, 0.44f, 0.44f },
        Arc = new[] { ClimbLoop.Arc3(33, 0f, 0.06f, 0.01f), ClimbLoop.Arc3(33, 0f, 0.06f, 0.01f), ClimbLoop.Arc3(33, 0f, 0.09f, 0f), ClimbLoop.Arc3(33, 0f, 0.09f, 0f) },
        Body = ClimbLoop.Dwell(61, 0.5f, new[] { 0.62f, 0.12f }, 0.44f, 0.85f),
    };

    private static bool Between(float value, float min, float max) => !float.IsNaN(value) && value >= min && value <= max;
}

/// <summary>Original ladder gait with independent limb timing (owner references 2026-10-26/27). Each limb has its own
/// release, travel and landing window; planted contacts are real rungs and stay still while other limbs move. Contacts
/// are a pure function of the body's height on the run and the travel direction: the up loop while climbing, the down
/// loop while descending; a reversal blends between them over RejoinSeconds. Nothing drifts, nothing depends on frame
/// rate, a body that stops moving stops the limbs, and a limb's move always completes as the body passes its window.
/// A stop settles a limb between rungs onto the nearest reachable rung. The authored body curve gives a bounded,
/// zero-mean visual offset (slower rise while a foot recovers, faster as the leg extends). This controls contact
/// targets and a visual torso offset only, never the player.</summary>
internal sealed class LimbGait
{
    internal const int LeftHand = 0, RightHand = 1, LeftFoot = 2, RightFoot = 3;

    private readonly float[] _shown = new float[4];
    private readonly float[] _swing = new float[4];
    private readonly float[] _settled = new float[4];
    private readonly bool[] _synced = new bool[4];
    private readonly bool[] _planted = new bool[4];
    private readonly float[] _shift = new float[2];
    private float _reverseUntil, _clock;
    private readonly int[][] _baseRung = { new int[4], new int[4] };
    private GaitShape _shape = new GaitShape();
    private float _base, _pitch, _cycle, _shoulderRel, _lastFeet;
    private int _first, _last;
    private bool _holding, _descending;

    internal bool Ready { get; private set; }
    internal GaitShape Shape => _shape;

    /// <summary>The loop currently driving contacts.</summary>
    internal ClimbLoop Loop => _descending ? _shape.Down : _shape.Up;
    internal bool Descending => _descending;

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
        _cycle = _shape.Up.StrideRungs * pitch;
        BaseRungs(_shape.Up, _baseRung[0]);
        BaseRungs(_shape.Down, _baseRung[1]);
        _holding = false;
        _descending = false;
        _lastFeet = feetY;
        _shift[0] = _shift[1] = 0f;
        _clock = 0f;
        _reverseUntil = -1f;
        Ready = true;
        Pose(feetY, true);
        for (int limb = 0; limb < 4; limb++) { _synced[limb] = true; }
        return true;
    }

    private void BaseRungs(ClimbLoop loop, int[] rungs)
    {
        for (int limb = 0; limb < 4; limb++)
        {
            // The rung a limb holds just before its window opens, relative to the body there: climbing up, feet leave
            // at FootLowRel and hands' planted range centres on the shoulder; climbing down the same stance bands apply
            // to where a limb lands at the end of its window.
            bool hand = limb < 2;
            float plantedRange = (1f - loop.Length[limb]) * _cycle;
            float lowRel = hand ? _shoulderRel + _shape.HandMidFromShoulder - plantedRange * 0.5f : _shape.FootLowRel;
            float phase = ReferenceEquals(loop, _shape.Down) ? loop.Start[limb] + loop.Length[limb] : loop.Start[limb];
            rungs[limb] = (int)Math.Round((_cycle * phase + lowRel) / _pitch) - (ReferenceEquals(loop, _shape.Down) ? loop.StrideRungs : 0);
        }
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
        _clock += seconds;
        float moved = feetY - _lastFeet;
        _lastFeet = feetY;
        if (!hold && Math.Abs(moved) > 0.0005f)
        {
            bool down = moved < 0f;
            if (down != _descending)
            {
                // Reversal: the new loop is shifted by whole rungs to the phase whose contacts best match the limbs'
                // current ones, then any remaining difference glides over ReverseSeconds (no reset, no snap).
                _descending = down;
                MatchPhase(feetY);
                _reverseUntil = _clock + _shape.ReverseSeconds;
                for (int limb = 0; limb < 4; limb++) { _synced[limb] = false; }
            }
        }

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
            float swing = 0f;
            float target = hold ? _settled[limb] : Target(limb, feetY, false, out swing);
            sway += (limb == RightHand || limb == RightFoot ? 0.5f : -0.5f) * (float)Math.Sin(Math.PI * swing);
            float tau = (hold ? _shape.SettleSeconds : _clock < _reverseUntil ? _shape.ReverseSeconds : _shape.RejoinSeconds) / 3f;
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

            _swing[limb] = _synced[limb] ? swing : 0f;
            // Planted means resting on a real rung: in step with the gait outside its window, or settled after a stop.
            _planted[limb] = (_synced[limb] && swing <= 0f) || (hold && Math.Abs(_shown[limb] - target) < 0.002f);
        }

        Sway = Clamp(sway, -1f, 1f);
        return true;
    }

    /// <summary>All four contacts planted on real rungs for a body at <paramref name="feetY"/>.</summary>
    internal void Pose(float feetY, bool planted)
    {
        for (int limb = 0; limb < 4; limb++)
        {
            _shown[limb] = Target(limb, feetY, planted, out float swing);
            _swing[limb] = planted ? 0f : swing;
            _planted[limb] = planted || swing <= 0f;
        }

        Sway = 0f;
    }

    /// <summary>Contact of one limb: height, and swing progress (0..1, 0 when planted).</summary>
    internal OriginalLimbContact Contact(int limb)
    {
        if (!Ready || limb < 0 || limb > 3) { return default; }
        float s = _swing[limb];
        return new OriginalLimbContact(_shown[limb], s > 0f ? (float)Math.Sin(Math.PI * s) : 0f, _planted[limb], s);
    }

    /// <summary>The authored swing arc of a limb at swing progress <paramref name="swing"/>, ladder space (lat toward the
    /// limb's own side, out toward the climber, up).</summary>
    internal void Arc(int limb, float swing, out float lat, out float outward, out float upward)
    {
        float[][] table = Loop.Arc[limb];
        lat = ClimbLoop.Sample(table, swing, 0);
        outward = ClimbLoop.Sample(table, swing, 1);
        upward = ClimbLoop.Sample(table, swing, 2);
    }

    /// <summary>The cycle phase 0..1 of a body at this height.</summary>
    internal float Phase(float feetY)
    {
        float u = (feetY + _shift[_descending ? 1 : 0] - _base) / _cycle;
        return u - (float)Math.Floor(u);
    }

    /// <summary>The authored body offset (up, toward the ladder) and extra lean at this height (bounded, zero mean up).</summary>
    internal void Body(float feetY, out float up, out float toward, out float lean)
    {
        up = toward = lean = 0f;
        if (!Ready || !Finite(feetY)) { return; }
        float t = Phase(feetY);
        ClimbLoop loop = Loop;
        up = ClimbLoop.Sample(loop.Body, t, 0);
        toward = ClimbLoop.Sample(loop.Body, t, 1);
        lean = ClimbLoop.Sample(loop.Lean, t);
    }

    /// <summary>Contact height of one limb for a body at <paramref name="feetY"/> in the current loop.</summary>
    internal float Target(int limb, float feetY, bool planted, out float swing)
    {
        ClimbLoop loop = Loop;
        int[] baseRung = _baseRung[_descending ? 1 : 0];
        float u = (feetY + _shift[_descending ? 1 : 0] - _base) / _cycle - loop.Start[limb];
        float cycle = (float)Math.Floor(u);
        float t = u - cycle;
        float w = Clamp(t / loop.Length[limb], 0f, 1f);
        if (planted) { w = w < 0.5f ? 0f : 1f; }
        long from = baseRung[limb] + (long)cycle * loop.StrideRungs;
        int a = ClampRung(from), b = ClampRung(from + loop.StrideRungs);
        bool moving = a != b && w > 0f && w < 1f;
        swing = moving ? w : 0f;
        return _base + (a + (b - a) * ClimbLoop.Sample(loop.Swing, w)) * _pitch;
    }

    private void MatchPhase(float feetY)
    {
        int index = _descending ? 1 : 0;
        float best = float.MaxValue, chosen = 0f;
        int stride = Loop.StrideRungs;
        for (int k = -stride; k < stride; k++)
        {
            _shift[index] = k * _pitch;
            float cost = 0f;
            for (int limb = 0; limb < 4; limb++) { cost += Math.Abs(Target(limb, feetY, false, out _) - _shown[limb]); }
            if (cost < best - 1e-4f) { best = cost; chosen = _shift[index]; }
        }

        _shift[index] = chosen;
    }

    /// <summary>Per-limb timing figures for the README and tests: recovery seconds and mean/peak recovery speed at
    /// <paramref name="speed"/> m/s body travel.</summary>
    internal static void Recovery(ClimbLoop loop, int limb, float pitch, float speed, out float seconds, out float mean, out float peak)
    {
        float cycleSeconds = loop.StrideRungs * pitch / speed;
        seconds = loop.Length[limb] * cycleSeconds;
        float distance = loop.StrideRungs * pitch;
        mean = distance / seconds;
        peak = 0f;
        for (int i = 0; i < loop.Swing.Length - 1; i++)
        {
            float slope = (loop.Swing[i + 1] - loop.Swing[i]) * (loop.Swing.Length - 1);
            peak = Math.Max(peak, slope * mean);
        }
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
