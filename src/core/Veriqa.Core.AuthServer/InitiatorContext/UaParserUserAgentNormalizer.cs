// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using UAParser;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// User-Agent normalization based on UAParser — the C# port of the official ua-parser (ICC-012).
/// The parser is deterministic; the device type is derived from the recognized OS/device families
/// (ua-parser has no ready desktop/mobile/tablet classification — a deterministic
/// family mapping is applied).
/// </summary>
internal sealed class UaParserUserAgentNormalizer : IUserAgentNormalizer
{
    /// <summary>
    /// The "undetermined" value in ua-parser families.
    /// </summary>
    private const string UnknownFamily = "Other";

    /// <summary>
    /// Mobile device OS families (immutable set, case-insensitive comparison).
    /// </summary>
    private static readonly FrozenSet<string> MobileOsFamilies = new[]
    {
        "Android",
        "iOS",
        "Windows Phone",
        "BlackBerry OS",
        "KaiOS"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Desktop device OS families (immutable set, case-insensitive comparison).
    /// </summary>
    private static readonly FrozenSet<string> DesktopOsFamilies = new[]
    {
        "Windows",
        "Mac OS X",
        "Linux",
        "Ubuntu",
        "Fedora",
        "Chrome OS",
        "FreeBSD"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tablet markers in the ua-parser device family.
    /// </summary>
    private static readonly IReadOnlyList<string> TabletDeviceMarkers = new[] { "iPad", "Tablet", "Kindle" }.AsReadOnly();

    /// <summary>
    /// Shared parser instance with the built-in uap-core regular expressions.
    /// Thread-safe after creation.
    /// </summary>
    private static readonly Parser Parser = Parser.GetDefault();

    /// <inheritdoc />
    public UserAgentInfo? Normalize(string? userAgent)
    {
        // The method normalizes the UA into browser/OS/device type; an empty string — no data
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var clientInfo = Parser.Parse(userAgent);

        var browser = NormalizeFamily(clientInfo.UA.Family);
        var osPlatform = NormalizeFamily(clientInfo.OS.Family);
        var deviceType = ResolveDeviceType(clientInfo);

        // If nothing could be recognized — there is no data
        if (browser is null && osPlatform is null && deviceType is InitiatorContextFields.DeviceTypes.Unknown)
        {
            return null;
        }

        return new UserAgentInfo(browser, osPlatform, deviceType);
    }

    /// <summary>
    /// Reduces a ua-parser family to the field value: "Other" is treated as undetermined.
    /// </summary>
    /// <param name="family">The family from ua-parser.</param>
    /// <returns>The family name or null.</returns>
    private static string? NormalizeFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family) || string.Equals(family, UnknownFamily, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return family;
    }

    /// <summary>
    /// Determines the device type from the recognized device and OS families.
    /// </summary>
    /// <param name="clientInfo">The UA parse result.</param>
    /// <returns>The device type constant (ICC-014).</returns>
    private static string ResolveDeviceType(ClientInfo clientInfo)
    {
        var deviceFamily = clientInfo.Device.Family ?? string.Empty;

        // Tablets are recognized by the device family
        foreach (var marker in TabletDeviceMarkers)
        {
            if (deviceFamily.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return InitiatorContextFields.DeviceTypes.Tablet;
            }
        }

        var osFamily = clientInfo.OS.Family ?? string.Empty;

        if (MobileOsFamilies.Contains(osFamily))
        {
            return InitiatorContextFields.DeviceTypes.Mobile;
        }

        if (DesktopOsFamilies.Contains(osFamily))
        {
            return InitiatorContextFields.DeviceTypes.Desktop;
        }

        return InitiatorContextFields.DeviceTypes.Unknown;
    }
}
