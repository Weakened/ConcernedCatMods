using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

namespace ConcernedTeamster.Tests;

/// <summary>CT-021: the Cartographer gate must decide Available / Absent /
/// VersionTooLow / ProbeFailed from a plugin lookup without ever throwing,
/// name its reasons actionably, and compose exactly one log line per state.
/// Fake types mirror the member names of the real contract chain recorded in
/// CARTOGRAPHER_CONTRACT.md; the probe resolves each hop from the previous
/// hop's metadata, so shape — not type identity — is what these fakes prove.</summary>
public class CartographerGateTests
{
    // -- fake Cartographer surface (member names match the contract) --

    private sealed class FakePoint
    {
        public FakePoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }
    }

    private sealed class FakeId
    {
        public FakeId(Guid value)
        {
            Value = value;
        }

        public Guid Value { get; }
    }

    private sealed class FakeRoute
    {
        public FakeId? Id { get; set; }

        public string Name { get; set; } = "";

        public bool Archived { get; set; }

        public List<FakePoint> Points { get; } = new();
    }

    private sealed class FakeRouteStore
    {
        public IEnumerable<FakeRoute> Living => Array.Empty<FakeRoute>();

        public long ChangeStamp => 0L;
    }

    /// <summary>Generic holders let each broken-shape test build the chain
    /// with one substituted link; the probe reads field TYPE metadata, so a
    /// null value is enough to carry the shape.</summary>
    private sealed class FakePluginOf<TRuntime>
    {
        private readonly TRuntime? _runtime;

        public FakePluginOf(TRuntime? runtime)
        {
            _runtime = runtime;
        }
    }

    private sealed class FakeRuntimeOf<TStore>
    {
        private readonly TStore? _routeStore;

        public FakeRuntimeOf(TStore? store)
        {
            _routeStore = store;
        }
    }

    private sealed class FakePluginWithoutRuntime
    {
    }

    private sealed class FakeStoreIntChangeStamp
    {
        public IEnumerable<FakeRoute> Living => Array.Empty<FakeRoute>();

        public int ChangeStamp => 0;
    }

    private sealed class FakeStoreLivingNotEnumerable
    {
        public int Living => 3;

        public long ChangeStamp => 0L;
    }

    private sealed class FakePointDoubleX
    {
        public double X { get; }

        public float Y { get; }

        public float Z { get; }
    }

    private sealed class FakeRouteDoubleXPoints
    {
        public FakeId? Id { get; set; }

        public string Name { get; set; } = "";

        public bool Archived { get; set; }

        public List<FakePointDoubleX> Points { get; } = new();
    }

    private sealed class FakeStoreDoubleXPoints
    {
        public IEnumerable<FakeRouteDoubleXPoints> Living => Array.Empty<FakeRouteDoubleXPoints>();

        public long ChangeStamp => 0L;
    }

    private static object CompletePlugin()
    {
        return new FakePluginOf<FakeRuntimeOf<FakeRouteStore>>(
            new FakeRuntimeOf<FakeRouteStore>(new FakeRouteStore()));
    }

    // -- availability decisions --

    [Fact]
    public void Evaluate_CompleteSurfaceAtFloorVersion_Available()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(new Version(0, 10, 0), CompletePlugin()));

        Assert.Equal(CartographerAvailability.Available, report.Availability);
        Assert.True(report.IsAvailable);
        Assert.Equal(12, report.VerifiedMemberCount);
        Assert.Equal("0.10.0", report.DetectedVersion);
        Assert.Contains("AVAILABLE", report.LogLine);
        Assert.Contains("0.10.0", report.LogLine);
    }

    [Fact]
    public void Evaluate_NewerVersionWithIntactMembers_Available()
    {
        // Above-floor versions are accepted only because every member still
        // verifies — the member probe is the forward-compatibility gate.
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(new Version(1, 2, 3), CompletePlugin()));

        Assert.Equal(CartographerAvailability.Available, report.Availability);
        Assert.Equal("1.2.3", report.DetectedVersion);
    }

    [Fact]
    public void Evaluate_NotFound_Absent()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.NotFound());

        Assert.Equal(CartographerAvailability.Absent, report.Availability);
        Assert.False(report.IsAvailable);
        Assert.Contains("not installed", report.LogLine);
        Assert.Contains("standalone", report.LogLine);
    }

    [Fact]
    public void Evaluate_VersionBelowFloor_VersionTooLow()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(new Version(0, 9, 7), CompletePlugin()));

        Assert.Equal(CartographerAvailability.VersionTooLow, report.Availability);
        Assert.Equal("0.9.7", report.DetectedVersion);
        Assert.Contains("0.9.7", report.LogLine);
        Assert.Contains("0.10.0", report.LogLine);
        Assert.Contains("update", report.LogLine);
    }

    [Fact]
    public void Evaluate_MissingVersion_VersionTooLow()
    {
        // No version means the floor cannot be proven; never guess.
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(null, CompletePlugin()));

        Assert.Equal(CartographerAvailability.VersionTooLow, report.Availability);
        Assert.Equal("unknown", report.DetectedVersion);
    }

    // -- probe-failure paths --

    [Fact]
    public void Evaluate_LookupThrows_ProbeFailedNamesExceptionType()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => throw new InvalidOperationException("registry torn"));

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("InvalidOperationException", report.Detail);
        Assert.Contains("did not verify", report.LogLine);
    }

    [Fact]
    public void Evaluate_NullLookup_ProbeFailed()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(null);

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
    }

    [Fact]
    public void Evaluate_LookupReturnsNull_ProbeFailed()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(() => null!);

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("returned nothing", report.Detail);
    }

    [Fact]
    public void Evaluate_FoundWithoutInstance_ProbeFailed()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(new Version(0, 10, 1), null));

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("instance is not available", report.Detail);
        Assert.Equal("0.10.1", report.DetectedVersion);
    }

    [Fact]
    public void Evaluate_PluginWithoutRuntimeField_ProbeFailedNamesMember()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(new Version(0, 10, 0), new FakePluginWithoutRuntime()));

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("CartographerPlugin._runtime (field not found)", report.Detail);
    }

    [Fact]
    public void Evaluate_RuntimeWithoutStoreField_ProbeFailedNamesMember()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(new Version(0, 10, 0), new FakePluginOf<object>(null)));

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("CartographerRuntime._routeStore (field not found)", report.Detail);
    }

    [Fact]
    public void Evaluate_ChangeStampWrongType_ProbeFailedExplainsBothTypes()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(
                new Version(0, 10, 0),
                new FakePluginOf<FakeRuntimeOf<FakeStoreIntChangeStamp>>(null)));

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("RouteStore.ChangeStamp (property type is Int32, expected Int64)", report.Detail);
    }

    [Fact]
    public void Evaluate_LivingNotEnumerable_ProbeFailedReportsRouteTypeUnresolved()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(
                new Version(0, 10, 0),
                new FakePluginOf<FakeRuntimeOf<FakeStoreLivingNotEnumerable>>(null)));

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("AtlasRoute.Id (type not found)", report.Detail);
    }

    [Fact]
    public void Evaluate_PointCoordinateWrongType_ProbeFailedNamesLeafMember()
    {
        CartographerCapabilityReport report = CartographerGate.Evaluate(
            () => CartographerLookup.Detected(
                new Version(0, 10, 0),
                new FakePluginOf<FakeRuntimeOf<FakeStoreDoubleXPoints>>(null)));

        Assert.Equal(CartographerAvailability.ProbeFailed, report.Availability);
        Assert.Contains("RoadPoint.X (property type is Double, expected Single)", report.Detail);
    }

    // -- log-line discipline --

    [Fact]
    public void EveryState_ComposesExactlyOneLine()
    {
        var reports = new List<CartographerCapabilityReport>
        {
            CartographerGate.Evaluate(
                () => CartographerLookup.Detected(new Version(0, 10, 0), CompletePlugin())),
            CartographerGate.Evaluate(() => CartographerLookup.NotFound()),
            CartographerGate.Evaluate(
                () => CartographerLookup.Detected(new Version(0, 9, 0), CompletePlugin())),
            CartographerGate.Evaluate(() => throw new InvalidOperationException("boom")),
        };

        Assert.Equal(4, reports.Select(r => r.Availability).Distinct().Count());
        foreach (CartographerCapabilityReport report in reports)
        {
            Assert.False(string.IsNullOrWhiteSpace(report.LogLine));
            Assert.DoesNotContain('\n', report.LogLine);
        }
    }

    [Fact]
    public void HiddenStates_NeverReportVerifiedMembers()
    {
        Assert.Equal(0, CartographerGate.Evaluate(() => CartographerLookup.NotFound()).VerifiedMemberCount);
        Assert.Equal(
            0,
            CartographerGate.Evaluate(
                () => CartographerLookup.Detected(new Version(0, 1, 0), CompletePlugin())).VerifiedMemberCount);
        Assert.Equal(
            0,
            CartographerGate.Evaluate(
                () => CartographerLookup.Detected(new Version(0, 10, 0), new FakePluginWithoutRuntime()))
                .VerifiedMemberCount);
    }
}
