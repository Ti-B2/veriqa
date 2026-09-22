// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Transaction initiator context (SPEC-017 §6.1): observable request attributes
/// collected at transaction creation (transaction field #19, SPEC-001 §3.1).
/// Populated exactly once at creation and immutable afterwards (append-only, ICC-030).
/// Not indexed and not used for search (ICC-032).
/// </summary>
public sealed class InitiatorContextSnapshot
{
    /// <summary>
    /// Human-readable name of the client application (required, ICC-010).
    /// Source: OidcContext.ClientId → DisplayName; fallback — the ClientId itself.
    /// </summary>
    [JsonPropertyName("client_application_name")]
    public required string ClientApplicationName { get; init; }

    /// <summary>
    /// Transaction initiation time (UTC, required).
    /// </summary>
    [JsonPropertyName("initiated_at")]
    public required DateTimeOffset InitiatedAt { get; init; }

    /// <summary>
    /// Source IP of the initiator (best-effort, respecting ForwardedHeaders — ICC-020).
    /// Null if collection is disabled or a reliable IP cannot be determined (ICC-022).
    /// </summary>
    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; init; }

    /// <summary>
    /// Trustworthiness of the IP delivery infrastructure (ICC-021): true if the
    /// ForwardedHeaders configuration specifies KnownProxies/KnownIPNetworks (X-Forwarded-For
    /// is accepted only from known proxies). The flag reflects the deployment configuration,
    /// NOT the fact that a specific request passed through a known proxy: with proxies configured,
    /// a direct connection also gets true. A diagnostic attribute for audit —
    /// do not use it as "IP verified" in decision-making logic.
    /// </summary>
    [JsonPropertyName("ip_trusted")]
    public bool IpTrusted { get; init; }

    /// <summary>
    /// Normalized browser name (best-effort, e.g. "Chrome", "Safari").
    /// </summary>
    [JsonPropertyName("browser")]
    public string? Browser { get; init; }

    /// <summary>
    /// Normalized OS/platform (best-effort, e.g. "Windows", "Android").
    /// </summary>
    [JsonPropertyName("os_platform")]
    public string? OsPlatform { get; init; }

    /// <summary>
    /// Device type: desktop / mobile / tablet / unknown (best-effort).
    /// Allowed values — constants of <see cref="InitiatorContextFields"/> (ICC-014).
    /// </summary>
    [JsonPropertyName("device_type")]
    public string? DeviceType { get; init; }

    /// <summary>
    /// Country by GeoIP (approximate, best-effort — ICC-006).
    /// </summary>
    [JsonPropertyName("geo_country")]
    public string? GeoCountry { get; init; }

    /// <summary>
    /// City by GeoIP (approximate, without precise coordinates, best-effort).
    /// </summary>
    [JsonPropertyName("geo_city")]
    public string? GeoCity { get; init; }

    /// <summary>
    /// Raw collection metadata: truncated RawUserAgent (when StoreRawUserAgent is enabled),
    /// IP trust results, GeoIP database version. Sensitive data — not shown to the user
    /// and not logged above Debug (ICC-060).
    /// </summary>
    [JsonPropertyName("raw_metadata")]
    public JsonElement? RawMetadata { get; init; }

    /// <summary>
    /// Snapshot capture time (UTC, required).
    /// </summary>
    [JsonPropertyName("captured_at")]
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    /// Context collector version (semver).
    /// </summary>
    [JsonPropertyName("collector_version")]
    public required string CollectorVersion { get; init; }
}
