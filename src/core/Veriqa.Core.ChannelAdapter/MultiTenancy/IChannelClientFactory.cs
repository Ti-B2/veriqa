// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Contract seam "per-tenant channel client factory" (SPEC-003 §17.4, CA-166).
/// By the <c>(tenant, ChannelType)</c> key it creates/returns a cached per-tenant channel
/// client (bot client) built from the tenant's effective token. Replaces the registration of
/// a singleton client built from a single token. The cache is thread-safe; for the default
/// tenant (N=1) the client is immutable within the process. Anti-fork: the token is taken from
/// credentials resolved by the canonical TASK-040 resolver (CFG-235).
/// </summary>
public interface IChannelClientFactory
{
    /// <summary>
    /// Creates or returns a cached channel client for the context.
    /// </summary>
    /// <typeparam name="TClient">Channel client type (e.g. <c>ITelegramBotClient</c>).</typeparam>
    /// <param name="context">Resolution context <c>(tenant, ChannelType)</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success with a client, or <c>Result.Failure</c> with code
    /// <see cref="ChannelCredentialErrorCodes.ChannelCredentialsMissing"/> /
    /// <see cref="ChannelCredentialErrorCodes.ChannelNotAvailable"/>.
    /// </returns>
    ValueTask<Result<TClient>> GetOrCreateClientAsync<TClient>(
        ChannelCredentialContext context,
        CancellationToken cancellationToken = default)
        where TClient : class;
}
