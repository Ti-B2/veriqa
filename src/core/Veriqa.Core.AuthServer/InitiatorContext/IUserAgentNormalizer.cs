// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// User-Agent normalization result (SPEC-017 §5.2, ICC-012).
/// </summary>
/// <param name="Browser">Normalized browser name (null — not determined).</param>
/// <param name="OsPlatform">Normalized OS/platform (null — not determined).</param>
/// <param name="DeviceType">Device type: desktop / mobile / tablet / unknown.</param>
public sealed record UserAgentInfo(string? Browser, string? OsPlatform, string DeviceType);

/// <summary>
/// Deterministic normalization of the User-Agent into browser / platform / device type
/// (SPEC-017, ICC-012). The UA string is not trusted as a reliable fact (ICC-002).
/// </summary>
public interface IUserAgentNormalizer
{
    /// <summary>
    /// Normalizes the User-Agent string.
    /// </summary>
    /// <param name="userAgent">Raw User-Agent string.</param>
    /// <returns>Normalized data, or null if the string is empty/unusable.</returns>
    UserAgentInfo? Normalize(string? userAgent);
}
