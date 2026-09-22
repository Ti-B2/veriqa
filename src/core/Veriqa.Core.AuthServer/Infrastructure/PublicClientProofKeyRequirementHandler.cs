// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;

using OpenIddict.Abstractions;
using OpenIddict.Server;

using Veriqa.Core.AuthServer.Constants;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Rejects an authorization request of a PUBLIC client that carries no <c>code_challenge</c>, with
/// <c>invalid_request</c> (SPEC-002 §10.2: PKCE is mandatory for <c>client_type = public</c>).
/// <para>
/// The rule reads the client type of the application record, not a flag stored on it. That is what
/// makes it hold for every public client whoever registered it — the configuration seeder or an
/// integrator calling <see cref="IOpenIddictApplicationManager"/> directly. OpenIddict itself offers
/// only two switches: the server-wide <c>RequireProofKeyForCodeExchange()</c>, which cannot be relaxed
/// for a confidential client, and the per-record <c>Requirements.Features.ProofKeyForCodeExchange</c>,
/// which a record simply may not carry. The per-record requirement stays honoured by OpenIddict's own
/// handler, so an integrator can still demand PKCE from a confidential client of theirs.
/// </para>
/// <para>
/// Confidential clients are not affected: SPEC-002 §10.2 leaves PKCE optional for them. It is still
/// recommended for them — a client secret does not protect against an injected or substituted
/// authorization code, which is exactly what PKCE binds to the client instance.
/// </para>
/// </summary>
internal sealed class PublicClientProofKeyRequirementHandler
    : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>
{
    /// <summary>
    /// Description of the rejection; the placeholder takes the name of the missing parameter.
    /// </summary>
    private const string MissingCodeChallengeDescription =
        "The '{0}' parameter is required for public client applications.";

    /// <summary>
    /// OpenIddict application manager — the source of the client type of the record.
    /// </summary>
    private readonly IOpenIddictApplicationManager _applicationManager;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<PublicClientProofKeyRequirementHandler> _logger;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    /// <param name="applicationManager">OpenIddict application manager.</param>
    /// <param name="logger">Logger.</param>
    public PublicClientProofKeyRequirementHandler(
        IOpenIddictApplicationManager applicationManager,
        ILogger<PublicClientProofKeyRequirementHandler> logger)
    {
        _applicationManager = applicationManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask HandleAsync(OpenIddictServerEvents.ValidateAuthorizationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A request carrying a challenge is left to the built-in parameter validation (method S256
        // only); nothing is left for this rule to check.
        if (!string.IsNullOrEmpty(context.Request.CodeChallenge) || string.IsNullOrEmpty(context.ClientId))
        {
            return;
        }

        // The pipeline runs this handler after the client identifier was validated against the store,
        // so the record exists; a missing one is left to the handlers that own that check.
        var application = await _applicationManager.FindByClientIdAsync(context.ClientId, context.CancellationToken);
        if (application is null
            || !await _applicationManager.HasClientTypeAsync(
                application,
                OpenIddictConstants.ClientTypes.Public,
                context.CancellationToken))
        {
            return;
        }

        _logger.LogWarning(
            "Authorization request of public client {ClientId} rejected: the {Parameter} parameter is missing. ErrorCode: {ErrorCode}",
            context.ClientId,
            OpenIddictConstants.Parameters.CodeChallenge,
            OidcErrorCodes.PkceRequired);

        context.Reject(
            error: OpenIddictConstants.Errors.InvalidRequest,
            description: string.Format(
                CultureInfo.InvariantCulture,
                MissingCodeChallengeDescription,
                OpenIddictConstants.Parameters.CodeChallenge));
    }
}
