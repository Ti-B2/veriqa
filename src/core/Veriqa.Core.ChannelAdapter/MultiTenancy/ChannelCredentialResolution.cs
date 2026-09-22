// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Resolution of a channel's tenant-credential group through the canonical resolver (SPEC-003 §17.4,
/// SPEC-012 CFG-234/235): the single place where the credential resolution context is assembled and
/// where "no layer supplies credentials" is turned into a graceful failure.
/// <para>
/// It is a helper and deliberately not a seam: a substitutable interface over this call would be able
/// to answer with credentials that never passed the level precedence, the last-good degradation, the
/// secret masking or the cache — the second precedence mechanism CFG-235 forbids. The self-hosted case
/// is not a branch here either: with empty upper layers the resolver returns the core value of the
/// global <c>IOptions</c>, which is the N=1 behaviour 1:1.
/// </para>
/// </summary>
internal static class ChannelCredentialResolution
{
    /// <summary>
    /// Resolves the effective tenant-credential group of a channel.
    /// </summary>
    /// <typeparam name="TCredentials">Channel tenant-credential group type (041.1).</typeparam>
    /// <param name="resolver">Canonical layer resolver (the only one in the core).</param>
    /// <param name="key">Credential key of the channel.</param>
    /// <param name="context">Resolution key <c>(tenant, ChannelType)</c>.</param>
    /// <param name="logger">
    /// Logger of the caller — the place a credential read is made from, so that a read made outside a
    /// tenant scope names that place rather than this shared helper.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success with credentials, or <c>Result.Failure</c> with code
    /// <see cref="ChannelCredentialErrorCodes.ChannelCredentialsMissing"/> when no layer defines them.
    /// </returns>
    internal static async ValueTask<Result<TCredentials>> ResolveCredentialsAsync<TCredentials>(
        this IConfigurationResolver resolver,
        ConfigKey<TCredentials> key,
        ChannelCredentialContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
        where TCredentials : class
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        // A key built from the ambient tenant while no scope is open lands on the default tenant
        // without anybody having said so — in a multi-tenant installation that means the core
        // credentials are used for somebody else's request. It is not refused (the default tenant is
        // the correct answer for a self-hosted N=1 installation, where no scope is needed), it is made
        // visible: the path that forgot to open a scope is named by the caller's logger instead of
        // landing on the core silently.
        if (context.FromAmbient && !ChannelTenantContext.IsEstablished)
        {
            logger.LogWarning(
                "Credentials of channel {ChannelType} resolved outside a tenant scope — "
                + "the default tenant is assumed",
                context.ChannelType);
        }

        var resolved = await resolver.ResolveAsync(
            key,
            ResolutionContext.ForTenant(context.TenantId),
            ConfigDimensionValues.None,
            cancellationToken);

        // No layer set the credentials → missing for this tenant (no exception).
        if (resolved.Value is not { } credentials)
        {
            return Result<TCredentials>.Failure(
                ChannelCredentialErrorCodes.ChannelCredentialsMissing,
                $"Credentials of channel '{context.ChannelType}' not found for tenant "
                + $"'{context.TenantId ?? ChannelTenantContext.DefaultTenantDisplayName}'.");
        }

        return Result<TCredentials>.Success(credentials);
    }
}
