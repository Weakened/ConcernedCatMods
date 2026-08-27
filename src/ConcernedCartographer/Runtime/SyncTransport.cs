using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>Peer-to-peer share transport over Valheim's routed RPCs.
/// Envelopes are compressed codec rows; received shares only ever land in
/// the review inbox — nothing auto-applies. Hard caps bound hostile or
/// corrupt envelopes, and every row passes through the malformed-skipping
/// codecs.</summary>
internal sealed class SyncTransport
{
    private const string RpcName = "CC_AtlasShare";
    private const int ProtocolVersion = 1;
    private const int MaxCompressedBytes = 320_000;
    private const int MaxDecompressedBytes = 4_000_000;
    private const int MaxRows = 20_000;

    private readonly ManualLogSource _log;
    private readonly SyncInbox _inbox;
    private bool _registered;
    private bool _disabledForSession;

    /// <summary>Our own author id, used to drop self-echoes of the
    /// Everybody broadcast.</summary>
    public string LocalAuthorId { get; set; } = "";

    public SyncTransport(ManualLogSource log, SyncInbox inbox)
    {
        _log = log;
        _inbox = inbox;
    }

    /// <summary>Idempotent RPC registration once the routed-RPC system is
    /// alive.</summary>
    public void EnsureRegistered()
    {
        if (_registered || _disabledForSession || ZRoutedRpc.instance == null)
        {
            return;
        }

        try
        {
            ZRoutedRpc.instance.Register<ZPackage>(RpcName, OnShareReceived);
            _registered = true;
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    /// <summary>Broadcasts the local shared entities to every peer.</summary>
    public bool Share(string authorId, string authorName, IReadOnlyList<AtlasPin> pins, IReadOnlyList<AtlasRoute> routes, out string message)
    {
        message = "";
        if (_disabledForSession || !_registered || ZRoutedRpc.instance == null)
        {
            message = "Sync transport is unavailable this session.";
            return false;
        }

        try
        {
            var payload = new StringBuilder();
            payload.AppendLine("PINS");
            foreach (AtlasPin pin in pins)
            {
                payload.AppendLine(PinCodec.SerializeRow(pin));
            }

            payload.AppendLine("ROUTES");
            foreach (AtlasRoute route in routes)
            {
                foreach (string line in RouteCodec.SerializeRoute(route))
                {
                    payload.AppendLine(line);
                }
            }

            byte[] compressed = Utils.Compress(Encoding.UTF8.GetBytes(payload.ToString()));
            if (compressed.Length > MaxCompressedBytes)
            {
                message = $"The shared atlas is too large to broadcast ({compressed.Length / 1024} KB compressed). " +
                    "Reduce the shared scope or archive old shared entities.";
                return false;
            }

            var package = new ZPackage();
            package.Write(ProtocolVersion);
            package.Write(authorId);
            package.Write(authorName);
            package.Write(compressed.Length);
            package.Write(compressed);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, package);
            message = $"Shared {pins.Count} pin(s) and {routes.Count} route(s) to all connected players.";
            return true;
        }
        catch (Exception exception)
        {
            Disable(exception);
            message = "Sharing failed; see the log.";
            return false;
        }
    }

    private void OnShareReceived(long sender, ZPackage package)
    {
        if (_disabledForSession)
        {
            return;
        }

        try
        {
            int version = package.ReadInt();
            if (version != ProtocolVersion)
            {
                _log.LogWarning($"Ignored an atlas share with protocol version {version} (mine is {ProtocolVersion}).");
                return;
            }

            string authorId = package.ReadString();
            string authorName = package.ReadString();
            if (LocalAuthorId.Length > 0 && string.Equals(authorId, LocalAuthorId, StringComparison.Ordinal))
            {
                return;
            }
            int length = package.ReadInt();
            if (length <= 0 || length > MaxCompressedBytes)
            {
                _log.LogWarning("Ignored an oversized or empty atlas share.");
                return;
            }

            byte[] compressed = package.ReadByteArray();
            byte[] raw = Utils.Decompress(compressed);
            if (raw.Length > MaxDecompressedBytes)
            {
                _log.LogWarning("Ignored an atlas share that decompressed beyond the safety cap.");
                return;
            }

            string text = Encoding.UTF8.GetString(raw);
            string[] lines = text.Split('\n');
            if (lines.Length > MaxRows)
            {
                _log.LogWarning("Ignored an atlas share with too many rows.");
                return;
            }

            var pinLines = new List<string>();
            var routeLines = new List<string>();
            List<string>? current = null;
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');
                if (line == "PINS")
                {
                    current = pinLines;
                }
                else if (line == "ROUTES")
                {
                    current = routeLines;
                }
                else if (current is not null && line.Length > 0)
                {
                    current.Add(line);
                }
            }

            PinCodec.ParseResult pins = PinCodec.Parse(pinLines);
            RouteCodec.ParseResult routes = RouteCodec.Parse(routeLines);

            // Ignore self-echoes of our own broadcast.
            if (_inbox is null || (pins.Pins.Count == 0 && routes.Routes.Count == 0))
            {
                return;
            }

            _inbox.Add(new SyncInbox.Envelope(
                authorId,
                string.IsNullOrEmpty(authorName) ? "Unknown Viking" : authorName,
                pins.Pins,
                routes.Routes,
                DateTime.UtcNow));
            Player.m_localPlayer?.Message(
                MessageHud.MessageType.TopLeft,
                AtlasStrings.Format("hud.syncReceived", authorName));
            _log.LogInfo($"Atlas share received from {authorName} ({pins.Pins.Count} pin(s), {routes.Routes.Count} route(s), " +
                $"{pins.MalformedRows + routes.MalformedRows} malformed row(s) skipped).");
        }
        catch (Exception exception)
        {
            // A hostile envelope must never take the transport down silently
            // forever; log and keep listening, but disable after repeats.
            _log.LogWarning($"Failed to read an atlas share: {exception.Message}");
        }
    }

    private void Disable(Exception exception)
    {
        _disabledForSession = true;
        _log.LogError($"Sync transport failed and was disabled for this session: {exception}");
    }
}
