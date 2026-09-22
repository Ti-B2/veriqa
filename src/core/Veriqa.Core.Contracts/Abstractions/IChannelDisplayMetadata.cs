// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Optional display metadata of a channel in the sign-in window (public SPI, additive).
/// A third-party adapter implements it <i>in addition</i> to <see cref="IChannelAdapter"/> to give
/// its channel a human-readable button label and a brand glyph. Without it the channel still
/// renders: the button falls back to the raw <see cref="IChannelAdapter.ChannelType"/> and no icon.
/// </summary>
public interface IChannelDisplayMetadata
{
    /// <summary>
    /// Channel name shown on the sign-in button. A single non-localizable string — a brand name
    /// (like "Telegram") is not translated. Localizable CTA phrasing for third-party channels
    /// is out of scope of this SPI version. A blank value — or one longer than
    /// <see cref="Veriqa.Core.ChannelAdapter.Constants.CustomChannelContract.MaxDisplayNameLength"/>,
    /// which would break the sign-in window layout — degrades to the raw
    /// <see cref="IChannelAdapter.ChannelType"/>:
    /// the button is never rendered nameless.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Inner markup of the channel glyph inside a <c>24×24</c> viewBox (for example
    /// <c>&lt;path d="…"/&gt;</c>), or null for a channel without an icon.
    /// The value is embedded into the sign-in page, so it must be static markup owned by the
    /// adapter's author — never user input or content fetched at runtime. It is checked against an
    /// allowlist before rendering — plain shape elements only
    /// (<c>path</c>, <c>circle</c>, <c>ellipse</c>, <c>rect</c>, <c>line</c>, <c>polyline</c>,
    /// <c>polygon</c>, <c>g</c>) with double-quoted attributes, no <c>style</c> and no
    /// <c>on*</c> handlers — and it must not exceed
    /// <see cref="Veriqa.Core.ChannelAdapter.Constants.CustomChannelContract.MaxIconSvgPathLength"/>;
    /// markup outside it is dropped with a warning and the channel renders without an icon.
    /// </summary>
    string? IconSvgPath => null;
}
