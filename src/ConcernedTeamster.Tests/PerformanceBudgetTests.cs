using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Brake;
using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;
using TheConcernedCat.ConcernedTeamster.Domain.Carts;
using TheConcernedCat.ConcernedTeamster.Domain.Net;
using TheConcernedCat.ConcernedTeamster.Domain.Routes;
using TheConcernedCat.ConcernedTeamster.Domain.Terrain;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;

namespace ConcernedTeamster.Tests;

/// <summary>CT-048: formal timing/allocation coverage for every real per-tick
/// or per-call Domain entry point that did not already have it —
/// <see cref="RouteProfiler.Advance"/> (CT-023's per-frame budget, the only
/// other genuine per-frame Domain tick besides <c>TelemetrySampler.Tick</c>),
/// <see cref="BrakeLifecycle.EvaluateTick"/>, <see cref="TripRecorder"/>'s
/// feed/finalize/drain cycle over many trips, the two O(n)-over-samples trip
/// presenters at their hard sample cap, and <see cref="NetworkInputGuard"/>.
/// Numbers, rationale, and the in-game-only items this cannot reach are in
/// docs/mods/concerned-teamster/PERFORMANCE_BUDGETS.md. Wall-clock budgets
/// use generous multiples of the one-machine measured value (the same
/// convention as the existing CT-040 scale tests) so they are not brittle to
/// machine-to-machine or JIT/GC noise.</summary>
public class PerformanceBudgetTests
{
    // -- CT-023 route profiler: per-frame while a profile build is in flight --

    private static bool FlatProbe(float x, float z, out float height, out TerrainSurfaceKind surface)
    {
        height = 10f;
        surface = TerrainSurfaceKind.Untouched;
        return true;
    }

    private static IReadOnlyList<CartographerRoutePoint> StraightX(float lengthMeters)
    {
        return new List<CartographerRoutePoint> { new(0f, 0f, 0f), new(lengthMeters, 0f, 0f) };
    }

    [Fact]
    public void RouteProfiler_Advance_ConsumingWorstCaseProfile_AllocatesNothingAndCompletesWithinBudget()
    {
        // Long enough that construction coarsens spacing to hit the hard
        // position cap — the largest any single profile build can ever be.
        var profiler = new RouteProfiler(StraightX(50_000f), FlatProbe);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!profiler.IsComplete)
        {
            // 24 = RoutePickerPanel.ProfileSamplesPerFrame, CT-023's chosen
            // per-frame batch size (Domain cannot reference the Ui-layer
            // constant, so the literal is restated here).
            profiler.Advance(24);
        }

        stopwatch.Stop();
        long allocated = AllocationProbe.MeasureAfterWarmup(
            () =>
            {
                var allocationProfiler = new RouteProfiler(StraightX(50_000f), FlatProbe);
                return () =>
                {
                    while (!allocationProfiler.IsComplete)
                    {
                        allocationProfiler.Advance(24);
                    }
                };
            },
            warmupIterations: 1,
            measuredIterations: 1);

