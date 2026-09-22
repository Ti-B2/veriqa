// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.WhatsApp.Abstractions;
using Veriqa.Core.ChannelAdapter.WhatsApp.Configuration;
using Veriqa.Core.ChannelAdapter.WhatsApp.Constants;
using Veriqa.Core.ChannelAdapter.WhatsApp.Domain;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.MessageTemplates;

namespace Veriqa.Core.ChannelAdapter.WhatsApp;

/// <summary>
/// WhatsApp channel adapter (SPEC-003 §10).
/// There is a single channel adapter — the channel logic (handling inbound events, identity, deep link) is shared.
/// The message delivery provider (the shipped Meta Cloud API one, or a provider supplied by the host)
/// is injected via <see cref="IWhatsAppProvider"/>.
/// </summary>
internal sealed class WhatsAppChannelAdapter : IChannelAdapter
{
    /// <summary>
    /// WhatsApp message delivery provider.
    /// </summary>
    private readonly IWhatsAppProvider _provider;

    /// <summary>
    /// Per-tenant channel credential provider (seam CA-165). Tenant-credential fields (business number,
    /// webhook verify token, app secret) are resolved per operation by the ambient tenant, never captured
    /// in the constructor — self-hosted (tenant=null) stays the default N=1 path (1:1 behavior).
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<WhatsAppChannelAdapter> _logger;

    /// <summary>
    /// Localizer of channel messages into the recipient's language (SPEC-017 §7.2).
    /// </summary>
    private readonly IConfirmationPromptLocalizer _promptLocalizer;

    /// <summary>
    /// The single point at which the render points of this adapter obtain a resolved message —
    /// through the canonical resolver (SPEC-036 TPL-001). Singleton, like this adapter.
    /// </summary>
    private readonly IMessageTemplateAccessor _messageTemplates;

    /// <summary>
    /// Reader of the deep-link prefill context (SPEC-003 §10.4): the application name and page locale for
    /// the formatted prefill message. Singleton, like this adapter — no captive dependency.
    /// </summary>
    private readonly DeepLinkPrefillContextReader _prefillContextReader;

