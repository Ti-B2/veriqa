// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Diagnostic result codes for the initiator context check (SPEC-017 §11, ICC-090).
/// Used only for audit/diagnostics; none of the codes moves a transaction
/// to Failed (ICC-091) — context collection is best-effort and does not block transaction creation.
/// </summary>
public static class InitiatorContextResultCodes
{
    /// <summary>
    /// The GeoIP provider is unavailable; geo fields remain null.
    /// </summary>
    public const string GeoIpUnavailable = "initiator_context_geoip_unavailable";

    /// <summary>
    /// The IP was obtained from an untrusted X-Forwarded-For (ICC-021).
    /// </summary>
    public const string IpUntrusted = "initiator_context_ip_untrusted";

    /// <summary>
    /// The anomaly heuristic is enabled, but there is no history to compare against (ICC-072).
    /// </summary>
    public const string AnomalyNoHistory = "initiator_context_anomaly_no_history";

    /// <summary>
    /// RawMetadata exceeded the limit; the excess was truncated (ICC-031).
    /// </summary>
    public const string ContextTooLarge = "initiator_context_too_large";
}
