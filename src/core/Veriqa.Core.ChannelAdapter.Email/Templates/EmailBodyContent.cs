// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Templates;

/// <summary>
/// The ready email body returned by <see cref="IEmailMessageComposer"/> (SPEC-016 §4.3).
/// </summary>
/// <param name="Subject">Mail subject; also used as the HTML document title.</param>
/// <param name="HtmlBody">HTML part of the mail.</param>
/// <param name="TextBody">Plain-text part of the mail (fallback for clients without HTML).</param>
/// <param name="EmbedsQrImage">The rendered body referenced the QR image by Content-Id. The
/// delivery provider generates and attaches the
/// PNG only when this is <c>true</c> — otherwise the mail would carry an attachment nothing points at.
/// Which form of the QR a mail carries is therefore decided by the text that was rendered, never by a
/// setting: a variant naming the Content-Id slot gets the attachment, one naming a URL slot instead
/// gets none, and one naming neither is the mail without a QR.</param>
public sealed record EmailBodyContent(
    string Subject,
    string HtmlBody,
    string TextBody,
    bool EmbedsQrImage);
