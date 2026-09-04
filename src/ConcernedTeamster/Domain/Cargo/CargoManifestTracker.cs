using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Cargo;

/// <summary>Bounded manifest refresh (CT-006): consumers ask as often as
/// they like, the underlying reader runs at most once per interval — never
/// per frame, and only while something actually asks. The reader returning
/// null (cart gone, container unreadable) clears the cache so stale cargo
/// can never outlive its cart.</summary>
public sealed class CargoManifestTracker
{
    /// <summary>Default and floor refresh spacing. One second keeps the
    /// manifest fresher than hand-loading speed while staying far from
    /// per-frame cost.</summary>
    public const double MinRefreshSpacingSeconds = 1.0;

    private readonly double _refreshSpacingSeconds;
    private CargoManifest? _current;
    private double _nextRefreshTime;

    public CargoManifestTracker(double refreshSpacingSeconds = MinRefreshSpacingSeconds)
    {
        _refreshSpacingSeconds = Math.Max(MinRefreshSpacingSeconds, refreshSpacingSeconds);
    }

    public CargoManifest? Current => _current;

    /// <summary>Returns the cached manifest, refreshing through
    /// <paramref name="readManifest"/> only when the spacing has elapsed.
    /// The reader receives the capture timestamp.</summary>
    public CargoManifest? GetOrRefresh(double nowSeconds, Func<double, CargoManifest?> readManifest)
    {
        if (nowSeconds >= _nextRefreshTime)
        {
            _nextRefreshTime = nowSeconds + _refreshSpacingSeconds;
            _current = readManifest(nowSeconds);
        }

        return _current;
    }

    /// <summary>Forgets the cache and makes the next request refresh
    /// immediately (world switch, cart switch).</summary>
    public void Reset()
    {
        _current = null;
        _nextRefreshTime = 0d;
    }
}