        Assert.Equal(RouteProfiler.MaxSamplePositions, profiler.TotalSamplesConsumed);
        Assert.Equal(0L, allocated);
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"Consuming a full {RouteProfiler.MaxSamplePositions}-position profile in 24-sample " +
            $"batches took {stopwatch.ElapsedMilliseconds} ms against fake terrain — budget is 200 ms.");
    }

    [Fact]
    public void RouteProfiler_Advance_AfterCompletion_AllocatesNothing()
    {
        var profiler = new RouteProfiler(StraightX(400f), FlatProbe);
        while (!profiler.IsComplete)
        {
            profiler.Advance(24);
        }

        // Warm and measure on a dedicated thread. One million measured
        // calls represent several hours at 60 fps and must allocate exactly zero.
        long allocated = AllocationProbe.MeasureAfterWarmup(
            () => profiler.Advance(24),
            warmupIterations: 1_000_000,
            measuredIterations: 1_000_000);

        Assert.Equal(0L, allocated);
    }

    // -- CT-012 brake lifecycle: evaluated every due tick while engaged --

    [Fact]
    public void BrakeLifecycle_EvaluateTick_LongRun_AllocatesNothing()
    {
        var lifecycle = new BrakeLifecycle();
        var facts = new BrakeFacts(
            capabilityOk: true, inWorld: true, cartExists: true,
            isLocalAuthority: true, isAttached: false, distanceMeters: 1f);
        Assert.Equal(BrakeAction.Engage, lifecycle.EvaluateToggle("cart-1", facts, out _));
        lifecycle.MarkEngaged("cart-1"); // confirms physics engaged, as the adapter would after EvaluateToggle
        Assert.True(lifecycle.IsEngaged);

        // A full warmup keeps tiered-JIT work outside the exact-zero window.
        long allocated = AllocationProbe.MeasureAfterWarmup(
            () => lifecycle.EvaluateTick(facts, out _),
            warmupIterations: 200_000,
            measuredIterations: 200_000);

        Assert.Equal(0L, allocated);
    }

    // -- CT-016 trip recorder: feed/finalize/drain cycle over many trips --

    private static CartTelemetry PulledTelemetryAt(double nowSeconds)
    {
        CartSnapshot snapshot = CartSnapshot.Create(
            "cart-1", baseMass: 20f, cargoWeight: 10f, cargoDataAvailable: true,
            itemWeightMassFactor: 1f, isAttached: false, isPulledByLocalPlayer: true);
        return CartTelemetry.Create(snapshot, velocityAvailable: true,
            speedMetersPerSecond: 1f, verticalSpeedMetersPerSecond: 0f,
            gradeAvailable: false, instantGradePercent: 0f, smoothedGradePercent: 0f,
            gradeDirection: GradeDirection.Level, surface: TerrainSurfaceKind.Unavailable,
            sampleTimeSeconds: nowSeconds);
    }

    [Fact]
    public void TripRecorder_FeedDetachDrainOverManyTripCycles_AllocationStaysFlat()
    {
        TripRecorderOptions options = TripRecorderOptions.CreateClamped(
            recordSpacingSeconds: 1f, maxSamplesPerTrip: 50, maxTripsRetained: 50);
        var recorder = new TripRecorder(options);
        double now = 0.0;

        void RunOneTripCycle()
        {
            for (int sample = 0; sample < 49; sample++)
            {
                now += 1.1; // exceeds RecordSpacingSeconds so every call records
                recorder.FeedPulled(PulledTelemetryAt(now));
            }

            // A real detach: debounce starts, then a later tick past the
            // debounce window finalizes the trip (49 >= MinSamplesToKeep).
            now += 0.5;
            recorder.NotifyNotPulled(now);
            now += TripRecorderOptions.EndDebounceSeconds + 1.0;
            recorder.NotifyNotPulled(now);
            recorder.DrainFinishedTrips();
        }

        // Warm up (JIT + first backing-array growth for _current/_finished).
        for (int cycle = 0; cycle < 5; cycle++)
        {
            RunOneTripCycle();
        }

        long firstWindowBytes = MeasureAllocatedBytes(() =>
        {
            for (int cycle = 0; cycle < 100; cycle++)
            {
                RunOneTripCycle();
            }
        });

        long secondWindowBytes = MeasureAllocatedBytes(() =>
        {
            for (int cycle = 0; cycle < 100; cycle++)
            {
                RunOneTripCycle();
            }
        });

        // Each finished trip legitimately allocates (its samples copy out of
        // the recorder into an immutable Trip) — zero is the wrong bar here.
        // What matters over a long session is that 100 more identical cycles
        // cost no more than the first 100 did, like the sampler's own
        // long-session allocation test.
        Assert.True(secondWindowBytes <= firstWindowBytes * 2,
            $"Second 100-trip-cycle window allocated {secondWindowBytes} B vs the first window's " +
            $"{firstWindowBytes} B — allocation should stay roughly flat across equal-length windows " +
            "once warmed up, not grow with session length.");
    }

    // -- CT-019 route bottleneck / trip comparison: O(n) over samples,
    // triggered on user input (keystroke, row select), bounded by the hard
    // per-trip sample cap --

    private static Trip BuildWorstCaseTrip(int sampleCount, string cartId)
    {
        var samples = new TripSample[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            float grade = index % 13 == 0 ? 18f : 1f;
            samples[index] = new TripSample(index, index * 10.0f, 0f, grade, 1.5f, 250f);
        }

        return new Trip(1, cartId, samples);
    }

    [Fact]
    public void RouteBottleneckPresenter_Present_WorstCaseTripSize_CompletesWithinBudget()
    {
        Trip trip = BuildWorstCaseTrip(TripRecorderOptions.MaxMaxSamplesPerTrip, "1:1");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        RouteBottleneckPresenter.ViewModel viewModel =
            RouteBottleneckPresenter.Present(trip, null, null, "250");
        stopwatch.Stop();

        Assert.True(viewModel.Available);
        Assert.True(stopwatch.ElapsedMilliseconds < 50,
            $"Presenting a {TripRecorderOptions.MaxMaxSamplesPerTrip}-sample trip took " +
            $"{stopwatch.ElapsedMilliseconds} ms — budget is 50 ms (this runs on every keystroke of " +
            "the hypothetical-mass field).");
    }

    [Fact]
    public void TripComparisonPresenter_Present_WorstCaseTripSizes_CompletesWithinBudget()
    {
        Trip tripA = BuildWorstCaseTrip(TripRecorderOptions.MaxMaxSamplesPerTrip, "1:1");
        Trip tripB = BuildWorstCaseTrip(TripRecorderOptions.MaxMaxSamplesPerTrip, "1:2");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        TripComparisonPresenter.Present(tripA, tripB);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"Comparing two {TripRecorderOptions.MaxMaxSamplesPerTrip}-sample trips took " +
            $"{stopwatch.ElapsedMilliseconds} ms — budget is 100 ms (on-demand, but still user-facing " +
            "latency on row select).");
    }

    // -- CT-029 network input guard: sanitizes every network-derived field
    // CartSnapshot.Create reads, up to MaxCartsPerTick times per due tick --

    [Fact]
    public void NetworkInputGuard_AllGuards_LongRun_AllocateNothing()
    {
        float[] rawValues =
        {
            100f, -5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e12f, 0f,
        };

        int rawIndex = 0;
        long allocated = AllocationProbe.MeasureAfterWarmup(
            () =>
            {
                float raw = rawValues[rawIndex++ % rawValues.Length];
                NetworkInputGuard.Mass(raw);
                NetworkInputGuard.MassFactor(raw);
                NetworkInputGuard.Speed(raw);
                NetworkInputGuard.Grade(raw);
            },
            warmupIterations: 50_000,
            measuredIterations: 50_000);

        Assert.Equal(0L, allocated);
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
