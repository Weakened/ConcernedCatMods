using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Animation;

/// <summary>Original diagonal contact choreography. This controls four contact targets, never the player.
/// A stroke consumes completed signed motor travel; reversal retraces the current stroke. A stopped swing
/// settles onto the real endpoint nearer its body-relative support without advancing a climb clock. No frame-number sampling.</summary>
internal sealed class OriginalContactTrack
{
    internal const int LeftHand = 0, RightHand = 1, LeftFoot = 2, RightFoot = 3;
    internal const float SettleSeconds = 0.12f;
    internal const int StrideRungs = 2;
    private readonly float[] _held = new float[4];
    private readonly float[] _from = new float[4];
    private readonly float[] _to = new float[4];
    private float _first, _last, _pitch, _progress;
    private int _pair = -1, _direction, _settle = -1;

    internal bool Ready { get; private set; }
    internal int MovingPair => _pair; //0 LH/RF,1 RH/LF; -1 means all planted.
    internal float StrokeProgress => _progress;
    internal float SupportShift => _pair < 0 ? 0f : (_pair == 1 ? 1f : -1f) * Arc(_progress);

    internal bool Bind(float feetY, float firstRungY, float pitch, int lastRung, int handGapRungs)
    {
        Ready = false;
        if (!Finite(feetY) || !Finite(firstRungY) || !Finite(pitch) || pitch < 0.1f || pitch > 1f ||
            lastRung < 1 || lastRung > 4096 || handGapRungs < 1 || handGapRungs > 10 ||
            Math.Abs(feetY - firstRungY) > pitch * (lastRung + 10)) { return false; }
        _first = firstRungY;
        _last = firstRungY + lastRung * pitch;
        _pitch = pitch;
        int lowFoot = BoundIndex((int)Math.Floor((feetY + pitch * 0.5f - _first) / pitch + 0.5f), lastRung - 1);
        int lowHand = BoundIndex(lowFoot + handGapRungs, lastRung - 1);
        _held[LeftHand] = Height(lowHand + 1);
        _held[RightHand] = Height(lowHand);
        _held[LeftFoot] = Height(lowFoot);
        _held[RightFoot] = Height(lowFoot + 1);
        _pair = -1;
        _progress = 0f;
        _settle = -1;
        Ready = true;
        return true;
    }

    /// <summary>An aligned scaffold extension preserves world contacts and the in-flight curve.
    /// Removing an occupied endpoint refuses presentation; it never invents a replacement support.</summary>
    internal bool RefreshBounds(float firstRungY, int lastRung)
    {
        if (!Ready || !Finite(firstRungY) || lastRung < 1 || lastRung > 4096) { Ready = false; return false; }
        float offset = (firstRungY - _first) / _pitch;
        if (Math.Abs(offset - Math.Round(offset)) > 0.002f) { Ready = false; return false; }
        float last = firstRungY + lastRung * _pitch;
        for (int limb = 0; limb < 4; limb++)
        {
            if (!Inside(_held[limb], firstRungY, last) ||
                (Moving(limb) && (!Inside(_from[limb], firstRungY, last) || !Inside(_to[limb], firstRungY, last)))) { Ready = false; return false; }
        }
        _first = firstRungY;
        _last = last;
        return true;
    }

