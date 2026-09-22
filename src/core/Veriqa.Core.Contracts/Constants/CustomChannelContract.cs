// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Public SPI contract of a third-party channel registered through
/// <c>ChannelAdapterBuilder.AddChannel</c>: the <c>IChannelAdapter.ChannelType</c> format and the
/// caps on the optional <c>IChannelDisplayMetadata</c>. These constants live in the MIT contracts
/// assembly so an adapter built only against <c>Veriqa.Core.Contracts</c> can reference them and
/// self-check at compile time. The core registry and sign-in renderer enforce the very same values —
/// <c>CustomChannelConstants</c> aliases them, so there is a single source of truth.
/// </summary>
public static class CustomChannelContract
{
    /// <summary>
    /// Allowed <c>ChannelType</c> format of a third-party channel: a lowercase URL-safe token
    /// starting with a latin letter, up to 64 characters (<c>\A[a-z][a-z0-9-]{0,63}\z</c>).
    /// The token is matched ordinally against acr_values and is used verbatim as a URL path
    /// segment, so neither silent normalization nor uppercase is accepted. The anchors are
    /// deliberately <c>\A</c>/<c>\z</c> and not <c>^</c>/<c>$</c>: in .NET <c>$</c> also matches
    /// before a trailing newline, which would let a value read from configuration or a file
    /// ("acme-chat\n") through into a URL path segment and into the logs.
    /// </summary>
    public const string ChannelTypePattern = @"\A[a-z][a-z0-9-]{0,63}\z";

    /// <summary>
    /// Maximum length of the display label a third-party adapter may declare through
    /// <c>IChannelDisplayMetadata.DisplayName</c>. A label is a brand name on a tab and a CTA
    /// button, not a sentence: a longer value is dropped (the channel falls back to its type)
    /// instead of being rendered, so an adapter cannot blow up the sign-in window layout.
    /// </summary>
    public const int MaxDisplayNameLength = 32;

    /// <summary>
    /// Maximum length of the glyph markup a third-party adapter may declare through
    /// <c>IChannelDisplayMetadata.IconSvgPath</c>. Generous for a real 24×24 glyph, and it caps
    /// both the page weight and the cost of the allowlist scan that runs on every sign-in render.
    /// </summary>
    public const int MaxIconSvgPathLength = 4096;
}
