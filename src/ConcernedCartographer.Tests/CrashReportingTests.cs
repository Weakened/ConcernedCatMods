using TheConcernedCat.ConcernedCartographer.Reporting;

namespace ConcernedCartographer.Tests;

/// <summary>#97 crash-reporting privacy suite. The critical block is the
/// forbidden-field matrix: every category the privacy policy forbids is
/// intentionally planted into exception text and context data, and the
/// COMPLETE outgoing envelope (captured from the reporter's transport
/// seam — the exact bytes that would be POSTed) is proven not to contain
/// it. Also covers consent gating, dedupe/caps, bounded queue, DSN
/// parsing, and release identity.</summary>
public class CrashReportingTests
{
    private static CrashReportContext Context(Func<CrashReportRuntimeState>? state = null)
    {
        return new CrashReportContext
        {
            Release = "ConcernedCartographer@1.0.0+abc123def",
            ModVersion = "1.0.0",
            ValheimVersion = "0.221.12",
            UnityVersion = "6000.0.61f1",
            BepInExVersion = "5.4.23.3",
            JotunnVersion = "2.29.2",
            RuntimeState = state ?? (() => new CrashReportRuntimeState(multiplayer: false, noMap: false, mapOpen: true)),
        };
    }

    private const string Dsn = "https://publickey123@o12345.ingest.sentry.io/6789";

    private sealed class CapturingTransport
    {
        public readonly List<string> Bodies = new();
        public readonly List<string> AuthHeaders = new();
        public readonly List<string> Urls = new();
        private readonly object _lock = new();

        public bool Send(string url, string auth, string body)
        {
            lock (_lock)
            {
                Urls.Add(url);
                AuthHeaders.Add(auth);
                Bodies.Add(body);
            }

            return true;
        }
    }

    // ------------------------------------------------------------------
    // Forbidden-field matrix
    // ------------------------------------------------------------------

    [Fact]
    public void ForbiddenPatterns_InExceptionText_NeverReachTheOutgoingEnvelope()
    {
        var transport = new CapturingTransport();
        using var reporter = new SentryCrashReporter(Dsn, transport.Send);
        reporter.Initialize(Context());
        reporter.ConsentGranted = true;

        const string steamId = "76561198012345678";
        const string ipAddress = "192.168.1.23";
        const string serverUrl = "https://myserver.example.com:2456/join";
        const string machineUser = "erenc";
        const string worldFile = "PlainsHomeWorld";
        const string characterFile = "ErikTheBold";
        const string coordinates = "(1234.5, -567.8, 42.0)";
        const string hexSecret = "deadbeefcafe0123456789abcdef0123456789abcdef0123456789abcdef0123";
        const string token = "AAAAB3NzaC1yc2EAAAADAQABAAABgQDposeidonXYZ12345";

        string raw =
            "System.IO.IOException: Sharing violation on " +
            $"C:\\Users\\{machineUser}\\AppData\\LocalLow\\IronGate\\Valheim\\worlds_local\\{worldFile}.db " +
            $"while syncing {characterFile}.fch for steam {steamId} at {coordinates} " +
            $"via {serverUrl} host {ipAddress}:2456 token {token} secret {hexSecret}\n" +
            $"  at TheConcernedCat.ConcernedCartographer.Persistence.RoadPersistence.Save () in C:\\Users\\{machineUser}\\code\\RoadPersistence.cs:line 99";

        reporter.CaptureFatalSubsystemFailure("road persistence", raw);
        Assert.True(reporter.Flush(TimeSpan.FromSeconds(5)));

        string envelope = Assert.Single(transport.Bodies);
        Assert.DoesNotContain(steamId, envelope);
        Assert.DoesNotContain(ipAddress, envelope);
        Assert.DoesNotContain("myserver.example.com", envelope);
        Assert.DoesNotContain(machineUser, envelope);
        Assert.DoesNotContain(worldFile, envelope);
        Assert.DoesNotContain(characterFile, envelope);
        Assert.DoesNotContain("1234.5", envelope);
        Assert.DoesNotContain(hexSecret, envelope);
        Assert.DoesNotContain(token, envelope);
        Assert.DoesNotContain("C:\\Users", envelope);
        Assert.DoesNotContain("AppData", envelope);

        // The diagnostically useful parts survive.
        Assert.Contains("IOException", envelope);
        Assert.Contains("RoadPersistence.Save", envelope);
        Assert.Contains("road persistence", envelope);
    }

