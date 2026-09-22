// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// The core's own decisions about confirmation surfaces — the ones no channel declares.
/// </summary>
public static class ConfirmationSurfacePolicy
{
    /// <summary>
    /// Where the core asks the confirming question when the channel cannot ask it itself
    /// (SPEC-012 §4.4.2). One named point rather than the same enum value scattered as a literal:
    /// the core has exactly one implemented surface of its own today, and when a second appears,
    /// this is the single place that has to choose between them.
    /// </summary>
    public const ConfirmationSurface CoreDefault = ConfirmationSurface.OnWebPage;
}
