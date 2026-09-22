// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.WhatsApp.Constants;

namespace Veriqa.Core.ChannelAdapter.WhatsApp.Configuration;

/// <summary>
/// WhatsApp adapter settings validator.
/// Validation depends on the selected provider.
/// </summary>
public sealed class WhatsAppOptionsValidator : IValidateOptions<WhatsAppOptions>
{
    /// <summary>
    /// Validates the WhatsApp adapter settings.
    /// </summary>
    /// <param name="name">Options instance name.</param>
    /// <param name="options">Settings to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
    {
        // If the adapter is disabled, strict validation is not required.
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.BusinessPhoneNumber))
        {
            return ValidateOptionsResult.Fail(
                "BusinessPhoneNumber is required when the WhatsApp adapter is enabled.");
        }

        // Provider-specific validation exists only for the shipped provider: the settings of a provider
        // supplied by the host are unknown here. A value naming no registered provider is rejected at
        // startup by the provider match check, not by this validator.
        if (string.Equals(options.Provider, WhatsAppProviderNames.MetaCloudApi, StringComparison.Ordinal))
        {
            return ValidateMetaCloudApi(options.MetaCloudApi);
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Validates the Meta Cloud API provider settings.
    /// </summary>
    /// <param name="meta">Meta Cloud API settings.</param>
    /// <returns>Validation result.</returns>
    private static ValidateOptionsResult ValidateMetaCloudApi(MetaCloudApiOptions meta)
    {
        // The method checks the mandatory Meta Cloud API fields.
        if (string.IsNullOrWhiteSpace(meta.PhoneNumberId))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.PhoneNumberId is required for the MetaCloudApi provider.");
        }

        if (string.IsNullOrWhiteSpace(meta.AccessToken))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.AccessToken is required for the MetaCloudApi provider.");
        }

        if (string.IsNullOrWhiteSpace(meta.AppSecret))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.AppSecret is required for the MetaCloudApi provider.");
        }

        if (string.IsNullOrWhiteSpace(meta.WebhookVerifyToken))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.WebhookVerifyToken is required for the MetaCloudApi provider.");
        }

        var graphBaseUrlValidation = ValidateGraphApiBaseUrl(meta.GraphApiBaseUrl);
        if (graphBaseUrlValidation.Failed)
        {
            return graphBaseUrlValidation;
        }

        if (string.IsNullOrWhiteSpace(meta.GraphApiVersion))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.GraphApiVersion is required for the MetaCloudApi provider.");
        }

        if (!meta.GraphApiVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.GraphApiVersion must start with the 'v' prefix (for example, v21.0).");
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Validates the Graph API base URL.
    /// </summary>
    /// <param name="graphApiBaseUrl">Graph API base URL.</param>
    /// <returns>Validation result.</returns>
    private static ValidateOptionsResult ValidateGraphApiBaseUrl(string graphApiBaseUrl)
    {
        // Check that the URL is absolute.
        if (!Uri.TryCreate(graphApiBaseUrl, UriKind.Absolute, out var graphApiUri))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.GraphApiBaseUrl must be a correct absolute URL.");
        }

        // Only HTTPS is used for the external API.
        if (!string.Equals(graphApiUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "MetaCloudApi.GraphApiBaseUrl must use HTTPS.");
        }

        return ValidateOptionsResult.Success;
    }
}
