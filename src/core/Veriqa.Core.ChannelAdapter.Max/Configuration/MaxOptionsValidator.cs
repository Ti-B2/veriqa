// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.Max.Enums;

namespace Veriqa.Core.ChannelAdapter.Max.Configuration;

/// <summary>
/// MAX adapter settings validator.
/// Checks the mandatory fields when the adapter is enabled.
/// </summary>
public sealed class MaxOptionsValidator : IValidateOptions<MaxOptions>
{
    /// <summary>
    /// Address of the update mode inside the host configuration, as an operator reads it in a refusal.
    /// </summary>
    private static readonly string UpdateModePath =
        MaxOptions.SectionName + ":" + nameof(MaxOptions.UpdateMode);

    /// <summary>
    /// Validates the MAX adapter settings.
    /// </summary>
    /// <param name="name">Options instance name.</param>
    /// <param name="options">Settings to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, MaxOptions options)
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
                + $"Allowed values: {string.Join(", ", Enum.GetNames<MaxUpdateMode>())}.");
        }

        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            return ValidateOptionsResult.Fail(
                "BotToken is required when the MAX adapter is enabled.");
        }

        if (options.UpdateMode is MaxUpdateMode.Webhook)
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

            // SPEC-003 §11.3: WebhookSecretToken is mandatory in the Webhook mode for security
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
    /// Validates the webhook base URL: an absolute URI with the HTTPS scheme.
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

        // The MAX Bot API requires HTTPS for the webhook
        if (!string.Equals(webhookBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "WebhookBaseUrl must use HTTPS (a MAX Bot API requirement).");
        }

        return ValidateOptionsResult.Success;
    }
}
