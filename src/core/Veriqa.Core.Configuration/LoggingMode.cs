// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Logging mode (SPEC-012 §6.5). Protective setting: no level below the owning one may weaken it
/// (SPEC-011 R8, SPEC-012 CFG-052) — the user step and its gate were removed (SPEC-012 §7), and the
/// application does NOT override the mode either (audit weakening, decision #3).
/// "Stricter" = more logging (Audit &gt; System &gt; Disabled).
/// Audit axis only: operational Serilog logs are configured separately, per deployment
/// (SPEC-012 §4.5, SPEC-011 R12).
/// <para>
/// It lives in the settings mechanism rather than in the auth server because both sides of the
/// Logging axis depend on this assembly and neither depends on the other: the auth server owns the
/// options the core level is read from, the audit trail is the consumer of the resolved value, and
/// an audit journal is a cross-cutting sink rather than a feature of an OIDC server. A product type
/// in a mechanism assembly is the exception this axis buys, not a new norm — see
/// <see cref="LoggingConfigKeys"/>.
/// </para>
/// </summary>
public enum LoggingMode
{
    /// <summary>
    /// No audit records are written (the least strict mode).
    /// </summary>
    Disabled,

    /// <summary>
    /// No audit records are written; business events are not audited (the default value).
    /// For the audit axis it is equivalent to <see cref="Disabled"/>.
    /// </summary>
    System,

    /// <summary>
    /// Full action audit (the strictest mode, SPEC-011).
    /// </summary>
    Audit
}
