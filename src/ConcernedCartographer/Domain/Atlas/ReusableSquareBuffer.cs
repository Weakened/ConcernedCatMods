using System;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>One full-texture scratch buffer, kept across redraws (Sentry
/// OutOfMemoryException in RoadOverlayRenderer.RedrawAll). A 2048-texel
/// overlay is a 16 MB Color32[]; allocating one or two per redraw churns
/// Mono's non-moving heap until no 16 MB contiguous block is left. The
/// renderers instead hold one of these: the same array comes back, zeroed,
/// for as long as the texture size is unchanged, and a size change drops
/// the old array before allocating so two never live at once on its
/// account. Texture2D.SetPixels32 copies the array into the texture, so
/// the caller may clear and refill it between two writes.</summary>
internal sealed class ReusableSquareBuffer<T>
    where T : struct
{
    private T[]? _buffer;
    private int _size;

    /// <summary>How many arrays this buffer has ever allocated. A steady
    /// redraw loop at one texture size must leave it at 1.</summary>
    public int AllocationCount { get; private set; }

    /// <summary>A zeroed <paramref name="size"/>² array, reused whenever
    /// the size matches the previous call.</summary>
    public T[] Acquire(int size)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Texture size must be positive.");
        }

        if (_buffer is not null && _size == size)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            return _buffer;
        }

        // Release first: the allocation below may collect, and this field
        // should no longer keep the old array alive when it does (best
        // effort: Mono scans the stack conservatively).
        _buffer = null;
        _size = 0;
        T[] fresh = new T[checked(size * size)];
        _buffer = fresh;
        _size = size;
        AllocationCount++;
        return fresh;
    }
}
