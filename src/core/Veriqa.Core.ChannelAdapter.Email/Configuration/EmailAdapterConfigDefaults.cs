// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// Default configuration constants of the Email adapter.
/// Used as default values in the Options classes.
/// </summary>
internal static class EmailAdapterConfigDefaults
{
    /// <summary>
    /// Default client application name in the email — used when the transaction has no
    /// initiator context (SPEC-017) (capture is disabled or the transaction was created
    /// before it was enabled) and the client's DisplayName cannot be determined.
    /// </summary>
    public const string DefaultClientName = "Veriqa";

    /// <summary>
    /// Default action/correlation token TTL — 5 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultTokenTtl = TimeSpan.FromMinutes(5);
}
