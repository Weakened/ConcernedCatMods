using System.Collections.Generic;
using BepInEx.Logging;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Keeps the last few dozen lines Teamster itself logged this
/// session, for the Support Bundle's "recent log lines" section (CT-039).
/// Subscribes only to Teamster's own <see cref="ManualLogSource"/> — no
/// other mod's or Valheim's own log lines are ever captured. Unlike
/// Concerned Cartographer's crash reporter (which deliberately never
/// includes log content in an automatically-sent report — see that
/// product's PRIVACY.md), a support bundle here is generated and reviewed
/// by the player before they share it anywhere, so recent log context is
/// exactly the point rather than a risk to exclude; every captured line
/// still passes through <see cref="Domain.Support.SupportBundleSanitizer"/>
/// before export, since a log line written for a developer's eyes can
/// embed a full path.</summary>
internal sealed class LogTailRecorder
{
    private const int MaxLines = 100;

    private readonly Queue<string> _lines = new();
    private readonly object _gate = new();
    private ManualLogSource? _attached;

    public void Attach(ManualLogSource log)
    {
        _attached = log;
        log.LogEvent += HandleLogEvent;
    }

    public void Detach()
    {
        if (_attached is null)
        {
            return;
        }

        _attached.LogEvent -= HandleLogEvent;
        _attached = null;
    }

    /// <summary>A snapshot of captured lines, oldest first. Safe to call
    /// from any thread; never throws.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return new List<string>(_lines);
        }
    }

    private void HandleLogEvent(object sender, LogEventArgs args)
    {
        try
        {
            string line = "[" + args.Level + "] " + (args.Data?.ToString() ?? "");
            lock (_gate)
            {
                _lines.Enqueue(line);
                while (_lines.Count > MaxLines)
                {
                    _lines.Dequeue();
                }
            }
        }
        catch
        {
            // Recording recent lines must never harm the game.
        }
    }
}
