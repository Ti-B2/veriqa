// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Email.Domain;

/// <summary>
/// Message for sending a magic link email in Pull mode (SPEC-016 §4.3).
/// </summary>
/// <param name="ToAddress">Recipient's email address.</param>
/// <param name="ClientName">Client application name to display in the email.</param>
/// <param name="ActionToken">One-time action token for building the magic link.</param>
/// <param name="ExpiresAt">Token expiration moment (UTC).</param>
/// <param name="PublicBaseUrl">Public base URL for generating the magic link.</param>
/// <param name="Locale">Recipient locale for localizing the email: the IETF language tag of the
/// request that started the Pull flow (the mail is sent before confirmation, so no channel-side
/// locale exists yet). Null — the base language (English).</param>
public sealed record EmailLoginMessage(
    string ToAddress,
    string ClientName,
    string ActionToken,
    DateTimeOffset ExpiresAt,
    string PublicBaseUrl,
    string? Locale = null)
{
    /// <summary>
    /// Ownership levels of the transaction the mail is sent for (SPEC-036 TPL-116): the wording of the
    /// mail is a levelled setting like any other, so a text an application or a <c>ui_config</c> record
    /// declares reaches the recipient only when the send states the levels it was addressed to.
    /// <para>
    /// Required rather than nullable: "no levels" is a value of the type
    /// (<see cref="ResolutionContext.Core"/>, the self-hosted context), so a send cannot quietly leave
    /// the wording to the core level.
    /// </para>
    /// </summary>
    public required ResolutionContext Ownership { get; init; }

    /// <summary>
    /// Initiator context of the transaction (SPEC-017, delta of SPEC-016 §9.3 — informative, it
    /// complements but does not replace sender verification); null — the details were not captured.
    /// <para>
    /// The CONTEXT travels here, not a line rendered from it: the mail is a confirmation text of its
    /// own, so its template declares the initiator slots and words them itself. A context without
    /// details simply leaves those slots unfilled, and the ladder of the mail degrades onto the variant
    /// that names none of them.
    /// </para>
    /// </summary>
    public ConfirmationPromptContext? InitiatorContext { get; init; }

    /// <summary>
    /// Time zone the moments of the mail are shown in (an IANA identifier, for example
    /// <c>Europe/Berlin</c>) — the zone stated for the transaction that started the Pull flow. Null —
    /// the zone is unknown and the expiry is shown in UTC with the marker that says so.
    /// <para>
    /// It travels beside <see cref="Locale"/> and never apart from it: the two are one answer to how
    /// the recipient reads the expiry of the link — the conventions of the date and the zone it is
    /// stated in.
    /// </para>
    /// </summary>
    public string? TimeZone { get; init; }
}
