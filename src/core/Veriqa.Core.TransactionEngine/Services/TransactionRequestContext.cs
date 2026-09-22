// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.Services;

/// <summary>
/// Request context of a transaction created outside the OIDC flow (server-to-server confirmation
/// entry, SPEC-039 C19). It is a request parameter (value object), not a domain object, and it plays
/// for such a transaction the role <see cref="OidcContext"/> plays for an OIDC-initiated one:
/// the tenant/application attribution, the language of the request and the resolved
/// <c>ui_config</c> code, carried as one container rather than three independent fields.
/// <para>
/// Readers never reach for it directly — the single way to read the context of a transaction is
/// <see cref="TransactionContextExtensions"/>, which knows the precedence of the two containers.
/// </para>
/// </summary>
public sealed class TransactionRequestContext
{
    /// <summary>
    /// Tenant attribution of the request. Null — the tenant level is not stated.
    /// </summary>
    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    /// <summary>
    /// Application attribution of the request (the calling client's <c>client_id</c>).
    /// Null — the application level is not stated.
    /// </summary>
    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; init; }

    /// <summary>
    /// Language of the request (BCP 47 tag). Null — no language was stated.
    /// </summary>
    [JsonPropertyName("ui_locale")]
    public string? UiLocale { get; init; }

    /// <summary>
    /// Resolved <c>ui_config</c> record code (SPEC-002 section 4.6). Null — no record was selected.
    /// </summary>
    [JsonPropertyName("ui_config_code")]
    public string? UiConfigCode { get; init; }

    /// <summary>
    /// Time zone the moments of the request are shown in (IANA identifier). Null — no zone was
    /// stated, and a moment is shown in UTC with its marker.
    /// </summary>
    [JsonPropertyName("ui_time_zone")]
    public string? UiTimeZone { get; init; }
}
