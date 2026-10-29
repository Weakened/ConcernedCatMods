using System.Runtime;

namespace ConcernedCartographer.Tests;

/// <summary>#463: the one collection whose tests measure exact allocation
/// counts. It runs on its own, after the parallel collections, so no other
/// test is allocating while a window is open.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExactAllocationCollection
{
    public const string Name = "Exact allocation windows (no parallel neighbours)";
}

/// <summary>#463: an allocation count that a busy machine cannot inflate.
///
/// <c>GC.GetAllocatedBytesForCurrentThread</c> is not a pure per-thread
/// count: under load, gen-0 collections set off by other threads' allocations
/// charged a thread that allocated nothing with about one allocation quantum
/// (7 936, 8 000 and 8 088 bytes in a local reproduction with four allocating
/// neighbours; 0 every time with none). That is the 8 128 bytes
/// <c>SteadyStateTickAllocatesNothing</c> once reported.
///
/// The window here runs inside a no-GC region, so no collection can happen in
/// it, and a real allocation is still counted to the byte. If a collection
/// happens anyway (the region's budget was exceeded, or something forced one),
/// the measurement is refused as invalid instead of being reported as
/// allocations. It is never retried and never given a tolerance.</summary>
internal static class ExactAllocations
{
    /// <summary>What the whole process may allocate while a window is open
    /// before the region ends. The window under test allocates nothing; the
    /// rest is headroom for the runner's own threads.</summary>
    public const long NoGcBudgetBytes = 16L * 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>Bytes allocated by this thread while <paramref name="window"/>
    /// ran. Throws <see cref="InvalidOperationException"/> when no exact
    /// count could be taken.</summary>
    public static long Measure(Action window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (Gate)
        {
            if (!GC.TryStartNoGCRegion(NoGcBudgetBytes))
            {
                throw new InvalidOperationException("#463: could not open a no-GC region, so no exact allocation count was taken.");
            }

            long before;
            long after;
            bool held;
            try
            {
                before = GC.GetAllocatedBytesForCurrentThread();
                window();
                after = GC.GetAllocatedBytesForCurrentThread();
            }
            finally
            {
                held = GCSettings.LatencyMode == GCLatencyMode.NoGCRegion;
                if (held)
                {
                    GC.EndNoGCRegion();
                }
            }

            if (!held)
            {
                throw new InvalidOperationException(
                    "#463: a garbage collection ran inside the measured window, so its allocation count is not exact; " +
                    "the measurement is invalid, not a finding about the code under test.");
            }

            return after - before;
        }
    }
}

/// <summary>#463: the helper itself. A collection inside the window is an
/// invalid measurement, never a byte count; a real allocation is still
/// counted; and an empty window is exactly zero.</summary>
[Collection(ExactAllocationCollection.Name)]
public class ExactAllocationsTests
{
    [Fact]
    public void AnEmptyWindowIsExactlyZero()
    {
        Assert.Equal(0, ExactAllocations.Measure(() => { }));
    }

    [Fact]
    public void ARealAllocationIsStillCounted()
    {
        object?[] keep = new object?[1000];
        long allocated = ExactAllocations.Measure(() =>
        {
            for (int index = 0; index < keep.Length; index++)
            {
                keep[index] = new object();
            }
        });

        // At least one object header per allocation, 1 000 times: nothing
        // per-call can hide under the measurement.
        Assert.True(allocated >= 1000 * IntPtr.Size * 2, $"only {allocated} bytes counted for 1 000 objects");
        GC.KeepAlive(keep);
    }

    /// <summary>The isolation holds only while every exact-allocation test
    /// measures through <see cref="ExactAllocations"/> and sits in the
    /// non-parallel collection. Text audit over the test project.</summary>
    [Fact]
    public void EveryAllocationTestUsesTheExactWindowInTheIsolatedCollection()
    {
        string root = TestRoot();
        var rawCounters = new List<string>();
        var measured = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal) ||
                relative == "ExactAllocations.cs")
            {
                continue;
            }

            string text = File.ReadAllText(file);
            if (text.Contains("GetAllocatedBytesForCurrentThread", StringComparison.Ordinal))
            {
                rawCounters.Add(relative);
            }

            if (text.Contains("ExactAllocations.Measure(", StringComparison.Ordinal))
            {
                measured.Add(relative);
                Assert.True(
                    text.Contains("[Collection(ExactAllocationCollection.Name)]", StringComparison.Ordinal),
                    relative + " measures exact allocations outside the isolated collection");
            }
        }

        Assert.Empty(rawCounters);
        measured.Sort(StringComparer.Ordinal);
        Assert.Equal(new[] { "RouteFollowMathTests.cs", "SailingRouteFollowControllerTests.cs" }, measured);

        var definition = typeof(ExactAllocationCollection).GetCustomAttributes(typeof(CollectionDefinitionAttribute), false);
        Assert.True(Assert.IsType<CollectionDefinitionAttribute>(Assert.Single(definition)).DisableParallelization);
    }

    private static string TestRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output."),
            "src", "ConcernedCartographer.Tests");
    }

    [Fact]
    public void ACollectionInsideTheWindowIsRefusedNotReportedAsBytes()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => ExactAllocations.Measure(() => GC.Collect()));
        Assert.Contains("invalid", refused.Message);
        Assert.NotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
    }
}
