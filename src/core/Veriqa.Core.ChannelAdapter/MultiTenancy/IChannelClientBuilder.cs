// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Builder of a channel client from the tenant's effective token (SPEC-003 §17.4, CA-166).
/// Registered by every channel that uses a bot client (Telegram/MAX) under the closed generic
/// service type <c>IChannelClientBuilder&lt;TClient&gt;</c>; the <see cref="IChannelClientFactory"/>
/// factory picks the builder of the requested client type and caches the result per-tenant. This way
/// the client stops being a singleton built from a single token and becomes derived from the tenant's
/// credentials.
/// </summary>
/// <remarks>
/// The client type is the type parameter, not a declared property: an implementation cannot name one
/// type and build another, and the registration point derives the factory key from
/// <typeparamref name="TClient"/> itself.
/// </remarks>
/// <typeparam name="TClient">Type of the client the builder creates (e.g. <c>ITelegramBotClient</c>).</typeparam>
public interface IChannelClientBuilder<TClient>
    where TClient : class
{
    /// <summary>
    /// Channel type the builder creates the client for.
    /// </summary>
    string ChannelType { get; }

    /// <summary>
    /// Creates a channel client from the tenant's effective credentials (resolves them and extracts
    /// the token internally — the builder owns the credentials of its own channel).
    /// </summary>
    /// <param name="context">Resolution context <c>(tenant, ChannelType)</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success with the client, or a missing-credentials error. A success carrying <c>null</c> violates
    /// the contract and is treated by the factory as a missing-credentials failure.
    /// </returns>
    ValueTask<Result<TClient>> BuildAsync(
        ChannelCredentialContext context,
        CancellationToken cancellationToken = default);
}
