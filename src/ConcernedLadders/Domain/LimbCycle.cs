using System;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>Which rung each hand and foot holds while climbing, driven by the body's actual travel.
/// A limb stays planted on its rung while the body moves; once its rung falls more than a pitch
/// behind the body it swings two rungs on, so the hands alternate and each foot moves with the
/// opposite hand. Holding still keeps every limb where it is. Game-free so the cadence is testable.</summary>
internal sealed class LimbCycle
{
    internal const int LeftHand = 0, RightHand = 1, LeftFoot = 2, RightFoot = 3;

    /// <summary>Body travel, as a fraction of a rung pitch, that completes one limb swing.</summary>
    internal const float SwingTravel = 0.8f;

    /// <summary>A swing interrupted by stopping completes at this rate per second, so a hold settles.</summary>
    internal const float SettleRate = 2.5f;

    private readonly int[] _rung = new int[4];
    private readonly float[] _from = new float[4];
    private readonly float[] _progress = new float[4];
    private readonly float[] _nominal = new float[4];
    private float _bottom;
    private float _pitch = 1f / 3f;
    private int _count;
    private float _feet;

    internal bool Ready { get; private set; }

    /// <summary>Places the limbs for a body whose feet are at <paramref name="feet"/>: hands near the grip
    /// height, feet near the soles, each pair on adjacent rungs.</summary>
    internal void Reset(float feet, float bottom, float pitch, float height, float handHeight, float footHeight)
    {
        Ready = false;
        if (!ClimbPolicy.Finite(feet) || !ClimbPolicy.Finite(bottom) || !ClimbPolicy.Finite(pitch) || pitch < 0.1f ||
            !ClimbPolicy.Finite(height) || height < pitch || !ClimbPolicy.Finite(handHeight) || !ClimbPolicy.Finite(footHeight))
        {
            return;
        }

        _bottom = bottom;
        _pitch = pitch;
        _count = Math.Max(1, (int)Math.Round(height / pitch));
        _feet = feet;
        _nominal[LeftHand] = _nominal[RightHand] = handHeight;
        _nominal[LeftFoot] = _nominal[RightFoot] = footHeight;
        for (int limb = 0; limb < 4; limb++)
        {
            // Left hand and right foot lead together, like a person on a real ladder.
            float lead = limb == LeftHand || limb == RightFoot ? 0.5f : -0.5f;
            _rung[limb] = Nearest(feet + _nominal[limb] + lead * pitch);
            _from[limb] = RungHeight(_rung[limb]);
            _progress[limb] = 1f;
        }

        Ready = true;
    }

    /// <summary>Advances the cycle by the body's observed travel this frame.</summary>
    internal void Step(float feet, float seconds)
    {
        if (!Ready || !ClimbPolicy.Finite(feet)) { return; }
        float travel = feet - _feet;
        _feet = feet;
        float advance = Math.Abs(travel) / (SwingTravel * _pitch) +
                        (ClimbPolicy.Finite(seconds) && seconds > 0f ? Math.Min(seconds, 0.1f) * SettleRate : 0f);
        for (int limb = 0; limb < 4; limb++)
        {
            if (_progress[limb] < 1f)
            {
                _progress[limb] = Math.Min(1f, _progress[limb] + advance);
                continue;
            }

            float desired = feet + _nominal[limb];
            float held = RungHeight(_rung[limb]);
            int next = _rung[limb];
            if (held < desired - _pitch) { next = _rung[limb] + 2; }
            else if (held > desired + _pitch) { next = _rung[limb] - 2; }
            next = Math.Max(0, Math.Min(_count - 1, next));
            if (next == _rung[limb] || next == _rung[Partner(limb)]) { continue; }
            _from[limb] = held;
            _rung[limb] = next;
            _progress[limb] = 0f;
        }
    }

    /// <summary>The height the limb's contact point is at now, between its last rung and its next.</summary>
    internal float Height(int limb)
    {
        float t = Smooth(_progress[limb]);
        return _from[limb] + (RungHeight(_rung[limb]) - _from[limb]) * t;
    }

    /// <summary>0 while planted, rising to 1 mid-swing: how far the limb lifts off the rungs.</summary>
    internal float Swing(int limb) => (float)Math.Sin(Math.PI * _progress[limb]);

    internal int Rung(int limb) => _rung[limb];

    internal float RungHeight(int index) => _bottom + (index + 0.5f) * _pitch;

    private int Nearest(float height) =>
        Math.Max(0, Math.Min(_count - 1, (int)Math.Round((height - _bottom) / _pitch - 0.5f)));

    private static int Partner(int limb) => limb switch
    {
        LeftHand => RightHand,
        RightHand => LeftHand,
        LeftFoot => RightFoot,
        _ => LeftFoot,
    };

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
