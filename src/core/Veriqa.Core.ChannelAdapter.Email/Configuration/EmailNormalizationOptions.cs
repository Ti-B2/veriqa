// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// Email address normalization settings (SPEC-016 §6.2).
/// </summary>
public sealed class EmailNormalizationOptions
{
    /// <summary>
    /// Lowercase the domain part of the email. Enabled by default.
    /// </summary>
    public bool LowercaseDomain { get; set; } = true;

    /// <summary>
    /// Lowercase the local part of the email. Enabled by default for consumer UX.
    /// </summary>
    public bool LowercaseLocalPart { get; set; } = true;

    /// <summary>
    /// Apply provider-specific canonicalization (for example, Gmail dots/plus aliases).
    /// Disabled by default — may lead to erroneous merging of identities.
    /// </summary>
    public bool ProviderSpecificCanonicalization { get; set; } = false;
}
