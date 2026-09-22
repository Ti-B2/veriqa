// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.Contracts;
using Veriqa.Core.ChannelAdapter.WhatsApp.Abstractions;
using Veriqa.Core.ChannelAdapter.WhatsApp.Configuration;
using Veriqa.Core.ChannelAdapter.WhatsApp.Constants;

namespace Veriqa.Core.ChannelAdapter.WhatsApp.Providers.MetaCloudApi;

/// <summary>
/// WhatsApp message delivery provider via the Meta Cloud API (Graph API).
/// The official integration path — requires a WhatsApp Business Account.
/// </summary>
internal sealed class MetaCloudApiWhatsAppProvider : IWhatsAppProvider
{
    /// <summary>
    /// HTTP client factory for Graph API calls.
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// WhatsApp adapter settings — kept ONLY for the infrastructure-level Graph API endpoint fields
    /// (<c>GraphApiBaseUrl</c>/<c>GraphApiVersion</c>), which are not tenant-credential (audit verdict OK).
    /// Tenant-credential fields (<c>PhoneNumberId</c>/<c>AccessToken</c>) are resolved per operation via the seam.
    /// </summary>
    private readonly WhatsAppOptions _options;

    /// <summary>
    /// Per-tenant channel credential provider (seam CA-165). Tenant-credential fields
    /// (<c>PhoneNumberId</c>/<c>AccessToken</c>) are resolved per operation by the ambient tenant, never captured
    /// in the constructor — self-hosted (tenant=null) stays the default N=1 path (1:1 behavior).
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<MetaCloudApiWhatsAppProvider> _logger;

    /// <summary>
    /// Creates an instance of <see cref="MetaCloudApiWhatsAppProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="options">Adapter settings (infrastructure-level Graph API endpoint fields only).</param>
    /// <param name="resolver">Canonical layer resolver (source of the tenant's credentials).</param>
    /// <param name="logger">Logger.</param>
    public MetaCloudApiWhatsAppProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppOptions> options,
        IConfigurationResolver resolver,
        ILogger<MetaCloudApiWhatsAppProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderType => WhatsAppProviderNames.MetaCloudApi;

