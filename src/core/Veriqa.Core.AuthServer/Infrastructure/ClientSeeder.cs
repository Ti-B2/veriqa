// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Constants;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Background service for registering OIDC clients in OpenIddict at application startup.
/// Reads the client configuration from OidcClientsOptions.
/// </summary>
public sealed class ClientSeeder : IHostedService
{
    /// <summary>
    /// Service provider for creating a scope.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// OIDC client settings.
    /// </summary>
    private readonly OidcClientsOptions _clientsOptions;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<ClientSeeder> _logger;

    /// <summary>
    /// Initializes the client registration service.
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="clientsOptions">OIDC client settings.</param>
    /// <param name="logger">Logger.</param>
    public ClientSeeder(
        IServiceProvider serviceProvider,
        IOptions<OidcClientsOptions> clientsOptions,
        ILogger<ClientSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _clientsOptions = clientsOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The method registers all OIDC clients in OpenIddict at application startup

        if (_clientsOptions.Clients.Count is 0)
        {
            _logger.LogWarning("The OIDC client list is empty — no clients will be registered");
            return;
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var clientOptions in _clientsOptions.Clients)
        {
            await SeedClientAsync(manager, clientOptions, cancellationToken);
        }
    }

    /// <summary>
    /// Registers a single OIDC client in OpenIddict.
    /// If the client already exists — updates its configuration.
    /// If it does not exist — creates a new one.
    /// </summary>
    /// <param name="manager">OpenIddict application manager.</param>
    /// <param name="clientOptions">Client parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task SeedClientAsync(
        IOpenIddictApplicationManager manager,
        OidcClientOptions clientOptions,
        CancellationToken cancellationToken)
    {
        // The method creates or updates an OpenIddict client from configuration

        if (string.IsNullOrEmpty(clientOptions.ClientId))
        {
            _logger.LogWarning("ClientId is not set — client skipped");
            return;
        }

        try
        {
            var descriptor = BuildDescriptor(clientOptions);
            var existingClient = await manager.FindByClientIdAsync(clientOptions.ClientId, cancellationToken);

            if (existingClient is not null)
            {
                // Update the existing client to sync the configuration
                // without deletion (preserves active tokens and authorizations).
                // The source of truth remains the descriptor built from configuration,
                // so we apply it to the existing client directly.
                await manager.UpdateAsync(existingClient, descriptor, cancellationToken);

                _logger.LogInformation(
                    "Client {ClientId} updated for configuration sync",
                    clientOptions.ClientId);
            }
            else
            {
                await manager.CreateAsync(descriptor, cancellationToken);

                _logger.LogInformation(
                    "Client {ClientId} ({ClientType}) registered successfully with {RedirectUriCount} redirect URIs",
                    clientOptions.ClientId,
                    descriptor.ClientType,
                    descriptor.RedirectUris.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error registering client {ClientId}: {ErrorCode}",
                clientOptions.ClientId,
                OidcErrorCodes.ClientSeedingFailed);

            throw;
        }
    }

    /// <summary>
    /// Creates an OpenIddict application descriptor from the client parameters.
    /// </summary>
    /// <param name="clientOptions">Client parameters.</param>
    /// <returns>OpenIddict application descriptor.</returns>
    private static OpenIddictApplicationDescriptor BuildDescriptor(OidcClientOptions clientOptions)
    {
        // The method builds an OpenIddict descriptor from the client configuration

        var hasClientSecret = !string.IsNullOrEmpty(clientOptions.ClientSecret);

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientOptions.ClientId,
            ClientSecret = hasClientSecret ? clientOptions.ClientSecret : null,
            DisplayName = clientOptions.DisplayName,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            ClientType = hasClientSecret
                ? OpenIddictConstants.ClientTypes.Confidential
                : OpenIddictConstants.ClientTypes.Public
        };

        // Redirect URIs
        foreach (var uriStr in clientOptions.AllowedRedirectUris)
        {
            if (Uri.TryCreate(uriStr, UriKind.Absolute, out var parsedUri))
            {
                descriptor.RedirectUris.Add(parsedUri);
            }
            // Invalid URIs are skipped (validation happens at the OidcClientsOptionsValidator level)
        }

        // No PKCE requirement is written into the record. PKCE for a public client (SPEC-002 §10.2) is
        // enforced by the server pipeline from the ClientType set above, so it does not depend on how the
        // record was created. For a confidential client the spec leaves PKCE optional; it is still
        // recommended, since the secret does not protect against a substituted authorization code.

        // Base permissions: Authorization Code Flow
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);

        // Scopes
        foreach (var scope in clientOptions.AllowedScopes)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
        }

        // Refresh tokens
        if (clientOptions.AllowRefreshTokens)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        }

        // Client Credentials Grant (SPEC-039 R2): the permission is attached ONLY to an entry that
        // states it, so the server-wide grant stays closed for every client that did not ask for it —
        // OpenIddict then answers unauthorized_client without a token ever being issued.
        if (clientOptions.AllowClientCredentials)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        }

        // Confirmation token grant (SPEC-039 C51): the permission follows the flag and nothing else, so
        // a client that did not state it is answered unauthorized_client by the server itself and the
        // grant branch carries no check of its own.
        if (clientOptions.AllowConfirmationTokenGrant)
        {
            descriptor.Permissions.Add(
                OpenIddictConstants.Permissions.Prefixes.GrantType + ConfirmationTokenGrant.GrantType);
        }

        // Token introspection (RFC 7662): the permission follows the flag and nothing else, so a client
        // that did not state it is answered by OpenIddict itself without any token being examined.
        if (clientOptions.AllowIntrospection)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
        }

        return descriptor;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
