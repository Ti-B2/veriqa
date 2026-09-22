// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// Offline IP geolocation result (SPEC-017 §5.2): approximate country and city,
/// without precise coordinates (ICC-006).
/// </summary>
/// <param name="Country">Country (localized name or ISO code; null — not determined).</param>
/// <param name="City">City (null — not determined).</param>
public sealed record GeoIpLocation(string? Country, string? City);

/// <summary>
/// Offline IP geolocation provider (SPEC-017, ICC-013):
/// no calls to external online services in the trust path and no sharing of the IP with third parties.
/// </summary>
public interface IGeoIpProvider
{
    /// <summary>
    /// Whether the provider is available (the local database was found and opened).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Resolves an approximate geolocation from an IP address.
    /// </summary>
    /// <param name="address">Initiator IP address.</param>
    /// <returns>Geolocation, or null if it could not be resolved.</returns>
    GeoIpLocation? Lookup(IPAddress address);
}
