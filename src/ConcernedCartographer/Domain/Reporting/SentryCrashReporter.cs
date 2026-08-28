using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Sentry-backed crash reporter (#97), implemented directly
/// against Sentry's public envelope-ingestion HTTP endpoint — no SDK is
/// bundled, the mod keeps shipping one DLL, and the exact outgoing bytes
/// are produced by tested pure code (<see cref="CrashReportEvent"/> /
/// <see cref="SentryEnvelopeCodec"/>).
///
/// Reliability contract: nothing is queued before consent; the queue is
/// bounded; each envelope gets exactly one delivery attempt (no retries,
/// silent offline failure); the sender is a background thread that can
/// never delay game shutdown; every public member swallows its own
/// failures. Duplicate failures are locally deduplicated per session.</summary>
internal sealed class SentryCrashReporter : ICrashReporter
{
    public const int MaxQueuedEvents = 8;
    private const int SendTimeoutMilliseconds = 5000;

    private readonly SentryDsn? _dsn;
    private readonly Func<string, string, string, bool>? _transport;
    private readonly CrashReportThrottle _throttle = new();
    private readonly Queue<string> _queue = new();
    private readonly object _gate = new();
    private CrashReportContext _context = new();
    private Thread? _worker;
    private volatile bool _disposed;
    private volatile int _pending;

    /// <summary>transport(url, authHeader, envelopeBody) → success. The
    /// default posts over HTTPS; tests inject a capture function so the
    /// redaction suite asserts on the real outgoing body.</summary>
    public SentryCrashReporter(string dsn, Func<string, string, string, bool>? transport = null)
    {
        SentryDsn.TryParse(dsn, out _dsn);
        _transport = transport ?? (_dsn is null ? null : HttpPost);
    }

    public bool ConsentGranted { get; set; }

    /// <summary>False when no valid DSN is configured; the reporter then
    /// behaves exactly like <see cref="NullCrashReporter"/>.</summary>
    public bool IsOperational => _dsn is not null && !_disposed;

    public void Initialize(CrashReportContext context)
    {
        _context = context ?? new CrashReportContext();
    }

    public void CaptureException(string subsystem, string rawExceptionText)
    {
        Capture(subsystem, rawExceptionText, fatal: false);
    }

    public void CaptureFatalSubsystemFailure(string subsystem, string message)
    {
        Capture(subsystem, message, fatal: true);
    }

    public bool Flush(TimeSpan timeout)
    {
        try
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (_pending > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }

            return _pending == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            Flush(TimeSpan.FromSeconds(2));
            lock (_gate)
            {
                _queue.Clear();
                Monitor.PulseAll(_gate);
            }
        }
        catch
        {
            // The worker is a background thread; shutdown never waits on it.
        }
    }

    private void Capture(string subsystem, string rawText, bool fatal)
    {
        try
        {
            // Consent gate FIRST: nothing is built or queued without it.
            if (_disposed || !ConsentGranted || _dsn is null || _transport is null)
            {
                return;
            }

            CrashReportEvent report = CrashReportEvent.Create(subsystem, rawText, fatal, _context);
            if (!_throttle.ShouldSend(report.Fingerprint))
            {
                return;
            }

            string eventId = Guid.NewGuid().ToString("N");
            DateTime now = DateTime.UtcNow;
            string envelope = SentryEnvelopeCodec.Build(report.ToJson(eventId, now), eventId, now);
            lock (_gate)
            {
                if (_queue.Count >= MaxQueuedEvents)
                {
                    return;
                }

                _queue.Enqueue(envelope);
                _pending = _queue.Count;
                EnsureWorker();
                Monitor.Pulse(_gate);
            }
        }
        catch
        {
            // Crash reporting must never harm the game.
        }
    }

    private void EnsureWorker()
    {
        if (_worker is { IsAlive: true })
        {
            return;
        }

        _worker = new Thread(SendLoop)
        {
            IsBackground = true,
            Name = "CC-CrashReporter",
        };
        _worker.Start();
    }

    private void SendLoop()
    {
        try
        {
            while (!_disposed)
            {
                string? envelope = null;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        Monitor.Wait(_gate, 1000);
                    }

                    if (_queue.Count > 0)
                    {
                        envelope = _queue.Dequeue();
                    }
                }

                if (envelope is not null)
                {
                    try
                    {
                        // One attempt; offline/failed sends are dropped.
                        _transport?.Invoke(_dsn!.EnvelopeUrl, _dsn.AuthHeader, envelope);
                    }
                    catch
                    {
                        // Silent by contract.
                    }

                    lock (_gate)
                    {
                        _pending = _queue.Count;
                    }
                }
            }
        }
        catch
        {
            // A dead sender only means no more reports this session.
        }
    }

#pragma warning disable SYSLIB0014 // WebRequest: intentional — available on net48/Mono without extra references.
    private static bool HttpPost(string url, string authHeader, string body)
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        }
        catch
        {
            // Platform default stays in effect.
        }

        var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        request.Method = "POST";
        request.ContentType = "application/x-sentry-envelope";
        request.Headers["X-Sentry-Auth"] = authHeader;
        request.Timeout = SendTimeoutMilliseconds;
        request.ReadWriteTimeout = SendTimeoutMilliseconds;

        byte[] bytes = Encoding.UTF8.GetBytes(body);
        request.ContentLength = bytes.Length;
        using (System.IO.Stream stream = request.GetRequestStream())
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        using var response = (System.Net.HttpWebResponse)request.GetResponse();
        return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
    }
#pragma warning restore SYSLIB0014
}
