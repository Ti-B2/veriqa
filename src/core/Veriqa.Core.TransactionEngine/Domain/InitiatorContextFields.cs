// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// String constants for the initiator context fields and values (SPEC-017, ICC-014).
/// Eliminate magic strings when working with DeviceType and the set of displayed fields.
/// </summary>
public static class InitiatorContextFields
{
    /// <summary>
    /// Allowed device type values (<see cref="InitiatorContextSnapshot.DeviceType"/>).
    /// </summary>
    public static class DeviceTypes
    {
        /// <summary>
        /// Desktop computer / laptop.
        /// </summary>
        public const string Desktop = "desktop";

        /// <summary>
        /// Mobile device.
        /// </summary>
        public const string Mobile = "mobile";

        /// <summary>
        /// Tablet.
        /// </summary>
        public const string Tablet = "tablet";

        /// <summary>
        /// Device type could not be determined.
        /// </summary>
        public const string Unknown = "unknown";
    }

    /// <summary>
    /// Displayed field names for the DisplayFields configuration (SPEC-017 §10).
    /// </summary>
    public static class DisplayFields
    {
        /// <summary>
        /// Client application name.
        /// </summary>
        public const string Application = "application";

        /// <summary>
        /// Initiator's browser.
        /// </summary>
        public const string Browser = "browser";

        /// <summary>
        /// Initiator's OS/platform.
        /// </summary>
        public const string Os = "os";

        /// <summary>
        /// Approximate region (city, country).
        /// </summary>
        public const string Region = "region";
    }

    /// <summary>
    /// Anomaly heuristic signals (AnomalyDetection.Signals, SPEC-017 §9–10).
    /// </summary>
    public static class AnomalySignals
    {
        /// <summary>
        /// Comparison by country.
        /// </summary>
        public const string Country = "country";

        /// <summary>
        /// Comparison by device type.
        /// </summary>
        public const string DeviceType = "deviceType";
    }
}
