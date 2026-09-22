// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Configuration.Enums;

/// <summary>
/// Authentication page color scheme (SPEC-015 §2.2).
/// Controls the data-theme attribute on &lt;html&gt;.
/// </summary>
public enum AuthPageTheme
{
    /// <summary>
    /// Follows the user's system setting (prefers-color-scheme). Default value.
    /// </summary>
    Auto,

    /// <summary>
    /// Light (white) theme.
    /// </summary>
    Light,

    /// <summary>
    /// Dark theme.
    /// </summary>
    Dark
}
