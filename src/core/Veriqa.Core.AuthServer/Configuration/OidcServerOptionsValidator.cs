// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// OIDC server options validator.
/// Checks that time intervals are correct, the Issuer URI format is valid, and certificate Base64 is valid.
/// </summary>
public sealed class OidcServerOptionsValidator : IValidateOptions<OidcServerOptions>
{
    /// <summary>
    /// Longest delay, in milliseconds, a timer accepts (<c>Task.Delay</c>, <c>ITimer.Change</c>).
    /// </summary>
    private const long MaxTimerDelayMilliseconds = uint.MaxValue - 1;

    /// <summary>
    /// Longest prune interval, in whole seconds, the timer of the prune pass can wait (about 49.7 days).
    /// </summary>
    private const int MaxTokenPruneIntervalSeconds = (int)(MaxTimerDelayMilliseconds / TimeSpan.MillisecondsPerSecond);

    /// <summary>
    /// Validates the OIDC server options.
    /// </summary>
    /// <param name="name">Options instance name.</param>
    /// <param name="options">Options instance to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, OidcServerOptions options)
    {
        // Check that all time intervals are positive and the Issuer is valid
        var failures = new List<string>();

        if (options.AccessTokenLifetimeSeconds <= 0)
        {
            failures.Add("AccessTokenLifetimeSeconds must be greater than 0.");
        }

        if (options.RefreshTokenLifetimeSeconds <= 0)
        {
            failures.Add("RefreshTokenLifetimeSeconds must be greater than 0.");
        }

        if (options.AuthorizationCodeLifetimeSeconds <= 0)
        {
            failures.Add("AuthorizationCodeLifetimeSeconds must be greater than 0.");
        }

        if (options.TokenPruneIntervalSeconds <= 0)
        {
            failures.Add("TokenPruneIntervalSeconds must be greater than 0.");
        }
        else if (options.TokenPruneIntervalSeconds > MaxTokenPruneIntervalSeconds)
        {
            // The prune pass waits on a timer, and a longer wait is refused by the timer on every pass
            // rather than once: the start is stopped instead of a service that never prunes.
            failures.Add($"TokenPruneIntervalSeconds must not exceed {MaxTokenPruneIntervalSeconds}.");
        }

        if (options.TokenPruneThresholdSeconds <= 0)
        {
            failures.Add("TokenPruneThresholdSeconds must be greater than 0.");
        }

        // Issuer URI validation (if specified — must be a valid absolute URI)
        if (!string.IsNullOrEmpty(options.Issuer)
            && !Uri.TryCreate(options.Issuer, UriKind.Absolute, out _))
        {
            failures.Add(
                $"Issuer '{options.Issuer}' is not a valid absolute URI. "
                + "Specify a correct URI (for example, https://auth.example.com) or leave it empty for auto-detection.");
        }

        // Fail-fast validation of the signing certificate Base64: a configuration error is diagnosed
        // at application startup rather than at runtime on the first token request (FormatException).
        if (!string.IsNullOrEmpty(options.SigningCertificateBase64)
            && !IsValidBase64(options.SigningCertificateBase64))
        {
            failures.Add(
                "Veriqa:OpenIddict:Server:SigningCertificateBase64 contains invalid Base64. "
                + "Make sure the value is a correct Base64 string (PFX/PKCS12).");
        }

        // Fail-fast validation of the encryption certificate Base64
        if (!string.IsNullOrEmpty(options.EncryptionCertificateBase64)
            && !IsValidBase64(options.EncryptionCertificateBase64))
        {
            failures.Add(
                "Veriqa:OpenIddict:Server:EncryptionCertificateBase64 contains invalid Base64. "
                + "Make sure the value is a correct Base64 string (PFX/PKCS12).");
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Checks whether the string is valid Base64 (without allocating a byte array).
    /// </summary>
    /// <param name="value">String to check.</param>
    /// <returns><c>true</c> — the string is valid Base64.</returns>
    private static bool IsValidBase64(string value)
    {
        // Convert.TryFromBase64String requires Span<byte> — use stackalloc for small strings,
        // otherwise a temporary array (Base64 check is not a hot path, runs once at startup).
        var maxBytes = (value.Length / 4) * 3 + 3;
        Span<byte> buffer = maxBytes <= 1024
            ? stackalloc byte[maxBytes]
            : new byte[maxBytes];

        return Convert.TryFromBase64String(value, buffer, out _);
    }
}
