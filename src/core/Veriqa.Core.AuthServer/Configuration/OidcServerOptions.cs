// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Security.Cryptography.X509Certificates;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// OIDC server parameters (Veriqa:OpenIddict:Server configuration section).
/// </summary>
public sealed class OidcServerOptions
{
    /// <summary>
    /// Name of the configuration section.
    /// </summary>
    public const string SectionName = "Veriqa:OpenIddict:Server";

    /// <summary>
    /// Explicit Issuer URI. If null, it is determined automatically from the server URL.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Access token lifetime in seconds. Default: 3600 (1 hour).
    /// </summary>
    public int AccessTokenLifetimeSeconds { get; set; } = 3600;

    /// <summary>
    /// Refresh token lifetime in seconds. Default: 1209600 (14 days).
    /// </summary>
    public int RefreshTokenLifetimeSeconds { get; set; } = 1_209_600;

    /// <summary>
    /// Authorization code lifetime in seconds. Default: 300 (5 minutes).
    /// </summary>
    public int AuthorizationCodeLifetimeSeconds { get; set; } = 300;

    /// <summary>
    /// Whether refresh token rotation (rolling refresh tokens) is enabled.
    /// Default: true (required by SPEC-002 §7.2).
    /// When false, rolling refresh tokens are disabled (DisableRollingRefreshTokens).
    /// </summary>
    public bool EnableRefreshTokenRotation { get; set; } = true;

    /// <summary>
    /// Whether the Revocation Endpoint (RFC 7009, /connect/revoke) is enabled.
    /// Default: true.
    /// </summary>
    public bool EnableRevocation { get; set; } = true;

    /// <summary>
    /// Period, in seconds, of the background pass that prunes the OpenIddict token and authorization
    /// records. Default: 3600 (1 hour). Must be greater than 0 and not exceed 4294967 (about 49.7 days,
    /// the longest wait a timer accepts).
    /// </summary>
    public int TokenPruneIntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// Minimum age, in seconds and counted from the creation of a record, before a token or
    /// authorization record that is no longer valid (expired or revoked) may be pruned.
    /// Default: 86400 (24 hours). Must be greater than 0. Valid records are never pruned.
    /// </summary>
    public int TokenPruneThresholdSeconds { get; set; } = 86_400;

    // ── Signing certificate ─────────────────────────────────────────────────

    /// <summary>
    /// Path to the X.509 signing certificate (PFX/PKCS12).
    /// Required in a non-Development environment if <see cref="SigningCertificateBase64"/> is not set.
    /// In Development, a dev certificate is used automatically.
    /// </summary>
    public string? SigningCertificatePath { get; set; }

    /// <summary>
    /// X.509 signing certificate in Base64 format (PFX/PKCS12).
    /// Takes priority over <see cref="SigningCertificatePath"/>.
    /// Convenient for container deployments via env vars (VERIQA__OPENIDDICT__SERVER__SIGNINGCERTIFICATEBASE64).
    /// </summary>
    public string? SigningCertificateBase64 { get; set; }

    /// <summary>
    /// Signing certificate password. Applied to both the file and the Base64 source.
    /// </summary>
    public string? SigningCertificatePassword { get; set; }

    // ── Encryption certificate ──────────────────────────────────────────────

    /// <summary>
    /// Path to the X.509 encryption certificate (PFX/PKCS12).
    /// Required in a non-Development environment if <see cref="EncryptionCertificateBase64"/> is not set.
    /// In Development, a dev certificate is used automatically.
    /// </summary>
    public string? EncryptionCertificatePath { get; set; }

    /// <summary>
    /// X.509 encryption certificate in Base64 format (PFX/PKCS12).
    /// Takes priority over <see cref="EncryptionCertificatePath"/>.
    /// Convenient for container deployments via env vars (VERIQA__OPENIDDICT__SERVER__ENCRYPTIONCERTIFICATEBASE64).
    /// </summary>
    public string? EncryptionCertificateBase64 { get; set; }

    /// <summary>
    /// Encryption certificate password. Applied to both the file and the Base64 source.
    /// </summary>
    public string? EncryptionCertificatePassword { get; set; }

    /// <summary>
    /// Loads the X.509 signing certificate.
    /// Source priority: Base64 → file.
    /// The calling code (OpenIddict) manages the certificate's lifetime and Dispose.
    /// </summary>
    /// <returns>The signing certificate.</returns>
    /// <exception cref="InvalidOperationException">Neither Base64 nor a path is set.</exception>
    public X509Certificate2 LoadSigningCertificate()
    {
        // Base64 takes priority (convenient for an env var in container deployments)
        if (!string.IsNullOrEmpty(SigningCertificateBase64))
        {
            var bytes = Convert.FromBase64String(SigningCertificateBase64);
            return X509CertificateLoader.LoadPkcs12(bytes, SigningCertificatePassword);
        }

        // Fallback — load from file
        if (!string.IsNullOrEmpty(SigningCertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(SigningCertificatePath, SigningCertificatePassword);
        }

        throw new InvalidOperationException(
            "Set Veriqa:OpenIddict:Server:SigningCertificateBase64 (Base64 PFX) "
            + "or Veriqa:OpenIddict:Server:SigningCertificatePath (path to a PFX file). "
            + "The parameters are required in a non-Development environment.");
    }

    /// <summary>
    /// Loads the X.509 encryption certificate.
    /// Source priority: Base64 → file.
    /// The calling code (OpenIddict) manages the certificate's lifetime and Dispose.
    /// </summary>
    /// <returns>The encryption certificate.</returns>
    /// <exception cref="InvalidOperationException">Neither Base64 nor a path is set.</exception>
    public X509Certificate2 LoadEncryptionCertificate()
    {
        // Base64 takes priority (convenient for an env var in container deployments)
        if (!string.IsNullOrEmpty(EncryptionCertificateBase64))
        {
            var bytes = Convert.FromBase64String(EncryptionCertificateBase64);
            return X509CertificateLoader.LoadPkcs12(bytes, EncryptionCertificatePassword);
        }

        // Fallback — load from file
        if (!string.IsNullOrEmpty(EncryptionCertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(EncryptionCertificatePath, EncryptionCertificatePassword);
        }

        throw new InvalidOperationException(
            "Set Veriqa:OpenIddict:Server:EncryptionCertificateBase64 (Base64 PFX) "
            + "or Veriqa:OpenIddict:Server:EncryptionCertificatePath (path to a PFX file). "
            + "The parameters are required in a non-Development environment.");
    }
}
