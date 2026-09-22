// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using OpenIddict.Abstractions;
using OpenIddict.Server;

using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Compatibility;

/// <summary>
/// Applies the <see cref="ClientCompatibilityQuirks.DropResourceParameter"/> quirk: removes the
/// <c>resource</c> parameter (RFC 8707) from an authorization request and from a token request before
/// the built-in OpenIddict validation sees it. This server registers no resources, so OpenIddict
/// rejects any value of that parameter as <c>invalid_target</c> and the sign-in fails before it starts;
/// with nothing consuming the parameter, removing it is equivalent to the client never having sent it —
/// the granted rights come from the scopes either way.
/// <para>
/// The handler is ALWAYS in the pipeline, on both extraction events: whether the relaxation applies is
/// decided at runtime from the effective quirk set of <c>request.ClientId</c>, never by a conditional
/// registration. Its order places it after the ASP.NET Core extraction handlers, so ClientId is already
/// available — including the <c>client_secret_basic</c> credentials of a token request.
/// </para>
/// </summary>
internal sealed class DropResourceParameterHandler
    : IOpenIddictServerHandler<OpenIddictServerEvents.ExtractAuthorizationRequestContext>,
      IOpenIddictServerHandler<OpenIddictServerEvents.ExtractTokenRequestContext>
{
    /// <summary>
    /// Separator of the dropped values in the diagnostics line.
    /// </summary>
    private const string DroppedValueSeparator = " ";

    /// <summary>
    /// Canonical configuration resolver — the single point that yields the effective quirk set per client.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<DropResourceParameterHandler> _logger;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    /// <param name="resolver">Canonical configuration resolver.</param>
    /// <param name="logger">Logger.</param>
    public DropResourceParameterHandler(
        IConfigurationResolver resolver,
        ILogger<DropResourceParameterHandler> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(OpenIddictServerEvents.ExtractAuthorizationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DropResourceAsync(context.Request, context.CancellationToken);
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(OpenIddictServerEvents.ExtractTokenRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DropResourceAsync(context.Request, context.CancellationToken);
    }

    /// <summary>
    /// Drops the <c>resource</c> parameter of the request when the calling client has the quirk enabled,
    /// and leaves every other request untouched. The token request is covered as well as the
    /// authorization one: RFC 8707 §2 defines the parameter for both, and a client carrying a foreign
    /// provider's dialect sends it wherever that provider took it.
    /// </summary>
    /// <param name="request">Request being extracted; null — nothing to relax.</param>
    /// <param name="cancellationToken">Token that cancels the quirk resolution.</param>
    /// <returns>Completion task.</returns>
    private async ValueTask DropResourceAsync(OpenIddictRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return;
        }

        var resources = request.Resources;
        if (resources is not { Length: > 0 } values || string.IsNullOrEmpty(request.ClientId))
        {
            return;
        }

        var quirks = (await _resolver.ResolveAsync(
            AuthServerConfigKeys.ClientCompatibilityQuirks,
            ResolutionContext.Of(tenantId: null, request.ClientId, uiConfigSelector: null),
            ConfigDimensionValues.None,
            cancellationToken)).Value;

        if (!quirks.Contains(ClientCompatibilityQuirks.DropResourceParameter, StringComparer.Ordinal))
        {
            return;
        }

        // The dropped values are the only diagnostics an integrator gets; a resource indicator is a
        // target URI the client states openly and is not a secret.
        _logger.LogInformation(
            "Compatibility quirk {Quirk} dropped the resource parameter {Resource} from the request of client {ClientId}",
            ClientCompatibilityQuirks.DropResourceParameter,
            string.Join(DroppedValueSeparator, values),
            request.ClientId);

        request.Resources = null;
    }
}
