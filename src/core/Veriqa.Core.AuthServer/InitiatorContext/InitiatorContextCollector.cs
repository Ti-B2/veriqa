// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// Initiator context collector (SPEC-017 §5): IP accounting for ForwardedHeaders (ICC-020),
/// normalized User-Agent (ICC-012), offline geolocation (ICC-013), client
/// application name from the OIDC clients configuration (ClientId → DisplayName).
/// All fields except the required ones are best-effort (ICC-011): unavailability does not block
/// transaction creation.
/// </summary>
internal sealed class InitiatorContextCollector : IInitiatorContextCollector
{
    /// <summary>
    /// Context collector version (semver, snapshot CollectorVersion field).
    /// </summary>
    private const string CollectorVersion = "1.0.0";

    /// <summary>
    /// Length limit of the raw User-Agent in RawMetadata (ICC-031 recommendation — 2 KB).
    /// </summary>
    private const int MaxRawUserAgentLength = 2048;

    /// <summary>
    /// Name of the RawMetadata field with the truncated raw User-Agent.
    /// </summary>
    private const string RawUserAgentMetadataField = "raw_user_agent";

    /// <summary>
    /// Name of the RawMetadata field with the untrusted IP source flag (ICC-021).
    /// </summary>
    private const string IpUntrustedMetadataField = "ip_untrusted_forwarding";

    /// <summary>
    /// Initiator context check settings.
    /// </summary>
    private readonly IOptions<InitiatorContextOptions> _options;

    /// <summary>
    /// Guarded entry point to the OIDC clients snapshot (source of DisplayName).
    /// </summary>
    private readonly OidcClientsOptionsAccessor _clients;

    /// <summary>
    /// ForwardedHeaders settings — for assessing IP trustworthiness (ICC-021).
    /// </summary>
    private readonly IOptions<ForwardedHeadersOptions> _forwardedHeadersOptions;

    /// <summary>
    /// User-Agent normalizer.
    /// </summary>
    private readonly IUserAgentNormalizer _userAgentNormalizer;

    /// <summary>
    /// Offline GeoIP provider.
    /// </summary>
    private readonly IGeoIpProvider _geoIpProvider;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<InitiatorContextCollector> _logger;

    /// <summary>
    /// Clock the snapshot capture moment is taken from.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the initiator context collector.
    /// </summary>
    /// <param name="options">Initiator context check settings.</param>
    /// <param name="clients">Guarded entry point to the OIDC clients snapshot.</param>
    /// <param name="forwardedHeadersOptions">ForwardedHeaders settings.</param>
    /// <param name="userAgentNormalizer">User-Agent normalizer.</param>
    /// <param name="geoIpProvider">Offline GeoIP provider.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time provider.</param>
    public InitiatorContextCollector(
        IOptions<InitiatorContextOptions> options,
        OidcClientsOptionsAccessor clients,
        IOptions<ForwardedHeadersOptions> forwardedHeadersOptions,
        IUserAgentNormalizer userAgentNormalizer,
        IGeoIpProvider geoIpProvider,
        ILogger<InitiatorContextCollector> logger,
        TimeProvider timeProvider)
    {
        _options = options;
        _clients = clients;
        _forwardedHeadersOptions = forwardedHeadersOptions;
        _userAgentNormalizer = userAgentNormalizer;
        _geoIpProvider = geoIpProvider;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public InitiatorContextSnapshot? Collect(HttpContext httpContext, string? clientId)
    {
        // The method builds the context snapshot: required fields + best-effort IP/UA/geo.
        // Any collection error does not block transaction creation (ICC-011).

        var options = _options.Value;

        // Collection is fully disabled — the snapshot is not built (ICC-033)
        if (!options.Enabled)
        {
            return null;
        }

        try
        {
            // The client application name is required (ICC-010): ClientId → DisplayName,
            // fallback — the ClientId itself; without a client identifier the context is not built
            var applicationName = ResolveClientApplicationName(clientId);
            if (applicationName is null)
            {
                _logger.LogDebug("Initiator context not built: ClientId is missing");
                return null;
            }

            var now = _timeProvider.GetUtcNow();

            var (remoteIp, ipTrusted) = CollectIpAddress(httpContext, options);
            var ipAddress = remoteIp?.ToString();
            var userAgentInfo = CollectUserAgent(httpContext, options, out var rawUserAgent);
            var geoLocation = CollectGeoLocation(options, remoteIp);

            var rawMetadata = BuildRawMetadata(options, rawUserAgent, ipAddress, ipTrusted);

            return new InitiatorContextSnapshot
            {
                ClientApplicationName = applicationName,
                InitiatedAt = now,
                IpAddress = ipAddress,
                IpTrusted = ipTrusted,
                Browser = userAgentInfo?.Browser,
                OsPlatform = userAgentInfo?.OsPlatform,
                DeviceType = userAgentInfo?.DeviceType,
                GeoCountry = geoLocation?.Country,
                GeoCity = geoLocation?.City,
                RawMetadata = rawMetadata,
                CapturedAt = now,
                CollectorVersion = CollectorVersion
            };
        }
        catch (Exception ex)
        {
            // Context collection never causes a transaction creation error (ICC-011)
            _logger.LogWarning(ex, "Failed to collect the initiator context — the transaction is created without context");
            return null;
        }
    }

    /// <summary>
    /// Resolves the client application name: DisplayName from the client configuration,
    /// fallback — the ClientId itself (SPEC-017 §5.2, field No. 2).
    /// </summary>
    /// <param name="clientId">OIDC client identifier.</param>
    /// <returns>Application name, or null if the ClientId is missing.</returns>
    private string? ResolveClientApplicationName(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        // Through the guarded accessor, not IOptions<OidcClientsOptions>: IOptions binds once, at its first
        // read (the client seeder makes it at host start), and never re-binds, so a client renamed or
        // added after start would keep answering with the
        // stale DisplayName here while the rest of the request reads the live section. The accessor
        // serves the live value, and a broken section degrades this best-effort field to the ClientId
        // (ICC-010) instead of reaching the catch that Collect wraps this call in.
        var clientConfig = _clients.Current.Clients
            .FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(clientConfig?.DisplayName)
            ? clientId
            : clientConfig.DisplayName;
    }

    /// <summary>
    /// Collects the initiator IP (best-effort): RemoteIpAddress already accounts for
    /// ForwardedHeadersMiddleware (ICC-020). The trust flag reflects the presence of
    /// configured KnownProxies / KnownIPNetworks (ICC-021) — that is, the deployment
    /// configuration, not the fact that this particular request passed through a known proxy
    /// (per-request proxy-route validation is not performed — the standard
    /// ForwardedHeadersMiddleware is reused, without parallel header processing).
    /// </summary>
    /// <param name="httpContext">HTTP context.</param>
    /// <param name="options">Collection settings.</param>
    /// <returns>IP address (or null) and the trust flag.</returns>
    private (IPAddress? RemoteIp, bool IpTrusted) CollectIpAddress(HttpContext httpContext, InitiatorContextOptions options)
    {
        if (!options.CollectIpAddress)
        {
            return (null, false);
        }

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            // A reliable IP cannot be determined — the fields remain null (ICC-022)
            return (null, false);
        }

        // With an open ForwardedHeaders configuration (KnownProxies/KnownIPNetworks cleared)
        // the IP from X-Forwarded-For is not considered reliable (ICC-021). A configuration-level flag:
        // it does not mean this particular request actually passed through a known proxy
        // (see the method's XML doc and InitiatorContextSnapshot.IpTrusted)
        var forwardedHeaders = _forwardedHeadersOptions.Value;
        var ipTrusted = forwardedHeaders.KnownProxies.Count > 0 || forwardedHeaders.KnownIPNetworks.Count > 0;

        if (!ipTrusted)
        {
            // We record the untrusted state with a diagnostic code; the IP itself is not logged (ICC-060)
            _logger.LogInformation(
                "Initiator IP obtained with an open ForwardedHeaders configuration and marked as untrusted. Code: {Code}",
                InitiatorContextResultCodes.IpUntrusted);
        }

        return (remoteIp, ipTrusted);
    }