    internal bool Advance(float completedTravel, float feetY, float seconds, bool hold)
    {
        if (!Ready || !Finite(completedTravel) || !Finite(feetY) || !Finite(seconds) || seconds < 0f ||
            Math.Abs(completedTravel) > 2f || (hold && Math.Abs(completedTravel) > 0.00001f)) { return false; }
        if (Math.Abs(completedTravel) < 0.000001f)
        {
            Settle(Math.Min(seconds, 0.1f), feetY);
            return true;
        }
        _settle = -1;
        float remaining = completedTravel;
        // Bounded recovery when a consumer sees several completed steps together; no per-render allocations.
        for (int iteration = 0; iteration < 24 && Math.Abs(remaining) > 0.000001f; iteration++)
        {
            int direction = remaining > 0f ? 1 : -1;
            if (_pair < 0)
            {
                // Settling can put contacts ahead of completed travel. Wait for the body to reach the
                // next support threshold instead of counting that visual settling as another stroke.
                float feetBefore = feetY - remaining;
                float gap = direction > 0 ? Math.Min(_held[LeftFoot], _held[RightFoot]) - (feetBefore + _pitch * 0.5f) :
                    feetBefore + _pitch * 1.5f - Math.Max(_held[LeftFoot], _held[RightFoot]);
                float wait = Math.Min(Math.Abs(remaining), Math.Max(0f, gap));
                remaining -= direction * wait;
                if (Math.Abs(remaining) <= 0.000001f) { break; }
                if (!Begin(direction)) { return true; }
            }
            float strokeDistance = _pitch; //two-rung step over one rung of body travel, matching the paired reference.
            float delta = remaining * _direction / strokeDistance;
            float endpoint = delta >= 0f ? 1f : 0f;
            float available = Math.Abs(endpoint - _progress) * strokeDistance;
            float consume = Math.Min(Math.Abs(remaining), available);
            _progress = Clamp(_progress + direction * _direction * consume / strokeDistance, 0f, 1f);
            remaining -= direction * consume;
            if (available <= consume + 0.000001f) { Finish(endpoint > 0.5f); }
        }
        return Math.Abs(remaining) <= 0.00001f;
    }

    internal OriginalLimbContact Contact(int limb)
    {
        if (!Ready || limb < 0 || limb > 3) { return default; }
        if (!Moving(limb) || Math.Abs(_to[limb] - _from[limb]) < 0.00001f)
        { return new OriginalLimbContact(_held[limb], 0f, true); }
        return new OriginalLimbContact(_from[limb] + (_to[limb] - _from[limb]) * Ease(_progress),
            Arc(_progress), _progress <= 0f || _progress >= 1f);
    }

    private bool Begin(int direction)
    {
        // The lower pair leads up; the higher pair leads down. Reversals preserve existing endpoints.
        float left = _held[LeftFoot];
        float right = _held[RightFoot];
        int preferred = direction > 0 ? (left <= right ? 1 : 0) : (right >= left ? 0 : 1);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            _pair = attempt == 0 ? preferred : 1 - preferred;
            bool changes = false;
            for (int limb = 0; limb < 4; limb++)
            {
                _from[limb] = _held[limb];
                _to[limb] = Moving(limb) ? Clamp(_held[limb] + direction * StrideRungs * _pitch, _first, _last) : _held[limb];
                changes |= Math.Abs(_to[limb] - _from[limb]) > 0.00001f;
            }
            if (changes) { _progress = 0f; _direction = direction; return true; }
        }
        _pair = -1;
        return false;
    }

    private void Settle(float seconds, float feetY)
    {
        if (_pair < 0) { return; }
        if (_settle < 0)
        {
            // Choose the actual body's nearer support, not merely the nearest curve parameter.
            // Repeated short stops must not return to the same old rung while the body keeps travelling.
            int foot = _pair == 0 ? RightFoot : LeftFoot;
            float comfortable = feetY + _pitch;
            _settle = Math.Abs(_from[foot] - comfortable) <= Math.Abs(_to[foot] - comfortable) ? 0 : 1;
        }
        float step = seconds / SettleSeconds;
        _progress = _settle == 1 ? Math.Min(1f, _progress + step) : Math.Max(0f, _progress - step);
        if (_progress <= 0f || _progress >= 1f) { Finish(_progress >= 1f); }
    }

    private void Finish(bool destination)
    {
        for (int limb = 0; limb < 4; limb++)
        { if (Moving(limb)) { _held[limb] = destination ? _to[limb] : _from[limb]; } }
        _pair = -1;
        _progress = 0f;
        _settle = -1;
    }

    private bool Moving(int limb) => _pair == 0 ? limb == LeftHand || limb == RightFoot :
        _pair == 1 && (limb == RightHand || limb == LeftFoot);
    private float Height(int rung) => _first + rung * _pitch;
    private static int BoundIndex(int value, int maximum) => Math.Max(0, Math.Min(maximum, value));
    private static bool Inside(float height, float first, float last) => height >= first - 0.001f && height <= last + 0.001f;
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
