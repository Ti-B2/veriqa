// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Diagnostic identifiers Veriqa reports from its own attributes.
/// </summary>
/// <remarks>
/// The numbering is deliberately separate from the MSBuild error codes of this repository
/// (<c>VERIQA0001</c> and its neighbours): those are build errors raised by a target, these are
/// compiler diagnostics raised by an analyzer, and a shared number space between two diagnostic
/// systems would make a suppression list unreadable. Consumers see these identifiers only in a
/// compiler message and in a <c>NoWarn</c>/<c>#pragma</c> of their own, so they are documented in
/// the package README rather than published as a public constant.
/// </remarks>
internal static class VeriqaDiagnosticIds
{
    /// <summary>
    /// The shape of a channel SPI extension point has not settled yet: the type is a closed
    /// hierarchy whose roadmap (claim enrichment, a shared contact, unlinking a channel, a chain of
    /// channels) adds variants to it. A member marked with this identifier may change in a minor
    /// version — that is precisely what the marker buys, and what keeps the rest of the surface
    /// under the ordinary "a change is a major" rule.
    /// </summary>
    internal const string UnsettledChannelSpiShape = "VERIQAEXP0001";
}
