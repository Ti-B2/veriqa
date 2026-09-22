// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Setting ownership level in the canonical configuration model (SPEC-012 §10).
/// Values are ordered BY RESOLUTION PRIORITY: the lower the numeric value, the higher
/// the priority (applied earlier). Canonical precedence order of SPEC-012 §10.3:
/// user override (bot) → per-request → ui_config → application → tenant → core.
/// Values are stored bottom-up by ownership and applied top-down by priority.
/// </summary>
public enum ConfigLevel
{
    /// <summary>
    /// User/bot-level override (highest priority in the order of SPEC-012 §10.3).
    /// Applied only when an upper-level gate is present.
    /// <para>
    /// RESERVED STEP OF THE SCALE: not implemented, no consumer. No key declares it, no source serves
    /// it and no surface collects a user's own value. It keeps its place in the precedence order so
    /// that the canonical order does not have to be renumbered once such a surface appears.
    /// </para>
    /// </summary>
    UserOverride = 0,

    /// <summary>
    /// Parameters of a specific request (acr_values/scope etc.).
    /// <para>
    /// RESERVED STEP OF THE SCALE: not implemented, no consumer — see <see cref="UserOverride"/>.
    /// </para>
    /// </summary>
    PerRequest = 1,

    /// <summary>
    /// Dynamic ui_config record (tenant artifact, SPEC-012 §10.2).
    /// </summary>
    UiConfig = 2,

    /// <summary>
    /// Application level (OIDC client, client_id).
    /// </summary>
    Application = 3,

    /// <summary>
    /// Tenant (project) level.
    /// </summary>
    Tenant = 4,

    /// <summary>
    /// Core level — global IOptions. For self-hosted (N=1) — the only specified level.
    /// </summary>
    Core = 5
}
