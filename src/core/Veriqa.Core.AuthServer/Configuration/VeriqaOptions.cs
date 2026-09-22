// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Configuration;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Root Veriqa configuration class (SPEC-012 §6.1).
/// Configuration section: Veriqa.
/// Combines the configuration subsections that are still read AS AN INPUT RECORD into a single class.
/// <para>
/// A subsection whose keys moved to path reading (SPEC-012 §10.6) has no field here, and the sign-in
/// page design — <c>Veriqa:AuthPageDesign</c> — is the first of them: every one of its keys is
/// declared with the address of each of its levels and resolved through them, so a field of this class
/// would bind the same section a second time. That second bind is not harmless duplication: the binder
/// of an options class refuses a value it cannot convert by throwing, which would stop the host on a
/// value the resolution is required to skip silently (SPEC-012 CFG-240/CFG-246). The one setting of
/// that section still read as an input record — the QR group, whose global section carries a critical
/// startup check (SPEC-012 §8.2) — is bound as <see cref="QrCodeOptions"/> on the section it names
/// itself.
/// </para>
/// </summary>
public sealed class VeriqaOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa";

    /// <summary>
    /// Channel display settings (SPEC-012 §4.1).
    /// </summary>
    public ChannelDisplayOptions ChannelDisplay { get; set; } = new();

    /// <summary>
    /// Scopes and claims settings (SPEC-012 §4.6).
    /// </summary>
    public ScopesClaimsOptions ScopesClaims { get; set; } = new();

    /// <summary>
    /// Authentication page localization settings (SPEC-007 §12).
    /// </summary>
    public LocalizationOptions Localization { get; set; } = new();

    /// <summary>
    /// Logging settings (SPEC-012 §6.5).
    /// A mirror field of the root contract; resolved through the shared level-based resolver.
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();

    /// <summary>
    /// Initiator context check settings — a mirror field of the contract (SPEC-012 §6.1).
    /// IMPORTANT: this property is NOT read at runtime. The source of truth is the standalone
    /// IOptions&lt;InitiatorContextOptions&gt;, which is registered and validated separately from the same
    /// Veriqa:InitiatorContext section in AddVeriqaTransactionEngine; all consumers inject exactly that one.
    /// The field exists only for the completeness of the root Veriqa contract — do not tie logic to it
    /// (otherwise the value will diverge from the actually applied standalone binding).
    /// </summary>
    public InitiatorContextOptions InitiatorContext { get; set; } = new();
}
