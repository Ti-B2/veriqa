// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Names of the dimensions a setting of this product is cut by (SPEC-012 CFG-237/CFG-239) — the ONE
/// place each of them is spelled.
/// <para>
/// A dimension name is a literal that travels: it stands in the declaration of a key, in the address
/// of a level (<c>{channel}</c>), in the point of the chain a consumer asks with, and in the map a
/// level states its per-dimension values in. A second spelling of the same name would not fail — it
/// would simply never match, and the setting would answer from the step that addresses nothing, which
/// looks exactly like a deployment that stated no value.
/// </para>
/// <para>
/// It lives in the mechanism assembly for the same reason the Logging axis does
/// (<see cref="LoggingConfigKeys"/>): the owners that spell the name are in contours that do not
/// depend on one another — the auth server, which cuts the sign-in page settings by channel, and the
/// channel contour together with the journal, which ask about a channel by its type. This assembly is
/// the only floor all of them already stand on.
/// </para>
/// </summary>
public static class ConfigDimensionNames
{
    /// <summary>
    /// Dimension that cuts a setting addressed BY CHANNEL. Its values are channel types
    /// (<c>telegram</c>, <c>whatsapp</c>, a third-party SPI channel type) — the core has no closed
    /// list of them, which is why a channel is asked about by its type rather than enumerated.
    /// </summary>
    public const string Channel = "channel";
}
