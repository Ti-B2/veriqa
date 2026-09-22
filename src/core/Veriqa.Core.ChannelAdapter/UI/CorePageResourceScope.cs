// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.UI;

/// <summary>
/// The external resources the page of THIS request actually links — the integrator's stylesheet and,
/// on the sign-in window, the integrator's script. A page resolves them for its own transaction
/// (SPEC-007 UI-101), and the security-headers middleware builds <c>style-src</c>/<c>script-src</c>
/// from the very same values, so the policy can never contradict the markup: an origin missing from
/// the policy makes the browser drop the stylesheet and the page silently loses its styling
/// (SPEC-007 UI-054, UI-040).
/// <para>
/// The type is a per-request (scoped) service rather than an untyped <c>HttpContext.Items</c> entry:
/// the value has a type, an owner and a documented meaning, none of which a string key carries.
/// A request that declares nothing — anything that renders no page of the contour — leaves
/// <see cref="IsDeclared"/> false, and the middleware then answers with the core level, which is the
/// only level such a request has (UI-090).
/// </para>
/// </summary>
public sealed class CorePageResourceScope
{
    /// <summary>
    /// Path or URL of the integrator's stylesheet linked by the page of this request; null — none.
    /// </summary>
    public string? StylesheetPath { get; private set; }

    /// <summary>
    /// Path or URL of the integrator's script loaded by the page of this request; null — none. Only
    /// the sign-in window can carry one: the risky customization mode of SPEC-012 §4.3 is out of reach
    /// of the service pages and of the <c>ui_config</c> record.
    /// </summary>
    public string? ScriptPath { get; private set; }

    /// <summary>
    /// Whether the page of this request has stated its effective resources. False means "no page of
    /// the contour was rendered here", not "the page has no resources".
    /// </summary>
    public bool IsDeclared { get; private set; }

    /// <summary>
    /// States the effective external resources of the page being rendered. Called once per rendered
    /// page, right where the values were resolved; a repeated call overwrites, so a request that
    /// renders a page after an aborted attempt reports the page it actually returned.
    /// </summary>
    /// <param name="stylesheetPath">Effective path of the integrator's stylesheet; null — none.</param>
    /// <param name="scriptPath">Effective path of the integrator's script; null — none.</param>
    public void Declare(string? stylesheetPath, string? scriptPath = null)
    {
        StylesheetPath = stylesheetPath;
        ScriptPath = scriptPath;
        IsDeclared = true;
    }
}
