using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>Sentry OutOfMemoryException in RoadOverlayRenderer.RedrawAll:
/// every full road redraw allocated two fresh full-texture pixel arrays
/// (16 MB each at 2048 texels). The renderers now share one reused buffer;
/// these tests pin the reuse, the clearing the dirt-then-paved sequence
/// depends on, and that neither renderer allocates a full-texture array of
/// its own again.</summary>
public class RoadRedrawBufferTests
{
    private struct Texel
    {
        public byte R;
        public byte A;
    }

    [Fact]
    public void ManyRedrawsAtOneSizeAllocateOnce()
    {
        var buffer = new ReusableSquareBuffer<Texel>();
        Texel[] first = buffer.Acquire(64);

        for (int redraw = 0; redraw < 100; redraw++)
        {
            // Two acquisitions per redraw: dirt, then paved.
            Assert.Same(first, buffer.Acquire(64));
            Assert.Same(first, buffer.Acquire(64));
        }

        Assert.Equal(1, buffer.AllocationCount);
        Assert.Equal(64 * 64, first.Length);
    }

    [Fact]
    public void ASecondAcquireReturnsTheArrayZeroedSoDirtInkCannotReachThePavedLayer()
    {
        var buffer = new ReusableSquareBuffer<Texel>();
        Texel[] dirt = buffer.Acquire(16);
        for (int index = 0; index < dirt.Length; index++)
        {
            dirt[index] = new Texel { R = 94, A = 255 };
        }

        Texel[] paved = buffer.Acquire(16);

        Assert.Same(dirt, paved);
        Assert.All(paved, texel => Assert.Equal(default, texel));
    }

    [Fact]
    public void ASizeChangeReallocatesOnceAtTheNewSizeAndThenReusesIt()
    {
        var buffer = new ReusableSquareBuffer<Texel>();
        Texel[] small = buffer.Acquire(8);
        small[3] = new Texel { R = 1, A = 1 };

        Texel[] large = buffer.Acquire(32);
        Assert.NotSame(small, large);
        Assert.Equal(32 * 32, large.Length);
        Assert.All(large, texel => Assert.Equal(default, texel));

        Assert.Same(large, buffer.Acquire(32));
        Assert.Equal(2, buffer.AllocationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveSizeIsRefused(int size)
    {
        var buffer = new ReusableSquareBuffer<Texel>();
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Acquire(size));
        Assert.Equal(0, buffer.AllocationCount);
    }

    [Theory]
    [InlineData("RoadOverlayRenderer.cs")]
    [InlineData("RouteOverlayRenderer.cs")]
    public void TheOverlayRenderersNeverAllocateAFullTexturePixelArrayThemselves(string file)
    {
        // The regression this guards is a renderer going back to
        // `new Color32[size * size]` per redraw; the shared buffer is the
        // only place that allocation may live.
        string source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "ConcernedCartographer", "Map", file));

        Assert.DoesNotContain("new Color32[", source, StringComparison.Ordinal);
        Assert.Contains("ReusableSquareBuffer<Color32>", source, StringComparison.Ordinal);
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        }
    }
}
