// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// The single declaration of the level order by RESOLUTION precedence (CFG-210):
/// user → per-request → ui_config → application → tenant → core. Both the resolver and the startup
/// map read it, so the order exists in one place rather than in two that could drift apart.
/// </summary>
internal static class ConfigLevelPrecedence
{
    /// <summary>
    /// Levels top to bottom by precedence.
    /// </summary>
    public static ConfigLevel[] Order { get; } =
    [
        ConfigLevel.UserOverride,
        ConfigLevel.PerRequest,
        ConfigLevel.UiConfig,
        ConfigLevel.Application,
        ConfigLevel.Tenant,
        ConfigLevel.Core
    ];
}
