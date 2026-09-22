// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Resolver for the effective login confirmation surface (SPEC-012 §4.4.2). The single point that
/// encapsulates the flow: level resolver (§10.6) → expansion of the <c>ChannelDefault</c> sentinel
/// from the channel's single confirmation fact → routing of an in-channel confirmation the channel
/// cannot perform to the core's own surface, with a WARNING.
/// Consumers receive an already CONCRETE surface (<c>None</c>/<c>InChannel</c>/<c>OnWebPage</c>), never
/// the <c>ChannelDefault</c> sentinel or the reserved <c>InChannelAndOnWebPage</c>.
/// </summary>
public interface IEffectiveConfirmationSurfaceResolver
{
    /// <summary>
    /// Computes the effective confirmation surface for a transaction in the context of a specific channel.
    /// </summary>
    /// <param name="transaction">The transaction (source of resolution context: application, etc.).</param>
    /// <param name="adapter">The confirmation channel adapter (source of channel facts).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A concrete confirmation surface: <c>None</c>, <c>InChannel</c>, or <c>OnWebPage</c>.
    /// </returns>
    ValueTask<ConfirmationSurface> ResolveEffectiveSurfaceAsync(
        Transaction transaction,
        IChannelAdapter adapter,
        CancellationToken cancellationToken = default);
}
