// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Configuration;

/// <summary>
/// Initiator context check settings (SPEC-017 §10, SPEC-012 §6.1).
/// Configuration section: Veriqa:InitiatorContext.
/// Used both by the collection point (Authorization Endpoint) and by the channel processing pipeline
/// (set of displayed fields, anomaly heuristic).
/// </summary>
public sealed class InitiatorContextOptions
{
    /// <summary>
    /// Configuration section name (CFG-113).
    /// </summary>
    public const string SectionName = "Veriqa:InitiatorContext";

    /// <summary>
    /// Default set of displayed fields (ICC-015).
    /// </summary>
    private static readonly IReadOnlyList<string> DefaultDisplayFields = new ReadOnlyCollection<string>(
    [
        InitiatorContextFields.DisplayFields.Application,
        InitiatorContextFields.DisplayFields.Browser,
        InitiatorContextFields.DisplayFields.Os,
        InitiatorContextFields.DisplayFields.Region
    ]);

    /// <summary>
    /// Global switch of the context check.
    /// When false, no snapshot is built and system behavior does not change (ICC-033, ICC-083).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Collect the initiator's IP address (best-effort).
    /// </summary>
    public bool CollectIpAddress { get; set; } = true;

    /// <summary>
    /// Collect and normalize the User-Agent (best-effort).
    /// </summary>
    public bool CollectUserAgent { get; set; } = true;

    /// <summary>
    /// Geolocation by IP (requires an available offline provider, ICC-013).
    /// </summary>
    public bool CollectGeoLocation { get; set; } = true;

    /// <summary>
    /// Whether to store the raw User-Agent in RawMetadata (sensitive data; default — no).
    /// </summary>
    public bool StoreRawUserAgent { get; set; }

    /// <summary>
    /// Fields displayed in the confirmation message
    /// (values — constants of <see cref="InitiatorContextFields.DisplayFields"/>).
    /// </summary>
    public IReadOnlyList<string> DisplayFields { get; set; } = DefaultDisplayFields;

    /// <summary>
    /// Settings of the basic anomaly heuristic (SPEC-017 §9).
    /// </summary>
    public InitiatorAnomalyDetectionOptions AnomalyDetection { get; set; } = new();

    /// <summary>
    /// Offline GeoIP provider settings.
    /// </summary>
    public InitiatorGeoIpOptions GeoIp { get; set; } = new();
}

/// <summary>
/// Settings of the basic initiator context anomaly heuristic (SPEC-017 §9).
/// Disabled by default; without a history source it remains a no-op even when enabled (ICC-072).
/// </summary>
public sealed class InitiatorAnomalyDetectionOptions
{
    /// <summary>
    /// Default comparison signals.
    /// </summary>
    private static readonly IReadOnlyList<string> DefaultSignals = new ReadOnlyCollection<string>(
    [
        InitiatorContextFields.AnomalySignals.Country,
        InitiatorContextFields.AnomalySignals.DeviceType
    ]);

    /// <summary>
    /// Whether the anomaly heuristic is enabled. Default — disabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Signals to compare against the typical context
    /// (values — constants of <see cref="InitiatorContextFields.AnomalySignals"/>).
    /// </summary>
    // Known limitation: experimental — consumed only by the anomaly detector, which stays a no-op
    // until a history store exists (ICC-072). Enabling it without history logs a Warning at startup.
    public IReadOnlyList<string> Signals { get; set; } = DefaultSignals;
}

/// <summary>
/// Offline GeoIP provider settings (SPEC-017, ICC-013):
/// geolocation without calling external online services on the trust path.
/// </summary>
public sealed class InitiatorGeoIpOptions
{
    /// <summary>
    /// GeoIP provider type. Default — the offline database (ICC-013).
    /// </summary>
    public string Provider { get; set; } = OfflineDatabaseProvider;

    /// <summary>
    /// Provider value for the offline database (MaxMind .mmdb or compatible).
    /// </summary>
    public const string OfflineDatabaseProvider = "OfflineDatabase";

    /// <summary>
    /// Path to the local GeoIP database (.mmdb). Null/empty — the provider is unavailable,
    /// geo fields stay null (graceful, ICC-082).
    /// </summary>
    public string? DatabasePath { get; set; }
}
