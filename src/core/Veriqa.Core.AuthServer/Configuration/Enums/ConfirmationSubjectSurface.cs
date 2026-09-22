// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Configuration.Enums;

/// <summary>
/// A surface the SUBJECT of a confirmation may be shown on besides the page that asks the question
/// (SPEC-012 §4.11, CFG-104). The set of them is what a deployment configures; the surface that asks
/// the question is not part of it and needs no permission — it is the question.
/// </summary>
public enum ConfirmationSubjectSurface
{
    /// <summary>
    /// The entry page of a confirmation transaction (SPEC-039 C15): the page that shows the way into
    /// the channel. Showing the subject there tells the user what they are about to be asked about
    /// before they leave for the channel.
    /// </summary>
    InteractionPage,

    /// <summary>
    /// The hop interstitial (SPEC-025). The value is part of the axis as the specification declares
    /// it, and configuration accepts it — but no display stands behind it while that specification is
    /// frozen, and a deployment that states it is told so at startup rather than left believing the
    /// display is configured.
    /// </summary>
    HopInterstitial
}