    /// <summary>
    /// Clock behind the health-check stamps and the capture timestamps of the adapter.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates an instance of <see cref="WhatsAppChannelAdapter"/>.
    /// </summary>
    /// <param name="provider">Message delivery provider.</param>
    /// <param name="resolver">Canonical layer resolver (source of the tenant's credentials).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="promptLocalizer">Channel message localizer.</param>
    /// <param name="messageTemplates">Accessor of the resolved messages of this adapter.</param>
    /// <param name="prefillContextReader">Reader of the deep-link prefill context.</param>
    /// <param name="timeProvider">Time provider.</param>
    public WhatsAppChannelAdapter(
        IWhatsAppProvider provider,
        IConfigurationResolver resolver,
        ILogger<WhatsAppChannelAdapter> logger,
        IConfirmationPromptLocalizer promptLocalizer,
        IMessageTemplateAccessor messageTemplates,
        DeepLinkPrefillContextReader prefillContextReader,
        TimeProvider timeProvider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _promptLocalizer = promptLocalizer ?? throw new ArgumentNullException(nameof(promptLocalizer));
        _messageTemplates = messageTemplates ?? throw new ArgumentNullException(nameof(messageTemplates));
        _prefillContextReader = prefillContextReader ?? throw new ArgumentNullException(nameof(prefillContextReader));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public string ChannelType => ChannelTypes.WhatsApp;

    /// <summary>
    /// Facts WhatsApp declares about itself (SPEC-003 §6.1, SPEC-012 §4.4.1): the channel supports
    /// interactive reply buttons, so it can ask for and receive a confirmation inside itself, like
    /// Telegram/MAX.
    /// </summary>
    /// <remarks>
    /// Interactive messages are paid, but that is not expressed as a confirmation surface: a channel
    /// declaring the web page instead makes the page ask the user to re-confirm a transaction the
    /// channel has already completed, on a different device than the one they have just used. Cost is
    /// an argument about whether a tenant is ALLOWED to send a button, not about whether
    /// the channel CAN confirm — the permission layer, expressed as a per-tenant channel setting, not
    /// as a channel fact. Operators who want the one-step flow configure
    /// <see cref="LoginConfirmationMode.None"/> explicitly.
    /// <para>
    /// The prompt is always a reply to the message the user has just sent (it is triggered by that
    /// inbound message, <c>AuthStart</c>), so it goes out inside the 24-hour customer service window,
    /// where free-form interactive messages are allowed and no pre-approved template is required.
    /// </para>
    /// <para>
    /// The Cloud API cannot edit a message already delivered to a user, so a terminal status is shown
    /// as a new message; the inbound webhook (messages/contacts) carries no recipient locale (verified
    /// against the official Meta Cloud API webhooks documentation, 2026-07-18), so consumers fall back
    /// to the transaction locale.
    /// </para>
    /// </remarks>
    private static readonly ChannelCapabilities DeclaredCapabilities = new()
    {
        SupportsInChannelConfirmation = true,
        SupportedMessageKinds = new[] { ChannelMessageKind.PlainText }.ToFrozenSet(),
        SupportsMessageUpdate = false,
        DeliversOutcomeNotice = true,
        ProvidesRecipientLocale = false
    };

    /// <inheritdoc />
    public ChannelCapabilities Capabilities => DeclaredCapabilities;

    /// <summary>
    /// WhatsApp webhook payload serialization options.
    /// </summary>
    private static readonly JsonSerializerOptions WhatsAppJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public Task<Result<ChannelInboundResult>> ProcessInboundEventAsync(
        ChannelInboundRequest request,
        CancellationToken cancellationToken = default)
    {
        // The method processes the inbound webhook payload and extracts the first relevant auth event.
        // The wire format is parsed here, not in the core: the channel logic is shared across all
        // providers, and the provider's payload format is normalized to WhatsAppWebhookPayload.
        WhatsAppWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(request.Body.Span, WhatsAppJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize the WhatsApp webhook payload.");
            return Task.FromResult(Result<ChannelInboundResult>.Success(new ChannelUnrelatedResult()));
        }

        if (payload is null)
        {
            return Task.FromResult(Result<ChannelInboundResult>.Success(new ChannelUnrelatedResult()));
        }

        if (!string.Equals(
                payload.ObjectType,
                WhatsAppAdapterConstants.WebhookObjectType,
                StringComparison.Ordinal))
        {
            return Task.FromResult(Result<ChannelInboundResult>.Success(
                new ChannelUnrelatedResult { RawEvent = payload }));
        }

        foreach (var entry in payload.Entries ?? [])
        {
            foreach (var change in entry?.Changes ?? [])
            {
                if (change is null)
                {
                    continue;
                }

                if (!string.Equals(
                        change.Field,
                        WhatsAppAdapterConstants.WebhookMessagesField,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (change.Value?.Messages is null || change.Value.Messages.Count is 0)
                {
                    continue;
                }

                foreach (var message in change.Value.Messages)
                {
                    var mappedResult = TryMapMessageToInboundResult(message, change.Value.Contacts, payload);
                    if (mappedResult is not null)
                    {
                        return Task.FromResult(mappedResult);
                    }
                }
            }
        }

        return Task.FromResult(Result<ChannelInboundResult>.Success(
            new ChannelUnrelatedResult { RawEvent = payload }));
    }

    /// <inheritdoc />
    public async Task<Result<ChannelMessageRef?>> SendConfirmationPromptAsync(
        TransactionId transactionId,
        string channelUserId,
        ConfirmationPromptContext promptContext,
        CancellationToken cancellationToken = default)
    {
        // The method sends an interactive message with confirm/decline buttons via the provider.
        // The recipient locale travels with the context and nowhere else (SPEC-017 §7.2).
        var recipientLocale = promptContext.RecipientLocale;

        var normalizedPhoneResult = TryNormalizePhoneToE164(channelUserId);
        if (normalizedPhoneResult is null)
        {
            return Result<ChannelMessageRef?>.Failure(
                ChannelAdapterErrorCodes.MessageSendFailed,
                "Invalid WhatsApp channel user identifier.");
        }

        // Render the text from the confirmation context (SPEC-017 §7.2) in the recipient's language;
        // on a context without details — the default text (ICC-042), also localized by the passed
        // locale (ICC-050). The wording is asked for at the FULL address the point has — the surface
        // it is shown on and the channel refining it — over the ownership levels the transaction
        // states, so a variant declared by a tenant, an application or a ui_config record answers
        // instead of the shipped one (SPEC-036 TPL-116).
        var messageText = ConfirmationPromptRenderer.Render(
            promptContext,
            MessageTemplateNaturalKeys.PromptDefault,
            await _messageTemplates.RequireAsync(
                MessageKinds.ConfirmationPrompt,
                MessageSurfaces.InChannel,
                ChannelTypes.WhatsApp,
                promptContext.Ownership,
                cancellationToken),
            _promptLocalizer,
            _logger);

        var transactionIdString = transactionId.ToString();
        var confirmReplyId = CallbackDataPrefixes.Confirm + transactionIdString;
        var declineReplyId = CallbackDataPrefixes.Decline + transactionIdString;

        var recipientDigits = StripE164Prefix(normalizedPhoneResult);

        // The button captions are Natural Keys localized into the recipient's language (ICC-050);
        // a missing translation falls back to the key's base text. The recipient locale comes from the
        // transaction fallback of the chain (WhatsApp itself does not supply a snapshot locale).
        var confirmButtonText = _promptLocalizer.ResolveOrBaseText(
            WhatsAppAdapterConstants.ConfirmButtonText, recipientLocale);
        var declineButtonText = _promptLocalizer.ResolveOrBaseText(
            WhatsAppAdapterConstants.DeclineButtonText, recipientLocale);

        var sent = await _provider.SendInteractiveButtonsMessageAsync(
            recipientDigits,
            messageText,
            confirmReplyId,
            confirmButtonText,
            declineReplyId,
            declineButtonText,
            cancellationToken);

        if (!sent)
        {
            return Result<ChannelMessageRef?>.Failure(
                ChannelAdapterErrorCodes.MessageSendFailed,
                "Error sending the WhatsApp message.");
        }

        // The Cloud API gives no addressable handle for editing a delivered message, so the prompt
        // is delivered but not referenceable — a success without a reference, never a failure.
        return Result<ChannelMessageRef?>.Success(null);
    }

    /// <inheritdoc />
    public Task<Result<ChannelMessageRef?>> SendMessageAsync(
        ChannelMessage message,
        CancellationToken cancellationToken = default)
        => SendTextAsync(message.ChannelUserId, message.Text, cancellationToken);

    /// <summary>
    /// Sends a plain text message to the user via the provider.
    /// </summary>
    /// <param name="channelUserId">Recipient within the channel.</param>
    /// <param name="text">Message text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success without a reference (WhatsApp has no addressable edit), or a failure.</returns>
    private async Task<Result<ChannelMessageRef?>> SendTextAsync(
        string channelUserId,
        string text,
        CancellationToken cancellationToken)
    {
        var normalizedPhoneResult = TryNormalizePhoneToE164(channelUserId);
        if (normalizedPhoneResult is null)
        {
            return Result<ChannelMessageRef?>.Failure(
                ChannelAdapterErrorCodes.MessageSendFailed,
                "Invalid WhatsApp channel user identifier.");
        }

        var recipientDigits = StripE164Prefix(normalizedPhoneResult);

        var sent = await _provider.SendTextMessageAsync(recipientDigits, text, cancellationToken);
        if (!sent)
        {
            return Result<ChannelMessageRef?>.Failure(
                ChannelAdapterErrorCodes.MessageSendFailed,
                "Error sending the WhatsApp message.");
        }

        return Result<ChannelMessageRef?>.Success(null);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ValidateWebhookAsync(
        ChannelInboundRequest request,
        CancellationToken cancellationToken = default)
    {
        // The method validates the GET Hub Challenge and the POST webhook signature.
        // Hub Challenge and signature are specific to the Meta Cloud API. Another provider's webhook is
        // invoked with a different signature model — validation is extended for other providers as needed.
        // The envelope itself is already buffered; what is awaited is the credential resolution, which
        // both checks need — the verify token and the app secret are tenant-credential fields.
        if (string.Equals(request.Method, HttpMethods.Get, StringComparison.Ordinal))
        {
            return await ValidateWebhookChallengeRequestAsync(request, cancellationToken);
        }

        if (!string.Equals(request.Method, HttpMethods.Post, StringComparison.Ordinal))
        {
            return Result<bool>.Success(false);
        }

        // Signature validation depends on the provider — we use _provider.ProviderType,
        // not the configured Provider option, to avoid desync with a custom DI registration.
        if (string.Equals(_provider.ProviderType, WhatsAppProviderNames.MetaCloudApi, StringComparison.Ordinal))
        {
            return await ValidateMetaWebhookSignatureAsync(request, cancellationToken);
        }

        // Only the Meta Cloud API provider is implemented; its webhooks are validated above.
        // Any other provider type is unsupported, so its webhook is rejected.
        _logger.LogWarning(
            "Webhook validation for provider {ProviderType} is not implemented yet. Webhook rejected.",
            _provider.ProviderType);

        return Result<bool>.Success(false);
    }

    /// <inheritdoc />
    public async Task<Result<ChannelHealthStatus>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        // The method delegates the health check to the provider.
        var startedAt = _timeProvider.GetUtcNow();

        var (isHealthy, details) = await _provider.CheckHealthAsync(cancellationToken);
        var elapsed = _timeProvider.GetUtcNow() - startedAt;

        return Result<ChannelHealthStatus>.Success(new ChannelHealthStatus(
            IsHealthy: isHealthy,
            ChannelType: ChannelTypes.WhatsApp,
            ResponseTime: elapsed,
            Details: details,
            CheckedAt: _timeProvider.GetUtcNow()));
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetDeepLinkAsync(
        TransactionId transactionId,
        CancellationToken cancellationToken = default)
    {
        // The method builds a WhatsApp click-to-chat deep link to start the auth flow.
        // The deep link is the same for all providers — it is addressed to WhatsApp directly.
        // One credential resolve per operation (CA-171): the business number is a tenant-credential field.
        var credentialsResult = await WhatsAppCredentialsResolver.ResolveAsync(_resolver, _logger, cancellationToken);
        if (credentialsResult.IsFailure)
        {
            _logger.LogError("WhatsApp credentials are unavailable for the deep link.");
            return Result<string>.Failure(
                ChannelAdapterErrorCodes.DeepLinkGenerationFailed,
                "WhatsApp credentials are unavailable for the deep link.");
        }

        var phoneDigits = TryNormalizeBusinessPhoneToDeepLinkDigits(credentialsResult.Value.BusinessPhoneNumber);
        if (phoneDigits is null)
        {
            return Result<string>.Failure(
                ChannelAdapterErrorCodes.DeepLinkGenerationFailed,
                "BusinessPhoneNumber is misconfigured for the WhatsApp deep link.");
        }

        // Render the formatted prefill text (SPEC-003 §10.4): a title with the application name (best-effort),
        // a one-line instruction, and the sign-in code on its own last line. The application name and the
        // page language come from the seam (which by contract never throws — the override provider is used
        // the same way); a null context degrades to the minimal variant in the base language.
        var prefillText = await RenderPrefillTextAsync(transactionId, cancellationToken);

        var encodedText = Uri.EscapeDataString(prefillText);
        var deepLink = string.Concat(
            WhatsAppAdapterConstants.DeepLinkBaseUrl,
            phoneDigits,
            "?text=",
            encodedText);

        return Result<string>.Success(deepLink);
    }

    /// <summary>
    /// Renders the formatted deep-link prefill text (SPEC-003 §10.4).
    /// </summary>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The multi-line prefill text (never null; at minimum the application-name-less variant).</returns>
    private async Task<string> RenderPrefillTextAsync(TransactionId transactionId, CancellationToken cancellationToken)
    {
        // The code is the guaranteed slot — it is the sign-in payload and is always present. The parser on
        // the inbound side extracts it from anywhere in the message text (see TryExtractAuthTransactionId).
        var code = string.Concat(WhatsAppAdapterConstants.DeepLinkPrefix, transactionId.ToString());

        var prefillContext = await _prefillContextReader.ReadAsync(
            transactionId, ChannelTypes.WhatsApp, cancellationToken);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [SlotNames.Code] = code,
        };

        // {app} present → the full variant with the title line; absent → the minimal variant (TPL-031).
        var applicationName = prefillContext?.ApplicationName;
        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            values[SlotNames.App] = applicationName;
        }

        var prefill = MessageTextRenderer.Render(
            await _messageTemplates.RequireAsync(
                MessageKinds.DeeplinkPrefill,
                MessageSurfaces.InChannel,
                ChannelTypes.WhatsApp,
                // The wording is resolved over the ownership of the very transaction this deep link
                // is built for (SPEC-036 TPL-116); an unreadable transaction leaves the core level as
                // the only one there is, which is also where the locale of the prefill degrades to.
                prefillContext?.Ownership ?? ResolutionContext.Core,
                cancellationToken),
            values,
            prefillContext?.UiLocale,
            prefillContext?.UiTimeZone,
            _promptLocalizer,
            MessageRenderMode.PlainText,
            _logger);

        if (prefill is null)
        {
            // A prefilled message this channel can send is plain text and nothing else, so a ladder none
            // of whose steps states the plain-text edition leaves it without wording. The code still
            // travels — it is the sign-in payload and the inbound parser finds it anywhere in the text —
            // but the deployment is told, because the wording it wrote reaches nobody.
            _logger.LogError(
                "No step of the {MessageKind} ladder of channel {ChannelType} states the plain-text "
                + "edition; the prefilled message carries the sign-in code alone.",
                MessageKinds.DeeplinkPrefill,
                ChannelTypes.WhatsApp);
        }

        return prefill ?? code;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ReportOutcomeAsync(
        TransactionOutcomeNotice notice,
        CancellationToken cancellationToken = default)
    {
        // WhatsApp has neither callback answers nor an editable delivered message: the only way to
        // show a terminal state is a new message. The status text is already localized by the core,
        // so an unknown future outcome value changes nothing here. A ReplacePrompt intent therefore
        // goes unhonoured by this channel as it ships today — there is nothing delivered to replace —
        // and that is not a refusal: the receipt still reaches the user and the result is unchanged.
        var sendResult = await SendTextAsync(notice.ChannelUserId, notice.StatusText, cancellationToken);

        return sendResult.IsSuccess
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(sendResult.Error);
    }

    /// <summary>
    /// Validates the GET Hub Challenge request (Meta Cloud API).
    /// </summary>
    /// <param name="request">HTTP request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    private async Task<Result<bool>> ValidateWebhookChallengeRequestAsync(
        ChannelInboundRequest request,
        CancellationToken cancellationToken)
    {
        // The method checks the hub.mode and hub.verify_token parameters.
        request.Query.TryGetValue(WhatsAppAdapterConstants.HubModeQueryKey, out var mode);
        request.Query.TryGetValue(WhatsAppAdapterConstants.HubVerifyTokenQueryKey, out var verifyToken);

        if (!string.Equals(
                mode,
                WhatsAppAdapterConstants.HubSubscribeMode,
                StringComparison.Ordinal))
        {
            return Result<bool>.Success(false);
        }

        // One credential resolve per operation (CA-171): the verify token is a tenant-credential field.
        var credentialsResult = await WhatsAppCredentialsResolver.ResolveAsync(_resolver, _logger, cancellationToken);
        if (credentialsResult.IsFailure)
        {
            _logger.LogError("WhatsApp credentials are unavailable for webhook challenge validation (Meta Cloud API).");
            return Result<bool>.Success(false);
        }

        var configuredToken = credentialsResult.Value.MetaCloudApi.WebhookVerifyToken;
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            _logger.LogError("WebhookVerifyToken is not configured for the WhatsApp adapter (Meta Cloud API).");
            return Result<bool>.Success(false);
        }

        // Compare fixed-length SHA-256 hashes (32 bytes) via FixedTimeEquals (CA-033):
        // the comparison is always constant-length, which eliminates the verify-token length leak through an early
        // return on length mismatch and preserves constant time.
        Span<byte> configuredTokenHash = stackalloc byte[32];
        Span<byte> incomingTokenHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken), configuredTokenHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(verifyToken ?? string.Empty), incomingTokenHash);

