// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// Shared entry point for resolving the Email tenant-credential group (SPEC-003 §17.4, CA-162/CA-166).
/// Mirrors the Telegram sample (<c>TelegramChannelAdapter.ResolveClient</c>): the tenant comes from the
/// request's ambient context; null — the default implicit tenant (self-hosted N=1), for which the
/// canonical resolver returns the core level from the global <c>IOptionsMonitor</c> — behavior 1:1
/// with the previous direct <c>IOptions&lt;EmailOptions&gt;.Value</c> read.
/// Exists so the resolver call is written once rather than repeated at every Email consumer
/// (no second precedence mechanism — anti-fork CFG-235).
/// </summary>
internal static class EmailCredentialsResolver
{
    /// <summary>
    /// Resolves the effective Email tenant-credentials of the current tenant.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="logger">
    /// Logger of the calling path — it names the place of a credential read made outside a tenant scope.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The credential group, or a graceful failure when no layer supplies credentials.</returns>
    public static ValueTask<Result<EmailTenantCredentials>> ResolveAsync(
        IConfigurationResolver resolver,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var context = ChannelCredentialContext.ForCurrentTenant(ChannelTypes.Email);

        return resolver.ResolveCredentialsAsync(EmailConfigKeys.Credentials, context, logger, cancellationToken);
    }

    /// <summary>
    /// Returns the effective token lifetime: the tenant value when it is usable, otherwise the core default.
    /// </summary>
    /// <remarks>
    /// The tenant value comes from an external source (cloud JSON), so it is validated rather than
    /// trusted (core-rules §10): both a missing field (older JSON) and a non-positive one
    /// (<c>00:00:00</c> or negative — a token that expires the moment it is issued, i.e. a silently
    /// broken sign-in) mean "not configured at this level" and fall back to the core default.
    /// Treating a non-positive value as unset rather than failing the send keeps the degradation
    /// identical to the missing-field case: a misconfigured level never takes the channel down.
    /// </remarks>
    /// <param name="credentials">Resolved tenant-credential group.</param>
    /// <param name="coreDefault">Core-level default (<see cref="EmailOptions.TokenTtl"/>).</param>
    /// <returns>The effective TTL (the core default whenever the tenant value is unusable).</returns>
    public static TimeSpan ResolveTokenTtl(EmailTenantCredentials credentials, TimeSpan coreDefault)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return credentials.TokenTtl is { } tenantTtl && tenantTtl > TimeSpan.Zero
            ? tenantTtl
            : coreDefault;
    }
}