    [Fact]
    public void ForbiddenContextData_IsDroppedStructurally()
    {
        var forbidden = new Dictionary<string, string>
        {
            ["steamId"] = "76561198000000001",
            ["playerName"] = "ErenTheViking",
            ["characterName"] = "ErikTheBold",
            ["worldName"] = "PlainsHomeWorld",
            ["worldSeed"] = "wxYZseed42",
            ["serverAddress"] = "valheim.example.com:2456",
            ["serverPassword"] = "hunter2secret",
            ["ipAddress"] = "10.0.0.7",
            ["coordinates"] = "(100, 200)",
            ["pinName"] = "Secret Silver Stash",
            ["pinNotes"] = "buried under the birch",
            ["pinTags"] = "silver,secret",
            ["routeName"] = "Smuggler Run",
            ["chat"] = "meet at the dock",
            ["savePath"] = "C:\\saves\\world.db",
            ["screenshot"] = "shot.png",
            ["logFile"] = "LogOutput.log contents",
            ["machineUser"] = "erenc",
            ["credential"] = "token-abc",
        };

        CrashReportEvent report = CrashReportEvent.Create(
            "pin adapter", "System.Exception: boom", fatal: true, Context(), forbidden);
        string json = report.ToJson("0123456789abcdef0123456789abcdef", new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc));
        string envelope = SentryEnvelopeCodec.Build(json, "0123456789abcdef0123456789abcdef", DateTime.UtcNow);

        foreach (KeyValuePair<string, string> entry in forbidden)
        {
            Assert.DoesNotContain(entry.Key, envelope);
            Assert.DoesNotContain(entry.Value, envelope);
        }

