// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.WhatsApp.Configuration;

/// <summary>
/// Shared entry point for resolving the WhatsApp tenant-credential group (SPEC-003 §17.4, CA-162/CA-165/CA-166).
/// Mirrors the Email sample (<c>EmailCredentialsResolver</c> of the Email channel package): the tenant comes from the
/// request's ambient context; null — the default implicit tenant (self-hosted N=1), for which the canonical
/// resolver returns the core level from the global <c>IOptionsMonitor</c> — behavior 1:1 with the previous direct
/// <c>IOptions&lt;WhatsAppOptions&gt;.Value</c> read. Exists so the resolver call is written once rather than
/// repeated at every WhatsApp consumer (no second precedence mechanism — anti-fork CFG-235). WhatsApp has no
/// long-lived bot client (HTTP goes through <c>IHttpClientFactory</c>), so consumption is a direct credential
/// resolution rather than an <c>IChannelClientBuilder</c>/factory client (a deliberate asymmetry with Telegram/MAX).
/// </summary>
internal static class WhatsAppCredentialsResolver
{
    /// <summary>
    /// Resolves the effective WhatsApp tenant-credentials of the current tenant.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="logger">
    /// Logger of the calling path — it names the place of a credential read made outside a tenant scope.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The credential group, or a graceful failure when no layer supplies credentials.</returns>
    public static ValueTask<Result<WhatsAppTenantCredentials>> ResolveAsync(
        IConfigurationResolver resolver,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var context = ChannelCredentialContext.ForCurrentTenant(ChannelTypes.WhatsApp);

        return resolver.ResolveCredentialsAsync(WhatsAppConfigKeys.Credentials, context, logger, cancellationToken);
    }
}
