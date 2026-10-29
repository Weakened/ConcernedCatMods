using System.Reflection;
using System.Runtime;

namespace ConcernedTeamster.Tests;

/// <summary>#469: the one collection whose tests measure allocation counts.
/// It runs on its own, after the parallel collections, so no other test is
/// allocating while a window is open. (Teamster's copy of the #463/#468 fix in
/// Cartographer; test projects share no code.)</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExactAllocationCollection
{
    public const string Name = "Exact allocation windows (no parallel neighbours)";
}

/// <summary>#469: an allocation count that a busy machine cannot inflate.
///
/// Observed in #463, not proven from the runtime's source: a thread that
/// allocated nothing was sometimes charged about one allocation quantum by
/// <c>GC.GetAllocatedBytesForCurrentThread</c> (7 936 - 8 088 bytes) while
/// other threads allocated and gen-0 collections ran; 0 every time with no
/// allocating neighbours, and 0 every time inside a no-GC region even with
/// them. An exact-zero bound, or a ratio between two small windows, can go red
/// on that alone.
///
/// The window runs inside a no-GC region sized for it, so no collection can
/// happen in it and every byte it allocates is still counted. If a collection
/// happens anyway, there are two cases:
/// - the window itself allocated at least its budget: the count is returned,
///   because it is a true lower bound and already above any ceiling a caller
///   sets below its budget;
/// - otherwise: the measurement is refused as invalid. It is never reported as
///   bytes, never retried, and never given a tolerance.</summary>
internal static class ExactAllocations
{
    /// <summary>Headroom for a window that should allocate nothing: the rest
    /// is for the runner's own threads.</summary>
    public const long DefaultBudgetBytes = 16L * 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>Bytes this thread allocated while <paramref name="window"/>
    /// ran. <paramref name="budgetBytes"/> must exceed what the window may
    /// legitimately allocate (callers pass several times their ceiling).
    /// Throws <see cref="InvalidOperationException"/> when no exact count
    /// could be taken.</summary>
    public static long Measure(Action window, long budgetBytes = DefaultBudgetBytes)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        lock (Gate)
        {
            if (!GC.TryStartNoGCRegion(budgetBytes))
            {
                throw new InvalidOperationException("#469: could not open a no-GC region, so no exact allocation count was taken.");
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
                    try
                    {
                        GC.EndNoGCRegion();
                    }
                    catch (InvalidOperationException)
                    {
                        // A collection landed between the check and the end.
                        held = false;
                    }
                }
            }

            long allocated = after - before;
            if (!held && allocated < budgetBytes)
            {
                throw new InvalidOperationException(
                    "#469: a garbage collection ran inside the measured window, so its allocation count is not exact; " +
                    "the measurement is invalid, not a finding about the code under test.");
            }

            return allocated;
        }
    }
}

/// <summary>#469: the helper itself, and the audit that keeps every
/// allocation measurement on it and in the isolated collection.</summary>
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

        Assert.True(allocated >= 1000 * IntPtr.Size * 2, $"only {allocated} bytes counted for 1 000 objects");
        GC.KeepAlive(keep);
    }

    [Fact]
    public void ACollectionInsideTheWindowIsRefusedNotReportedAsBytes()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => ExactAllocations.Measure(() => GC.Collect()));
        Assert.Contains("invalid", refused.Message);
        Assert.NotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
    }

    [Fact]
    public void AWindowThatOutgrowsItsBudgetReportsItsTrueBytes()
    {
        // A regression that allocates far more than expected must fail its
        // ceiling with a real number, not as an "invalid measurement".
        const long budget = 1L * 1024 * 1024;
        var keep = new List<byte[]>();
        long allocated = ExactAllocations.Measure(
            () =>
            {
                for (int index = 0; index < 64; index++)
                {
                    keep.Add(new byte[64 * 1024]);
                }
            },
            budget);

        Assert.True(allocated >= budget, $"{allocated} bytes reported for a window that allocated about 4 MiB");
        Assert.NotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        GC.KeepAlive(keep);
    }

    /// <summary>Text audit over the test project: no raw counter outside this
    /// file, and every file that measures (through this helper or through
    /// <see cref="AllocationProbe"/>) puts its measuring classes in the
    /// isolated collection. The listed classes are also checked by reflection.
    /// </summary>
    [Fact]
    public void EveryAllocationMeasurementUsesTheExactWindowInTheIsolatedCollection()
    {
        string root = TestRoot();
        var rawCounters = new List<string>();
        var measuring = new List<string>();
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

            if (relative != "AllocationProbe.cs" &&
                (text.Contains("ExactAllocations.Measure(", StringComparison.Ordinal) ||
                 text.Contains("AllocationProbe.MeasureAfterWarmup(", StringComparison.Ordinal)))
            {
                measuring.Add(relative);
                Assert.True(
                    text.Contains("[Collection(ExactAllocationCollection.Name)]", StringComparison.Ordinal),
                    relative + " measures allocations outside the isolated collection");
            }
        }

        Assert.Empty(rawCounters);
        measuring.Sort(StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                "AllocationProbeTests.cs",
                "PerformanceBudgetTests.cs",
                "TelemetrySamplerTests.cs",
                "TripPersistenceTests.cs",
            },
            measuring);

        foreach (Type type in new[]
        {
            typeof(AllocationProbeTests),
            typeof(PerformanceBudgetTests),
            typeof(TelemetrySamplerTests),
            typeof(TripPersistenceTests),
            typeof(ExactAllocationsTests),
        })
        {
            CustomAttributeData membership = Assert.Single(
                type.CustomAttributes, attribute => attribute.AttributeType == typeof(CollectionAttribute));
            Assert.Equal(ExactAllocationCollection.Name, membership.ConstructorArguments[0].Value);
        }

        object[] definition = typeof(ExactAllocationCollection).GetCustomAttributes(typeof(CollectionDefinitionAttribute), false);
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
            "src", "ConcernedTeamster.Tests");
    }
}
