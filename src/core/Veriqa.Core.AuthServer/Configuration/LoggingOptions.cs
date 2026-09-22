// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Logging settings (SPEC-012 §6.5). Configuration section: Veriqa:Logging.
/// <para>
/// The audit MODE is not a member here: it is owned by the core level alone — neither the application
/// nor the ui_config record overrides it (audit weakening, decision #3) — and it is read by path, so
/// the section is no longer bound into an object for it. What a value outside the enumeration costs is
/// then decided by the strict policy that key declares, and not by the binder of the platform.
/// </para>
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// Configuration section name. It is taken from the owner of the Logging keys rather than spelled
    /// again here: the same section is the CORE level of both of them, and a second literal of it would
    /// be a second answer able to drift from the addresses those keys declare.
    /// </summary>
    public const string SectionName = LoggingConfigKeys.CoreSectionName;

    /// <summary>
    /// Default retention of audit records, in days (SPEC-012 §6.5). Taken from the owner of the key for
    /// the same reason the section is: it is the value the core level of the key states where this
    /// section states none.
    /// </summary>
    public const int DefaultRetentionDays = LoggingConfigKeys.DefaultRetentionDays;

    /// <summary>
    /// Retention of audit records, in days. Protective ceiling: a lower level may only shorten it.
    /// Bounds the lifetime of records already written, regardless of the current mode.
    /// Must be greater than zero and no larger than a hundred years expressed in days — both ends
    /// are validated at startup.
    /// </summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;
}
