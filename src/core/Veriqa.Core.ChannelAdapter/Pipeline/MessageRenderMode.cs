// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Output mode of <see cref="MessageTextRenderer"/>: how the resolved template and substituted values
/// are treated with respect to HTML markup.
/// </summary>
public enum MessageRenderMode
{
    /// <summary>
    /// Plain text (messenger prompt, plain-text mail body): values are sanitized only.
    /// </summary>
    PlainText = 0,

    /// <summary>
    /// HTML body: the resolved template and every substituted value are HTML-encoded so a translation or
    /// a value can never break the markup (core-rules §10).
    /// </summary>
    Html = 1
}
