using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The single owner of the crash reporter (#97): builds the
/// provider from the embedded/override DSN, keeps consent synchronized
/// with the Privacy setting (changes take effect immediately), captures
/// the mod's own Error-level log events (every fail-closed subsystem
/// disable, persistence/migration/decoder failure, and invariant
/// violation) plus Unity unhandled exceptions whose stack involves
/// Concerned Cartographer, and shows the once-per-subsystem player
/// notice. Feature classes never talk to the provider — they just log,
/// exactly as before.</summary>
internal sealed class CrashReportingHub : IDisposable
{
    private readonly CartographerSettings _settings;
    private readonly ICrashReporter _reporter;
    private readonly CrashReportThrottle _noticeThrottle = new();
    private ManualLogSource? _attachedSource;
    private bool _inHandler;
    private bool _disposed;

    public CrashReportingHub(CartographerSettings settings, CrashReportContext context)
    {
        _settings = settings;

        string dsn = settings.SentryDsn.Value.Trim();
        if (dsn.Length == 0)
        {
            dsn = CrashReportingConfig.EmbeddedSentryDsn;
        }

        ICrashReporter reporter = new NullCrashReporter();
        if (dsn.Length > 0)
        {
            var sentry = new SentryCrashReporter(dsn);
            if (sentry.IsOperational)
            {
                reporter = sentry;
            }
            else
            {
                sentry.Dispose();
            }
        }

        _reporter = reporter;
        _reporter.Initialize(context);
        SyncConsent();
        _settings.CrashReportingConsent.SettingChanged += HandleConsentChanged;
    }

    /// <summary>True when a report would actually leave the machine:
    /// consent granted AND a working provider is configured.</summary>
    public bool ReportingActive =>
        _settings.CrashReportingConsent.Value == CrashConsentState.Enabled &&
        _reporter is SentryCrashReporter { IsOperational: true };

    /// <summary>Subscribes the capture hooks. The log source must be the
    /// mod's own — other mods' failures are never captured.</summary>
    public void Attach(ManualLogSource ownSource)
    {
        _attachedSource = ownSource;
        ownSource.LogEvent += HandleOwnLogEvent;
        Application.logMessageReceived += HandleUnityLog;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _settings.CrashReportingConsent.SettingChanged -= HandleConsentChanged;
            if (_attachedSource is not null)
            {
                _attachedSource.LogEvent -= HandleOwnLogEvent;
            }

            Application.logMessageReceived -= HandleUnityLog;
        }
        catch
        {
            // Teardown must never throw.
        }

        _reporter.Dispose();
    }

    private void HandleConsentChanged(object sender, EventArgs e)
    {
        SyncConsent();
    }

    private void SyncConsent()
    {
        try
        {
            _reporter.ConsentGranted = _settings.CrashReportingConsent.Value == CrashConsentState.Enabled;
        }
        catch
        {
            // A broken setting read leaves consent off — the safe side.
        }
    }

    private void HandleOwnLogEvent(object sender, LogEventArgs args)
    {
        if (_disposed || _inHandler ||
            (args.Level != LogLevel.Error && args.Level != LogLevel.Fatal))
        {
            return;
        }

        _inHandler = true;
        try
        {
            string text = args.Data?.ToString() ?? "";
            string subsystem = CrashSubsystems.Infer(text);
            _reporter.CaptureFatalSubsystemFailure(subsystem, text);
            NotifyPlayer(subsystem);
        }
        catch
        {
            // Reporting must never harm the game.
        }
        finally
        {
            _inHandler = false;
        }
    }

    private void HandleUnityLog(string condition, string stackTrace, LogType type)
    {
        if (_disposed || type != LogType.Exception)
        {
            return;
        }

        try
        {
            bool ours =
                (stackTrace is not null && stackTrace.Contains("TheConcernedCat.ConcernedCartographer")) ||
                (condition is not null && condition.Contains("TheConcernedCat.ConcernedCartographer"));
            if (ours)
            {
                _reporter.CaptureException("unhandled", (condition ?? "") + "\n" + (stackTrace ?? ""));
            }
        }
        catch
        {
            // Never interfere with Unity's log pipeline.
        }
    }

    private void NotifyPlayer(string subsystem)
    {
        try
        {
            if (!_noticeThrottle.ShouldNotify(subsystem))
            {
                return;
            }

            string key = ReportingActive ? "privacy.noticeSent" : "privacy.noticeOff";
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, AtlasStrings.Format(key, subsystem));
        }
        catch
        {
            // The notice is cosmetic.
        }
    }
}
