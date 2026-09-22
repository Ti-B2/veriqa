// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.TransactionEngine.Configuration;

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// Offline GeoIP provider based on MaxMind.GeoIP2 and a local .mmdb database (ICC-013).
/// The database (GeoLite2/GeoIP2 City or Country) is supplied at deployment; the path is set
/// via the Veriqa:InitiatorContext:GeoIp:DatabasePath configuration. If the database is not
/// configured or failed to open — the provider is unavailable and geo fields stay null
/// (graceful, ICC-082).
/// </summary>
/// <remarks>
/// Lives in this satellite package, not in the auth server itself: the database it reads is not
/// shipped with the product (GeoLite2 needs a MaxMind account and their EULA), so an installation
/// that never configures a path would otherwise carry a library it can never use.
/// </remarks>
internal sealed class MaxMindGeoIpProvider : IGeoIpProvider, IDisposable
{
    /// <summary>
    /// Local database reader (null — the provider is unavailable).
    /// Thread-safe; reused across all requests (MaxMind recommendation).
    /// </summary>
    private readonly DatabaseReader? _reader;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<MaxMindGeoIpProvider> _logger;

    /// <summary>
    /// Creates the provider: opens the local database if it is configured and exists.
    /// </summary>
    /// <param name="options">Initiator context check settings.</param>
    /// <param name="logger">Logger.</param>
    public MaxMindGeoIpProvider(
        IOptions<InitiatorContextOptions> options,
        ILogger<MaxMindGeoIpProvider> logger)
    {
        _logger = logger;

        var geoIpOptions = options.Value.GeoIp;

        // Only the offline provider is supported (ICC-013)
        if (!string.Equals(geoIpOptions.Provider, InitiatorGeoIpOptions.OfflineDatabaseProvider, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(geoIpOptions.DatabasePath))
        {
            return;
        }

        try
        {
            _reader = new DatabaseReader(geoIpOptions.DatabasePath);
        }
        catch (Exception ex)
        {
            // Database unavailability is not critical: geo fields will stay null (ICC-082)
            _logger.LogWarning(
                ex,
                "GeoIP database is not open, geolocation is unavailable. Code: {Code}, DatabasePath: {DatabasePath}",
                InitiatorContextResultCodes.GeoIpUnavailable,
                geoIpOptions.DatabasePath);
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => _reader is not null;

    /// <inheritdoc />
    public GeoIpLocation? Lookup(IPAddress address)
    {
        // The method resolves country/city from the local database; any errors — null (best-effort)
        if (_reader is null)
        {
            return null;
        }

        try
        {
            // A City database provides both country and city
            if (_reader.TryCity(address, out var cityResponse) && cityResponse is not null)
            {
                var country = cityResponse.Country?.Name ?? cityResponse.Country?.IsoCode;
                var city = cityResponse.City?.Name;

                return country is null && city is null ? null : new GeoIpLocation(country, city);
            }
        }
        catch (InvalidOperationException)
        {
            // The database is not of the City type (e.g. Country) — try a country-only lookup
            try
            {
                if (_reader.TryCountry(address, out var countryResponse) && countryResponse is not null)
                {
                    var country = countryResponse.Country?.Name ?? countryResponse.Country?.IsoCode;
                    return country is null ? null : new GeoIpLocation(country, null);
                }
            }
            catch (Exception ex)
            {
                LogLookupFailure(ex);
            }
        }
        catch (Exception ex)
        {
            LogLookupFailure(ex);
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _reader?.Dispose();
    }

    /// <summary>
    /// Logs a database lookup failure at the Debug level (the IP is not written to the log — ICC-060).
    /// </summary>
    /// <param name="ex">Lookup exception.</param>
    private void LogLookupFailure(Exception ex)
    {
        _logger.LogDebug(ex, "GeoIP lookup failure. Code: {Code}", InitiatorContextResultCodes.GeoIpUnavailable);
    }
}