        Assert.Empty(report.AllowedData);
        Assert.Empty(CrashReportEvent.AllowedDataKeys);
    }

    // ------------------------------------------------------------------
    // Sanitizer patterns
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("open C:\\Users\\erenc\\Documents\\notes.txt now", "erenc")]
    [InlineData("open /home/erenc/.config/valheim/prefs now", "erenc")]
    [InlineData("dir D:\\SteamLibrary\\steamapps\\common\\Valheim\\file.cfg", "SteamLibrary")]
    [InlineData("ping 203.0.113.9:2456 done", "203.0.113.9")]
    [InlineData("join https://play.example.com/x?pw=1", "example.com")]
    [InlineData("pos (1024.5, -300.25) reached", "1024.5")]
    [InlineData("steam 76561198012345678 linked", "76561198012345678")]
    [InlineData("world PlainsHome.db missing", "PlainsHome")]
    [InlineData("character ErikTheBold.fch corrupt", "ErikTheBold")]
    public void Sanitizer_ScrubsEachForbiddenShape(string input, string mustVanish)
    {
        string sanitized = CrashReportSanitizer.Sanitize(input, 4000);
        Assert.DoesNotContain(mustVanish, sanitized);
    }

    [Fact]
    public void Sanitizer_KeepsDiagnosticShape_AndCapsLength()
    {
        string sanitized = CrashReportSanitizer.Sanitize(
            "Could not save road atlas to C:\\Users\\erenc\\cfg\\468215918.roads.tsv: disk full", 4000);
        Assert.Contains("Could not save road atlas", sanitized);
        Assert.Contains(".roads.tsv", sanitized);
        Assert.DoesNotContain("erenc", sanitized);
        Assert.DoesNotContain("468215918", sanitized);

        string longText = string.Concat(Enumerable.Repeat("lorem ipsum ", 500));
        string capped = CrashReportSanitizer.Sanitize(longText, 100);
        Assert.True(capped.Length <= 100 + "…[truncated]".Length);
        Assert.EndsWith("…[truncated]", capped);
    }

    [Fact]
    public void ExceptionParsing_ExtractsTypeMessageAndStack()
    {
        CrashReportEvent report = CrashReportEvent.Create(
            "workbench",
            "System.NullReferenceException: Object reference not set\n" +
            "  at TheConcernedCat.ConcernedCartographer.Map.PinWorkbenchPanel.ApplyClicked () [0x0] in <hash>:0\n" +
            "  at UnityEngine.Events.UnityEvent.Invoke () [0x0] in <hash>:0",
            fatal: false,
            Context());

        Assert.Equal("System.NullReferenceException", report.ExceptionType);
        Assert.Equal("Object reference not set", report.ExceptionMessage);
        Assert.Contains("PinWorkbenchPanel.ApplyClicked", report.StackTrace);
        Assert.False(report.Fatal);
    }

    // ------------------------------------------------------------------
    // Consent, dedupe, caps, reliability
    // ------------------------------------------------------------------

    [Fact]
    public void WithoutConsent_NothingIsEverQueuedOrSent()
    {
        var transport = new CapturingTransport();
        using var reporter = new SentryCrashReporter(Dsn, transport.Send);
        reporter.Initialize(Context());
        reporter.ConsentGranted = false;

        reporter.CaptureException("pins", "System.Exception: a\n  at X.Y ()");
        reporter.CaptureFatalSubsystemFailure("roads", "Road surveyor failed: boom");

        Assert.True(reporter.Flush(TimeSpan.Zero));
        Assert.Empty(transport.Bodies);
    }

    [Fact]
    public void DuplicateFailures_AreSubmittedOnce()
    {
        var transport = new CapturingTransport();
        using var reporter = new SentryCrashReporter(Dsn, transport.Send);
        reporter.Initialize(Context());
        reporter.ConsentGranted = true;

        for (int index = 0; index < 5; index++)
        {
            reporter.CaptureFatalSubsystemFailure("pin adapter", "Pin adapter failed: same problem\n  at A.B ()");
        }

        reporter.CaptureFatalSubsystemFailure("pin adapter", "Pin adapter failed: DIFFERENT problem\n  at C.D ()");
        Assert.True(reporter.Flush(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, transport.Bodies.Count);
    }

    [Fact]
    public void Throttle_CapsSessionAndNotifiesOncePerSubsystem()
    {
        var throttle = new CrashReportThrottle(maxEventsPerSession: 3);
        Assert.True(throttle.ShouldSend("a"));
        Assert.False(throttle.ShouldSend("a"));
        Assert.True(throttle.ShouldSend("b"));
        Assert.True(throttle.ShouldSend("c"));
        Assert.False(throttle.ShouldSend("d"));

        Assert.True(throttle.ShouldNotify("pins"));
        Assert.False(throttle.ShouldNotify("pins"));
        Assert.True(throttle.ShouldNotify("roads"));
    }

    [Fact]
    public void QueueIsBounded_ExcessEventsAreDroppedNotBuffered()
    {
        using var release = new System.Threading.ManualResetEventSlim(false);
        var sent = new List<string>();
        var gate = new object();
        using var reporter = new SentryCrashReporter(Dsn, (_, _, body) =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            lock (gate)
            {
                sent.Add(body);
            }

            return true;
        });
        reporter.Initialize(Context());
        reporter.ConsentGranted = true;

        for (int index = 0; index < 20; index++)
        {
            reporter.CaptureException("bulk", $"System.Exception: unique {index}\n  at F{index}.G ()");
        }

        release.Set();
        reporter.Flush(TimeSpan.FromSeconds(5));
        lock (gate)
        {
            Assert.InRange(sent.Count, 1, SentryCrashReporter.MaxQueuedEvents + 1);
        }
    }

    [Fact]
    public void InvalidDsn_LeavesTheReporterInert()
    {
        foreach (string bad in new[] { "", "not a dsn", "https://nokey.example/12", "https://key@host/", "https://key@host/notanumber" })
        {
            var transport = new CapturingTransport();
            using var reporter = new SentryCrashReporter(bad, transport.Send);
            reporter.Initialize(Context());
            reporter.ConsentGranted = true;
            reporter.CaptureException("x", "System.Exception: y");
            Assert.False(reporter.IsOperational);
            Assert.True(reporter.Flush(TimeSpan.Zero));
            Assert.Empty(transport.Bodies);
        }
    }

    [Fact]
    public void Dsn_ParsesToEnvelopeUrlAndAuthHeader()
    {
        Assert.True(SentryDsn.TryParse(Dsn, out SentryDsn? parsed));
        Assert.Equal("https://o12345.ingest.sentry.io/api/6789/envelope/", parsed!.EnvelopeUrl);
        Assert.Contains("sentry_key=publickey123", parsed.AuthHeader);
        Assert.Contains("sentry_version=7", parsed.AuthHeader);
        Assert.DoesNotContain("secret", parsed.AuthHeader);
    }

    [Fact]
    public void Envelope_CarriesReleaseIdentityAndExactLength()
    {
        CrashReportEvent report = CrashReportEvent.Create(
            "pins", "System.Exception: x", fatal: true, Context());
        string json = report.ToJson("00000000000000000000000000000001", new DateTime(2026, 8, 28, 8, 0, 0, DateTimeKind.Utc));
        string envelope = SentryEnvelopeCodec.Build(json, "00000000000000000000000000000001", new DateTime(2026, 8, 28, 8, 0, 1, DateTimeKind.Utc));

        Assert.Contains("\"release\":\"ConcernedCartographer@1.0.0+abc123def\"", envelope);
        string[] lines = envelope.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains($"\"length\":{System.Text.Encoding.UTF8.GetByteCount(json)}", lines[1]);
        Assert.Contains("\"session.map_open\":\"true\"", envelope);
        Assert.Contains("\"game.valheim\":\"0.221.12\"", envelope);
    }

    [Fact]
    public void RuntimeStateProviderFailure_FallsBackToFalse()
    {
        CrashReportContext context = Context(() => throw new InvalidOperationException("boom"));
        CrashReportEvent report = CrashReportEvent.Create("pins", "System.Exception: x", fatal: false, context);
        Assert.False(report.Multiplayer);
        Assert.False(report.NoMap);
        Assert.False(report.MapOpen);
    }

    [Fact]
    public void SubsystemInference_MatchesTheModsErrorShapes()
    {
        Assert.Equal("Pin adapter", CrashSubsystems.Infer("Pin adapter failed and was disabled for this session (store data is safe): boom"));
        Assert.Equal("Chunk recovery", CrashSubsystems.Infer("Chunk recovery failed and was disabled for this session: x"));
        Assert.Equal("save road atlas", CrashSubsystems.Infer("Could not save road atlas to C:\\x\\y.tsv: disk full"));
        Assert.Equal("Workbench", CrashSubsystems.Infer("Workbench invariant violated: the panel is hidden but still owned the GUI input block; releasing it now."));
        Assert.Equal("unknown", CrashSubsystems.Infer("   "));
    }

    [Fact]
    public void NullReporter_AcceptsEverythingAndDoesNothing()
    {
        using var reporter = new NullCrashReporter();
        reporter.Initialize(new CrashReportContext());
        reporter.ConsentGranted = true;
        reporter.CaptureException("a", "b");
        reporter.CaptureFatalSubsystemFailure("c", "d");
        Assert.True(reporter.Flush(TimeSpan.Zero));
    }
}