    /// <summary>
    /// Collects and normalizes the User-Agent (best-effort, ICC-012).
    /// </summary>
    /// <param name="httpContext">HTTP context.</param>
    /// <param name="options">Collection settings.</param>
    /// <param name="rawUserAgent">Raw User-Agent string (for RawMetadata when StoreRawUserAgent is enabled).</param>
    /// <returns>Normalized UA data, or null.</returns>
    private UserAgentInfo? CollectUserAgent(HttpContext httpContext, InitiatorContextOptions options, out string? rawUserAgent)
    {
        rawUserAgent = null;

        if (!options.CollectUserAgent)
        {
            return null;
        }

        rawUserAgent = httpContext.Request.Headers.UserAgent.ToString();

        return _userAgentNormalizer.Normalize(rawUserAgent);
    }

    /// <summary>
    /// Determines the approximate geolocation from the IP (best-effort, ICC-013).
    /// Uses the same IPAddress written to the snapshot — geo and IP do not diverge.
    /// </summary>
    /// <param name="options">Collection settings.</param>
    /// <param name="remoteIp">The collected initiator IP.</param>
    /// <returns>Geolocation, or null.</returns>
    private GeoIpLocation? CollectGeoLocation(InitiatorContextOptions options, IPAddress? remoteIp)
    {
        if (!options.CollectGeoLocation || remoteIp is null || !_geoIpProvider.IsAvailable)
        {
            return null;
        }

        return _geoIpProvider.Lookup(remoteIp);
    }

    /// <summary>
    /// Builds RawMetadata: the truncated raw UA (if StoreRawUserAgent is enabled)
    /// and the untrusted IP source flag (ICC-021, ICC-031).
    /// </summary>
    /// <param name="options">Collection settings.</param>
    /// <param name="rawUserAgent">Raw User-Agent string.</param>
    /// <param name="ipAddress">The collected IP.</param>
    /// <param name="ipTrusted">IP trust flag.</param>
    /// <returns>JSON metadata element, or null.</returns>
    private JsonElement? BuildRawMetadata(
        InitiatorContextOptions options,
        string? rawUserAgent,
        string? ipAddress,
        bool ipTrusted)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (options.StoreRawUserAgent && !string.IsNullOrWhiteSpace(rawUserAgent))
        {
            if (rawUserAgent.Length > MaxRawUserAgentLength)
            {
                // The excess is truncated (ICC-031)
                _logger.LogWarning(
                    "Raw User-Agent exceeded the limit and was truncated. Code: {Code}, Limit: {Limit}",
                    InitiatorContextResultCodes.ContextTooLarge,
                    MaxRawUserAgentLength);

                rawUserAgent = rawUserAgent[..MaxRawUserAgentLength];
            }

            metadata[RawUserAgentMetadataField] = rawUserAgent;
        }

        if (ipAddress is not null && !ipTrusted)
        {
            metadata[IpUntrustedMetadataField] = true;
        }

        if (metadata.Count is 0)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(metadata);
    }
}