    /// <inheritdoc />
    public async Task<bool> SendTextMessageAsync(
        string recipientPhone,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        // The method sends a text message via the Graph API.
        var requestBody = new
        {
            messaging_product = MetaCloudApiConstants.MessagingProduct,
            to = recipientPhone,
            type = MetaCloudApiConstants.OutboundTypeText,
            text = new
            {
                body = messageText
            }
        };

        return await SendGraphMessageAsync(
            requestBody,
            recipientPhone,
            MetaCloudApiConstants.OutboundTypeText,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendInteractiveButtonsMessageAsync(
        string recipientPhone,
        string bodyText,
        string confirmButtonId,
        string confirmButtonTitle,
        string declineButtonId,
        string declineButtonTitle,
        CancellationToken cancellationToken = default)
    {
        // The method sends an interactive message with buttons via the Graph API.
        var requestBody = new
        {
            messaging_product = MetaCloudApiConstants.MessagingProduct,
            to = recipientPhone,
            type = MetaCloudApiConstants.OutboundTypeInteractive,
            interactive = new
            {
                type = MetaCloudApiConstants.InteractiveTypeButton,
                body = new
                {
                    text = bodyText
                },
                action = new
                {
                    buttons = new[]
                    {
                        new
                        {
                            type = MetaCloudApiConstants.ReplyButtonType,
                            reply = new
                            {
                                id = confirmButtonId,
                                title = confirmButtonTitle
                            }
                        },
                        new
                        {
                            type = MetaCloudApiConstants.ReplyButtonType,
                            reply = new
                            {
                                id = declineButtonId,
                                title = declineButtonTitle
                            }
                        }
                    }
                }
            }
        };

        return await SendGraphMessageAsync(
            requestBody,
            recipientPhone,
            MetaCloudApiConstants.OutboundTypeInteractive,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(bool IsHealthy, string? Details)> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // The method checks Graph API availability and the validity of the access token.
        try
        {
            // The probe runs outside any request, so no host path has stated whose credentials it
            // should read — and the installation's own (the default implicit tenant) is the answer it
            // wants: a health endpoint reports the state of THIS deployment, not of one of its
            // tenants. That is said explicitly rather than by omission, so that the read is not
            // mistaken for a request path that forgot to state its tenant (CA-164).
            using var tenantScope = ChannelTenantContext.BeginScope(tenantId: null);

            // One credential resolve per operation (CA-171): PhoneNumberId/AccessToken are tenant-credential.
            var credentialsResult = await WhatsAppCredentialsResolver.ResolveAsync(_resolver, _logger, cancellationToken);
            if (credentialsResult.IsFailure)
            {
                _logger.LogError("Meta Cloud API health check: WhatsApp credentials are unavailable.");
                return (false, "WhatsApp credentials are unavailable.");
            }

            var tenantMeta = credentialsResult.Value.MetaCloudApi;

            // GraphApiVersion is infrastructure-level (audit verdict OK) — stays from the global IOptions.
            var graph = _options.MetaCloudApi;
            var endpointPath = $"/{graph.GraphApiVersion}/{tenantMeta.PhoneNumberId}";
            var endpointUrl = BuildGraphApiUri(endpointPath);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpointUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                MetaCloudApiConstants.BearerScheme,
                tenantMeta.AccessToken);

            var client = _httpClientFactory.CreateClient(MetaCloudApiConstants.HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"HTTP {(int)response.StatusCode}");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Meta Cloud API health check failed.");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Sends a message via the WhatsApp Graph API.
    /// </summary>
    /// <param name="requestBody">Request body.</param>
    /// <param name="channelUserId">Recipient's number.</param>
    /// <param name="messageType">Outbound message type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the send succeeded.</returns>
    private async Task<bool> SendGraphMessageAsync(
        object requestBody,
        string channelUserId,
        string messageType,
        CancellationToken cancellationToken)
    {
        // The method sends a POST request to the Graph API and handles the response codes.
        try
        {
            // One credential resolve per operation (CA-171): PhoneNumberId/AccessToken are tenant-credential.
            var credentialsResult = await WhatsAppCredentialsResolver.ResolveAsync(_resolver, _logger, cancellationToken);
            if (credentialsResult.IsFailure)
            {
                _logger.LogError(
                    "Cannot send a WhatsApp (Meta) message: credentials are unavailable. Type={MessageType}, ChannelUserIdHash={ChannelUserIdHash}.",
                    messageType,
                    LogMasking.Fingerprint(channelUserId));

                return false;
            }

            var tenantMeta = credentialsResult.Value.MetaCloudApi;

            // GraphApiVersion is infrastructure-level (audit verdict OK) — stays from the global IOptions.
            var graph = _options.MetaCloudApi;
            var messagesPath = $"/{graph.GraphApiVersion}/{tenantMeta.PhoneNumberId}/{MetaCloudApiConstants.GraphMessagesResourceSegment}";
            var endpointUrl = BuildGraphApiUri(messagesPath);
            var requestJson = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                MetaCloudApiConstants.BearerScheme,
                tenantMeta.AccessToken);
            request.Content = new StringContent(
                requestJson,
                Encoding.UTF8,
                MetaCloudApiConstants.ApplicationJsonContentType);

            var client = _httpClientFactory.CreateClient(MetaCloudApiConstants.HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Error sending a WhatsApp (Meta) message. Type={MessageType}, ChannelUserIdHash={ChannelUserIdHash}, StatusCode={StatusCode}.",
                    messageType,
                    LogMasking.Fingerprint(channelUserId),
                    (int)response.StatusCode);

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception while sending a WhatsApp (Meta) message. Type={MessageType}, ChannelUserIdHash={ChannelUserIdHash}.",
                messageType,
                LogMasking.Fingerprint(channelUserId));

            return false;
        }
    }

    /// <summary>
    /// Builds an absolute Graph API URI.
    /// </summary>
    /// <param name="resourcePath">Relative resource path.</param>
    /// <returns>Absolute URI.</returns>
    private Uri BuildGraphApiUri(string resourcePath)
    {
        // The method combines GraphApiBaseUrl and the resource path into a safe absolute URI.
        var baseUrl = _options.MetaCloudApi.GraphApiBaseUrl.TrimEnd('/');
        var path = resourcePath.StartsWith("/", StringComparison.Ordinal)
            ? resourcePath
            : "/" + resourcePath;

        return new Uri(baseUrl + path, UriKind.Absolute);
    }
}
