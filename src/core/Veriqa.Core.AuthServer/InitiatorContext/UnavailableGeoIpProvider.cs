// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// The shipped default of <see cref="IGeoIpProvider"/>: a null object that resolves nothing.
/// The auth server carries no GeoIP library of its own — an actual lookup arrives with the
/// Veriqa.Core.AuthServer.MaxMind satellite package, which reads a local .mmdb database the
/// deployment supplies.
/// </summary>
/// <remarks>
/// Reporting itself unavailable is the same state an installation without a configured database
/// was already in, so the behaviour of a deployment that never wired GeoIP does not change: geo
/// fields stay null (graceful, ICC-082) and the startup diagnostics warn once when geolocation is
/// switched on without a provider behind it.
/// </remarks>
internal sealed class UnavailableGeoIpProvider : IGeoIpProvider
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public GeoIpLocation? Lookup(IPAddress address) => null;
}
