// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using OpenIddict.Abstractions;
using OpenIddict.Server;

using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Compatibility;

/// <summary>
/// Applies the <see cref="ClientCompatibilityQuirks.DropScopeOnCodeExchange"/> quirk: removes the
/// <c>scope</c> parameter from a token request made with <c>grant_type=authorization_code</c> before the
/// built-in OpenIddict validation sees it. RFC 6749 §4.1.3 does not define the parameter for that grant,
/// and OpenIddict rejects the whole request because of it; for the code grant the submitted value is not
/// used at all — the granted set comes from the authorization code — so removing it is equivalent to the
/// client never having sent it.
/// <para>
/// The handler is ALWAYS in the pipeline: whether the relaxation applies is decided at runtime from the
/// effective quirk set of <c>request.ClientId</c>, never by a conditional registration. Its order places
/// it after the ASP.NET Core extraction handlers (including the Basic credentials parsing), so ClientId is
/// available for <c>client_secret_basic</c> as well, not only when the credentials sit in the body.
/// </para>
/// </summary>
internal sealed class DropScopeOnCodeExchangeHandler
    : IOpenIddictServerHandler<OpenIddictServerEvents.ExtractTokenRequestContext>
{
    /// <summary>
    /// Canonical configuration resolver — the single point that yields the effective quirk set per client.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<DropScopeOnCodeExchangeHandler> _logger;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    /// <param name="resolver">Canonical configuration resolver.</param>
    /// <param name="logger">Logger.</param>
    public DropScopeOnCodeExchangeHandler(
        IConfigurationResolver resolver,
        ILogger<DropScopeOnCodeExchangeHandler> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask HandleAsync(OpenIddictServerEvents.ExtractTokenRequestContext context)
    {
        // The handler drops the scope parameter of a code exchange when the calling client has the quirk
        // enabled, and leaves every other request untouched.

        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        if (request is null)
        {
            return;
        }

        // Only the code exchange is relaxed. The refresh_token path keeps the parameter: there scope is
        // allowed by RFC 6749 §6 and narrows the granted rights, so dropping it would change the outcome.
        // Once the device code flow is enabled in this server, the same relaxation extends to the
        // device_code grant (the parameter is equally undefined for it); that grant is not enabled today,
        // which is why no branch for it exists here.
        if (!request.IsAuthorizationCodeGrantType())
        {
            return;
        }

        if (string.IsNullOrEmpty(request.Scope) || string.IsNullOrEmpty(request.ClientId))
        {
            return;
        }

        var quirks = (await _resolver.ResolveAsync(
            AuthServerConfigKeys.ClientCompatibilityQuirks,
            ResolutionContext.Of(tenantId: null, request.ClientId, uiConfigSelector: null),
            ConfigDimensionValues.None,
            context.CancellationToken)).Value;

        if (!quirks.Contains(ClientCompatibilityQuirks.DropScopeOnCodeExchange, StringComparer.Ordinal))
        {
            return;
        }

        // The dropped value is the only diagnostics an integrator gets; scope values are not secrets.
        _logger.LogInformation(
            "Compatibility quirk {Quirk} dropped the scope parameter {Scope} from the code exchange of client {ClientId}",
            ClientCompatibilityQuirks.DropScopeOnCodeExchange,
            request.Scope,
            request.ClientId);

        request.Scope = null;

        return;
    }
}
