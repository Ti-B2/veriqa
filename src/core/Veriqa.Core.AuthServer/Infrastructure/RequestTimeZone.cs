// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// The single check of a time zone a caller states — the entries that accept one ask here whether
/// the platform can resolve it into a zone at all.
/// </summary>
/// <remarks>
/// The rule is the platform's own lookup and nothing else: no list of zone identifiers is kept here
/// (core-rules section 3). What the check answers is "this deployment can convert a moment into that
/// zone", which is the only property either entry needs — a zone the platform does not carry data
/// for would silently leave every moment in UTC.
/// </remarks>
internal static class RequestTimeZone
{
    /// <summary>
    /// Reports whether the value names a time zone this deployment can resolve.
    /// </summary>
    /// <param name="timeZoneId">Raw time zone identifier (IANA, for example <c>Europe/Berlin</c>).</param>
    /// <returns><see langword="true"/> when the platform resolves the identifier.</returns>
    public static bool IsResolvable(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);
}