        var isValid = CryptographicOperations.FixedTimeEquals(
            configuredTokenHash,
            incomingTokenHash);
        return Result<bool>.Success(isValid);
    }

    /// <summary>
    /// Validates the POST webhook request signature (Meta Cloud API, X-Hub-Signature-256).
    /// </summary>
    /// <param name="request">Neutral inbound request envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    private async Task<Result<bool>> ValidateMetaWebhookSignatureAsync(
        ChannelInboundRequest request,
        CancellationToken cancellationToken)
    {
        // The method verifies the HMAC-SHA256 signature of the request body.
        try
        {
            request.Headers.TryGetValue(WhatsAppAdapterConstants.SignatureHeader, out var signatureHeader);
            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                _logger.LogWarning(
                    "WhatsApp webhook rejected: header {SignatureHeader} is missing.",
                    WhatsAppAdapterConstants.SignatureHeader);
                return Result<bool>.Success(false);
            }

            if (!signatureHeader.StartsWith(
                    WhatsAppAdapterConstants.SignaturePrefix,
                    StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "WhatsApp webhook rejected: the signature does not contain the {Prefix} prefix.",
                    WhatsAppAdapterConstants.SignaturePrefix);
                return Result<bool>.Success(false);
            }

            // One credential resolve per operation (CA-171): the app secret is a tenant-credential field.
            var credentialsResult = await WhatsAppCredentialsResolver.ResolveAsync(_resolver, _logger, cancellationToken);
            if (credentialsResult.IsFailure)
            {
                _logger.LogError("WhatsApp webhook rejected: credentials are unavailable.");
                return Result<bool>.Success(false);
            }

            var appSecret = credentialsResult.Value.MetaCloudApi.AppSecret;
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                _logger.LogError("WhatsApp webhook rejected: AppSecret is not configured.");
                return Result<bool>.Success(false);
            }

            // The envelope carries the body byte for byte, so the HMAC is computed over exactly what
            // arrived — no decoding step in between that could swallow a byte-order mark.
            var bodyBytes = request.Body.Span;
            var expectedSignatureHex = signatureHeader[WhatsAppAdapterConstants.SignaturePrefix.Length..]
                .ToLowerInvariant();
            var computedHash = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(appSecret),
                bodyBytes);

            var computedSignatureHex = Convert.ToHexStringLower(computedHash);

            // Compare fixed-length SHA-256 hashes (32 bytes) via FixedTimeEquals (CA-033):
            // comparing HMAC signatures is always constant-length, which eliminates the length leak through an early
            // return on length mismatch and preserves constant time.
            Span<byte> expectedSignatureHash = stackalloc byte[32];
            Span<byte> computedSignatureHash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(expectedSignatureHex), expectedSignatureHash);
            SHA256.HashData(Encoding.UTF8.GetBytes(computedSignatureHex), computedSignatureHash);

            var isValid = CryptographicOperations.FixedTimeEquals(
                expectedSignatureHash,
                computedSignatureHash);

            if (!isValid)
            {
                _logger.LogWarning("WhatsApp webhook rejected: invalid signature.");
            }

            return Result<bool>.Success(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp webhook validation error.");
            return Result<bool>.Failure(
                ChannelAdapterErrorCodes.WebhookValidationFailed,
                "WhatsApp webhook validation error.");
        }
    }

    /// <summary>
    /// Maps an inbound WhatsApp message to a <see cref="ChannelInboundResult"/>.
    /// </summary>
    /// <param name="message">Inbound webhook message.</param>
    /// <param name="contacts">List of contacts for profile lookup.</param>
    /// <param name="payload">Full webhook payload.</param>
    /// <returns>The message processing result, or null to skip.</returns>
    private Result<ChannelInboundResult>? TryMapMessageToInboundResult(
        WhatsAppWebhookMessage message,
        IReadOnlyList<WhatsAppWebhookContact>? contacts,
        WhatsAppWebhookPayload payload)
    {
        // The method determines the intent by the message type and the button/text payload.
        var normalizedPhone = TryNormalizePhoneToE164(message.From);
        if (normalizedPhone is null)
        {
            return Result<ChannelInboundResult>.Failure(
                ChannelAdapterErrorCodes.IdentityExtractionFailed,
                "Invalid sender number in the WhatsApp message.");
        }

        var displayName = FindDisplayName(contacts, normalizedPhone) ?? normalizedPhone;
        var snapshot = BuildIdentitySnapshot(normalizedPhone, displayName, message, _timeProvider.GetUtcNow());

        var messageType = message.Type ?? string.Empty;
        if (string.Equals(
                messageType,
                WhatsAppAdapterConstants.InboundTypeText,
                StringComparison.OrdinalIgnoreCase))
        {
            var text = message.Text?.Body?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            // The sign-in code may now sit anywhere in a formatted prefill message (SPEC-003 §10.4), not
            // only at the start — so we scan for the auth_ token instead of matching the whole text.
            // The bare-payload format (text == "auth_{id}") stays a special case: backward compatible.
            var transactionId = TryExtractAuthTransactionId(text);
            if (transactionId is null)
            {
                return null;
            }

            return Result<ChannelInboundResult>.Success(new ChannelAuthStartResult
            {
                TransactionId = transactionId.Value,
                Identity = snapshot,
                RawEvent = payload
            });
        }

        var callbackPayload = TryExtractCallbackPayload(message);
        if (string.IsNullOrWhiteSpace(callbackPayload))
        {
            return null;
        }

        if (callbackPayload.StartsWith(CallbackDataPrefixes.Confirm, StringComparison.Ordinal))
        {
            var rawTransactionId = callbackPayload[CallbackDataPrefixes.Confirm.Length..];
            if (!TryParseEventTransactionId(rawTransactionId, out var transactionId))
            {
                return null;
            }

            return Result<ChannelInboundResult>.Success(new ChannelAuthConfirmResult
            {
                TransactionId = transactionId,
                Identity = snapshot,
                RawEvent = payload
            });
        }

        if (callbackPayload.StartsWith(CallbackDataPrefixes.Decline, StringComparison.Ordinal))
        {
            var rawTransactionId = callbackPayload[CallbackDataPrefixes.Decline.Length..];
            if (!TryParseEventTransactionId(rawTransactionId, out var transactionId))
            {
                return null;
            }

            return Result<ChannelInboundResult>.Success(new ChannelAuthDeclineResult
            {
                TransactionId = transactionId,
                Identity = snapshot,
                RawEvent = payload
            });
        }

        return null;
    }

    /// <summary>
    /// Parses the transaction identifier carried by a channel event. An identifier that does not
    /// parse means the event is not ours: the caller answers with an irrelevant-event result instead
    /// of handing a broken value to the core (SPEC-003 §6.3).
    /// </summary>
    /// <param name="rawTransactionId">Raw identifier as it arrived in the event.</param>
    /// <param name="transactionId">Parsed identifier on success.</param>
    /// <returns>true — the identifier parsed and the intent can be constructed.</returns>
    private bool TryParseEventTransactionId(
        string? rawTransactionId,
        out TransactionId transactionId)
    {
        if (TransactionId.TryParse(rawTransactionId, out transactionId))
        {
            return true;
        }

        _logger.LogWarning(
            "Unparseable transaction identifier in a {ChannelType} event: {RawId}. "
            + "The event is treated as irrelevant",
            ChannelTypes.WhatsApp,
            rawTransactionId);

        return false;
    }

    /// <summary>
    /// Extracts the sign-in transaction id from an inbound text message: finds the <c>auth_</c> token
    /// followed by a valid fixed-length Base62 transaction id anywhere in the text (SPEC-003 §10.4).
    /// </summary>
    /// <remarks>
    /// The formatted prefill (SPEC-003 §10.4) puts the code on its own line, no longer at the very start,
    /// so a <c>StartsWith</c> match would break it. The id length and alphabet are fixed
    /// (<see cref="TransactionId.TryParse(string?, out TransactionId)"/>), which rules out false positives from surrounding text. The
    /// legacy bare-payload text (<c>auth_{id}</c> only) is matched by the same scan — backward compatible.
    /// </remarks>
    /// <param name="text">Inbound message text (already trimmed).</param>
    /// <returns>The transaction id, or null when no valid token is present.</returns>
    private static TransactionId? TryExtractAuthTransactionId(string text)
    {
        var prefix = WhatsAppAdapterConstants.DeepLinkPrefix;
        var searchStart = 0;

        while (true)
        {
            var prefixIndex = text.IndexOf(prefix, searchStart, StringComparison.Ordinal);
            if (prefixIndex < 0)
            {
                return null;
            }

            var idStart = prefixIndex + prefix.Length;
            if (idStart + TransactionId.StringLength <= text.Length)
            {
                var candidate = text.Substring(idStart, TransactionId.StringLength);
                if (TransactionId.TryParse(candidate, out var parsed))
                {
                    return parsed;
                }
            }

            // Overlapping occurrences are possible (e.g. "auth_auth_…"); advance by one to not miss them.
            searchStart = prefixIndex + 1;
        }
    }

    /// <summary>
    /// Extracts the callback payload from an interactive or button message.
    /// </summary>
    /// <param name="message">Inbound webhook message.</param>
    /// <returns>The button payload, or null.</returns>
    private static string? TryExtractCallbackPayload(WhatsAppWebhookMessage message)
    {
        // The method extracts the payload from the various WhatsApp button message formats.
        if (message.Interactive?.ButtonReply?.Id is not null)
        {
            return message.Interactive.ButtonReply.Id;
        }

        if (message.Button?.Payload is not null)
        {
            return message.Button.Payload;
        }

        return null;
    }

    /// <summary>
    /// Builds an identity snapshot for a WhatsApp user.
    /// </summary>
    /// <param name="channelUserId">User's number in E.164.</param>
    /// <param name="displayName">Display name.</param>
    /// <param name="message">Source webhook message.</param>
    /// <param name="capturedAt">Capture moment (UTC), supplied by the caller's time provider.</param>
    /// <returns>Channel identity snapshot.</returns>
    private static ChannelIdentitySnapshot BuildIdentitySnapshot(
        string channelUserId,
        string displayName,
        WhatsAppWebhookMessage message,
        DateTimeOffset capturedAt)
    {
        // The method builds a snapshot with a mandatory phone_number for WhatsApp.
        var rawMetadata = BuildRawMetadata(message);

        return new ChannelIdentitySnapshot
        {
            // Tenant of the inbound request, taken from the ambient scope the host opened for it —
            // the {tenant} segment of the webhook route or the polling iteration of that tenant.
            // The identity of a channel user belongs to the tenant whose channel received the event,
            // so the scope is the authoritative source here and the transaction is not consulted.
            TenantId = ChannelTenantContext.CurrentTenantId,
            ChannelType = ChannelTypes.WhatsApp,
            IsBot = false,
            ChannelUserId = channelUserId,
            DisplayName = displayName,
            FirstName = displayName,
            PhoneNumber = channelUserId,
            CapturedAt = capturedAt,
            AdapterVersion = WhatsAppAdapterConstants.AdapterVersion,
            RawMetadata = rawMetadata
        };
    }

    /// <summary>
    /// Builds raw metadata with a size limit.
    /// </summary>
    /// <param name="message">Source webhook message.</param>
    /// <returns>JSON metadata, or null if the limit is exceeded.</returns>
    private static JsonElement? BuildRawMetadata(WhatsAppWebhookMessage message)
    {
        // The method stores the message's minimal technical fields without PII secrets.
        var metadata = new
        {
            whatsapp_message_id = message.Id,
            message_type = message.Type,
            timestamp = message.Timestamp
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
        if (bytes.Length > WhatsAppAdapterConstants.MaxRawMetadataSize)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(metadata);
    }

    /// <summary>
    /// Normalizes a phone number to E.164 format with a leading <c>+</c>.
    /// </summary>
    /// <param name="rawPhone">Source number.</param>
    /// <returns>The normalized number, or null if the format is invalid.</returns>
    private static string? TryNormalizePhoneToE164(string? rawPhone)
    {
        // The method strips spaces/separators from the number and converts it to the +{digits} format.
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return null;
        }

        var trimmed = rawPhone.Trim();

        // SPEC-003: channel_user_id cannot contain the ':' character.
        // We check the source string before filtering characters.
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        var builder = new StringBuilder(trimmed.Length);

        foreach (var ch in trimmed)
        {
            if (char.IsDigit(ch))
            {
                builder.Append(ch);
            }
        }

        if (builder.Length is < 8 or > 15)
        {
            return null;
        }

        return "+" + builder;
    }

    /// <summary>
    /// Removes the leading <c>+</c> from an E.164 number for passing to the provider API.
    /// </summary>
    /// <param name="e164Phone">Number in E.164 format with a leading <c>+</c>.</param>
    /// <returns>The digits-only number.</returns>
    private static string StripE164Prefix(string e164Phone) => e164Phone.TrimStart('+');

    /// <summary>
    /// Normalizes a business number for a deep link of the <c>wa.me/{digits}</c> format.
    /// </summary>
    /// <param name="rawPhone">Source number.</param>
    /// <returns>The digits-only number, or null if the format is invalid.</returns>
    private static string? TryNormalizeBusinessPhoneToDeepLinkDigits(string? rawPhone)
    {
        // The method converts the number to a digits-only form for use in wa.me.
        var e164 = TryNormalizePhoneToE164(rawPhone);
        if (e164 is null)
        {
            return null;
        }

        var digits = e164.TrimStart('+');
        return digits.Length is < 8 or > 15 ? null : digits;
    }

    /// <summary>
    /// Looks up the user's display name from the contact list.
    /// </summary>
    /// <param name="contacts">Contacts from the webhook.</param>
    /// <param name="normalizedPhone">User's number in E.164.</param>
    /// <returns>The profile name, or null.</returns>
    private static string? FindDisplayName(
        IReadOnlyList<WhatsAppWebhookContact>? contacts,
        string normalizedPhone)
    {
        // The method matches a contact by phone number and returns profile.name.
        if (contacts is null || contacts.Count is 0)
        {
            return null;
        }

        foreach (var contact in contacts)
        {
            var contactPhone = TryNormalizePhoneToE164(contact.WaId);
            if (contactPhone is null)
            {
                continue;
            }

            if (!string.Equals(contactPhone, normalizedPhone, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(contact.Profile?.Name))
            {
                return null;
            }

            return contact.Profile.Name.Trim();
        }

        return null;
    }
}
