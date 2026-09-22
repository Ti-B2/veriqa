// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// SMTP settings for outbound email delivery (SPEC-016 §7.1).
/// </summary>
public sealed class EmailSmtpOptions
{
    /// <summary>
    /// The SMTP server host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// The SMTP server port. Default — 587 (STARTTLS).
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Enable TLS when connecting to the SMTP server.
    /// true — implicit TLS (port 465); false — STARTTLS or a plain connection (port 587/25),
    /// as decided by <see cref="RequireStartTls"/>.
    /// Disabled by default (port 587, STARTTLS).
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Require STARTTLS before authenticating when <see cref="UseSsl"/> is <see langword="false"/>.
    /// Enabled by default: a connection that carries credentials refuses to proceed when the server
    /// does not offer STARTTLS, so the password is never sent over a plain channel.
    /// <see langword="false"/> — STARTTLS is used only when the server advertises it, otherwise the
    /// connection, AUTH included, stays plain; meant for an internal relay reached without TLS on purpose.
    /// Has no effect on an anonymous connection (no credentials) or with <see cref="UseSsl"/> enabled.
    /// </summary>
    public bool RequireStartTls { get; set; } = true;

    /// <summary>
    /// The username for authentication with the SMTP server.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The password for authentication with the SMTP server.
    /// Must be stored via the standard secrets mechanism from SPEC-012 (EM-052).
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Tells whether these settings authenticate at all: a user name and a password are both present.
    /// </summary>
    /// <remarks>
    /// Lives on the options rather than inside either consumer because both have to answer it the same
    /// way: the sender decides from it whether to send AUTH, and the options validator refuses a
    /// configuration that is neither this nor <see cref="IsAnonymous"/>. Two separate spellings of this
    /// one condition could drift apart — the validator accepting credentials the sender then does not
    /// send — so the answer has a single home.
    /// </remarks>
    /// <returns><see langword="true"/> when the connection is to be authenticated.</returns>
    public bool RequiresAuthentication() =>
        !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);

    /// <summary>
    /// Tells whether these settings deliberately carry no credentials — an internal relay that needs no
    /// authentication, the typical on-premise setup.
    /// </summary>
    /// <returns><see langword="true"/> when neither a user name nor a password is set.</returns>
    public bool IsAnonymous() =>
        string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password);
}
