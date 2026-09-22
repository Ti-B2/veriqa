// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.Telegram.Enums;

namespace Veriqa.Core.ChannelAdapter.Telegram.Configuration;

/// <summary>
/// Validator of Telegram adapter settings.
/// Checks the required fields when the adapter is enabled.
/// </summary>
public sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    /// <summary>
    /// Address of the update mode inside the host configuration, as an operator reads it in a refusal.
    /// </summary>
    private static readonly string UpdateModePath =
        TelegramOptions.SectionName + ":" + nameof(TelegramOptions.UpdateMode);

    /// <summary>
    /// Validates the Telegram adapter settings.
    /// </summary>
    /// <param name="name">Options instance name.</param>
    /// <param name="options">Settings to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        // If the adapter is disabled, no validation is required
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        // The mode is judged by MEMBERSHIP and not by the binder alone: an unknown NAME never reaches
        // this object — the binder stops the start on it — while a raw NUMBER is converted into a
        // member that does not exist and would otherwise travel on unnamed. Both spellings of the same
        // mistake end the same way (SPEC-012 §8.2).
        if (!Enum.IsDefined(options.UpdateMode))
        {
            return ValidateOptionsResult.Fail(
                $"{UpdateModePath} is '{options.UpdateMode}', which is not a known update mode. "
                + $"Allowed values: {string.Join(", ", Enum.GetNames<TelegramUpdateMode>())}.");
        }

        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            return ValidateOptionsResult.Fail(
                "BotToken is required when the Telegram adapter is enabled.");
        }

        if (options.UpdateMode is TelegramUpdateMode.Webhook)
        {
            if (string.IsNullOrWhiteSpace(options.WebhookBaseUrl))
            {
                return ValidateOptionsResult.Fail(
                    "WebhookBaseUrl is required for the Webhook update mode.");
            }

            var webhookValidationResult = ValidateWebhookBaseUrl(options.WebhookBaseUrl);
            if (webhookValidationResult.Failed)
            {
                return webhookValidationResult;
            }

            // SPEC-003 §4.3: WebhookSecretToken is required in the Webhook mode
            if (string.IsNullOrWhiteSpace(options.WebhookSecretToken))
            {
                return ValidateOptionsResult.Fail(
                    "WebhookSecretToken is required for the Webhook update mode; " +
                    "configure it to enable inbound webhook signature validation.");
            }
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Validates the webhook base URL: absolute URI, HTTPS scheme.
    /// </summary>
    /// <param name="webhookBaseUrl">Webhook base URL.</param>
    /// <returns>Validation result.</returns>
    private static ValidateOptionsResult ValidateWebhookBaseUrl(string webhookBaseUrl)
    {
        // Check that the URL is an absolute HTTPS address
        if (!Uri.TryCreate(webhookBaseUrl, UriKind.Absolute, out var webhookBaseUri))
        {
            return ValidateOptionsResult.Fail(
                "WebhookBaseUrl must be a correct absolute URL.");
        }

        // The Telegram Bot API requires HTTPS for the webhook
        if (!string.Equals(webhookBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "WebhookBaseUrl must use HTTPS (a Telegram Bot API requirement).");
        }

        return ValidateOptionsResult.Success;
    }
}
