// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Enums;

/// <summary>
/// Sender verification policy for Email Push mode (SPEC-016 §5.5).
/// </summary>
public enum EmailVerificationPolicy
{
    /// <summary>
    /// Requires a DMARC pass with alignment for the From domain.
    /// Production default for public login.
    /// </summary>
    DmarcAlignedPass,

    /// <summary>
    /// Requires an SPF pass or DKIM pass without strict DMARC alignment.
    /// Acceptable for controlled environments.
    /// </summary>
    SpfOrDkimPass,

    /// <summary>
    /// Trusts the verified sender from the inbound provider.
    /// When the provider offers a strong guarantee.
    /// </summary>
    ProviderVerified,

    /// <summary>
    /// Accepts only domains/addresses from the allowlist.
    /// B2B/B2G, closed environments.
    /// </summary>
    AllowListOnly,

    /// <summary>
    /// Does not verify the sender cryptographically.
    /// Development only. Forbidden in production (EM-030).
    /// </summary>
    None
}
