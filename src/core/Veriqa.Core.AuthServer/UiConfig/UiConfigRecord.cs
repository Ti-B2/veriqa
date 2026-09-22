// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Veriqa.Core.AuthServer.Configuration.Enums;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.UiConfig;

/// <summary>
/// Versioned ui_config record (SPEC-012 CFG-203, SPEC-002 §4.6). JSONB-compatible DTO.
/// Replaces the static string allowlist with a dynamic tenant artifact.
///
/// Additive evolution discipline (R3):
/// — the only required field is <see cref="SchemaVersion"/>; all others are nullable/optional;
/// — defaults are applied AT RESOLUTION TIME (the renderer does feature detection), not stored in the record;
/// — enums are serialized as strings; an unknown enum value → null (fallback to the default);
/// — new fields are only added (optional); renaming/type change/required — forbidden;
/// — unknown fields are preserved round-trip via <see cref="Extra"/> (<c>[JsonExtensionData]</c>).
///
/// Explicit content exclusions (decisions #3/#6/#7/#8; TASK-046): the record does NOT carry LoginConfirmation.Mode
/// (login confirmation is a high-impact policy, the None lever, security CFG-049), Logging.Mode,
/// scopes/claims, RejectBots, rate-limit values, WelcomeMessage, InitiatorContext.DisplayFields/Enabled.
/// Since 2026-08-23 the custom SCRIPT of the sign-in page (path and its SRI hash) is excluded as well:
/// an executable script is the risky customization mode of SPEC-012 §4.3 and is stated by the owner of
/// the page (the global section or a client entry behind a gate), never by an artifact a request
/// selects. A legacy body still carrying those fields stays valid and round-trips through
/// <see cref="Extra"/> — it simply no longer puts a script on the page.
/// These settings are resolved via ownership levels, not through ui_config.
/// </summary>
public sealed class UiConfigRecord
{
    /// <summary>
    /// Record schema version (≥ 1 since the first version). The only required field.
    /// Absence in JSON is treated as v1 (forward-compat, §8 assumption #5);
    /// an explicit 0/negative makes the record invalid (see <see cref="IsSchemaVersionValid"/>).
    /// </summary>
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = SchemaVersionV1;

    // ── Visuals (matches the AuthPageDesignOptions fields) ───────────────────

    /// <summary>
    /// Design preset. Null — global default.
    /// </summary>
    [JsonPropertyName("preset")]
    [JsonConverter(typeof(TolerantNullableEnumConverter<DesignPreset>))]
    public DesignPreset? Preset { get; init; }

    /// <summary>
    /// Color scheme. Null — global default.
    /// </summary>
    [JsonPropertyName("theme")]
    [JsonConverter(typeof(TolerantNullableEnumConverter<AuthPageTheme>))]
    public AuthPageTheme? Theme { get; init; }

    /// <summary>
    /// Logo URL. Null — global default.
    /// </summary>
    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; init; }

    /// <summary>
    /// Primary color (#RRGGBB). Null — global default.
    /// </summary>
    [JsonPropertyName("primary_color")]
    public string? PrimaryColor { get; init; }

    /// <summary>
    /// Logo URLs the record states for a single theme, keyed by theme name (<c>light</c>,
    /// <c>dark</c>). A theme absent from the map takes <see cref="LogoUrl"/>. Null — the record
    /// states no themed logo.
    /// </summary>
    [JsonPropertyName("logo_url_by_theme")]
    public IReadOnlyDictionary<string, string>? LogoUrlByTheme { get; init; }

    /// <summary>
    /// Primary colors the record states for a single theme; see <see cref="LogoUrlByTheme"/> for the
    /// shape of the map. A theme absent from it takes <see cref="PrimaryColor"/>.
    /// </summary>
    [JsonPropertyName("primary_color_by_theme")]
    public IReadOnlyDictionary<string, string>? PrimaryColorByTheme { get; init; }

    /// <summary>
    /// Brand name shown in the brand row of the sign-in page. Null — the record states none and the
    /// name is taken from the level below. It is stated apart from <see cref="LogoUrl"/>: the two are
    /// resolved independently, so a record may state the name alone.
    /// </summary>
    [JsonPropertyName("brand_name")]
    public string? BrandName { get; init; }

    /// <summary>
    /// Path to custom CSS. Null — global default.
    /// </summary>
    [JsonPropertyName("custom_css_path")]
    public string? CustomCssPath { get; init; }

    /// <summary>
    /// CSS SRI hash of <see cref="CustomCssPath"/>. Null — the record pins nothing. The hash is
    /// resolved together with the path as one value, so a record that states one of them owns both.
    /// </summary>
    [JsonPropertyName("css_sri_hash")]
    public string? CssSriHash { get; init; }

    /// <summary>
    /// Path to the SignalR client. Null — global default.
    /// </summary>
    [JsonPropertyName("signalr_client_path")]
    public string? SignalRClientPath { get; init; }

    // ── Text (decisions #1, #8) ──────────────────────────────────────────────

    /// <summary>
    /// Authentication page title (decision #1). Null — global default (AuthPageStrings.Title).
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// On-page instruction (decision #1). Null — global default (AuthPageStrings.Instruction).
    /// </summary>
    [JsonPropertyName("instruction")]
    public string? Instruction { get; init; }

    // ── Behavior ─────────────────────────────────────────────────────────────

    /// <summary>
    /// List of channel codes (SPEC-002 §4.6). Null — global/delegated to the channel track.
    /// </summary>
    [JsonPropertyName("channels")]
    public IReadOnlyList<string>? Channels { get; init; }

    /// <summary>
    /// Channel display mode. Null — global default.
    /// </summary>
    [JsonPropertyName("channel_display_mode")]
    [JsonConverter(typeof(TolerantNullableEnumConverter<ChannelDisplayMode>))]
    public ChannelDisplayMode? ChannelDisplayMode { get; init; }

    /// <summary>
    /// Transaction TTL override in seconds. Null — the value from configuration.
    /// </summary>
    [JsonPropertyName("ttl_seconds")]
    public int? TtlSeconds { get; init; }

    /// <summary>
    /// Time zone the moments of the transaction's messages are shown in (IANA identifier, for example
    /// <c>Europe/Berlin</c>) — the default for relying parties that state none on the request. Null,
    /// or an identifier this installation cannot resolve — the zone stays unknown and the moments are
    /// shown in UTC with the marker that says so.
    /// </summary>
    [JsonPropertyName("default_time_zone")]
    public string? DefaultTimeZone { get; init; }

    /// <summary>
    /// QR settings of the sign-in page (SPEC-012 §9): the same fields as the global section
    /// <c>Veriqa:AuthPageDesign:QrCode</c>, each optional. A field the record does not state is taken
    /// from the level below — the record overrides a field, not the whole section. Null — the record
    /// states no QR settings at all.
    /// </summary>
    [JsonPropertyName("qr_code")]
    public Configuration.QrCodeOverrideOptions? QrCode { get; init; }

    // ── Evolution ────────────────────────────────────────────────────────────

    /// <summary>
    /// Unknown JSON fields preserved round-trip (evolution discipline, R3).
    /// A record newer than the current version is read without losing unknown fields.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// Schema version v1.
    /// </summary>
    public const int SchemaVersionV1 = 1;

    /// <summary>
    /// Checks whether the schema version is valid (CFG-203, §8 assumption #5):
    /// a missing field yields the v1 default; an explicit 0/negative is invalid.
    /// </summary>
    /// <returns>true if the version is ≥ 1.</returns>
    public bool IsSchemaVersionValid() => SchemaVersion >= SchemaVersionV1;
}
