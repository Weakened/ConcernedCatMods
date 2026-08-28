using System;
using System.Globalization;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Parsed Sentry client DSN (#97). A DSN
/// (<c>https://&lt;publicKey&gt;@&lt;host&gt;/&lt;projectId&gt;</c>) is a
/// public event-ingestion key, not an account secret — but it is still
/// never committed to the repository; see CRASH_REPORTING.md. Sentry
/// auth tokens are never used anywhere in the mod.</summary>
internal sealed class SentryDsn
{
    private SentryDsn(string envelopeUrl, string authHeader)
    {
        EnvelopeUrl = envelopeUrl;
        AuthHeader = authHeader;
    }

    /// <summary>POST target: <c>https://host/api/&lt;project&gt;/envelope/</c>.</summary>
    public string EnvelopeUrl { get; }

    /// <summary>Value for the <c>X-Sentry-Auth</c> request header.</summary>
    public string AuthHeader { get; }

    public static bool TryParse(string? dsn, out SentryDsn? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(dsn) ||
            !Uri.TryCreate(dsn!.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") ||
            uri.UserInfo.Length == 0)
        {
            return false;
        }

        // Only the public key part is used; a legacy secret suffix is ignored.
        string publicKey = uri.UserInfo.Split(':')[0];
        string path = uri.AbsolutePath.TrimEnd('/');
        int lastSlash = path.LastIndexOf('/');
        string projectId = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        if (publicKey.Length == 0 || projectId.Length == 0 ||
            !long.TryParse(projectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        string pathPrefix = lastSlash > 0 ? path.Substring(0, lastSlash) : "";
        string authority = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        string envelopeUrl = $"{uri.Scheme}://{authority}{pathPrefix}/api/{projectId}/envelope/";
        string authHeader = $"Sentry sentry_version=7, sentry_client=cc-crash/1, sentry_key={publicKey}";
        parsed = new SentryDsn(envelopeUrl, authHeader);
        return true;
    }
}
