// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// Email tenant-credential settings group (SPEC-003 §17.3, CFG-202): the tenant level
/// (in self-hosted ≡ the core). Includes outbound credentials (SMTP), the inbound webhook secret,
/// the public base URL and sender branding. Resolved by <c>(tenant, ChannelType)</c>.
/// </summary>
/// <param name="PublicBaseUrl">The public base URL for the magic link/QR (branding/identity).</param>
/// <param name="Outbound">Outbound delivery: provider, SMTP credentials, From branding.</param>
/// <param name="Inbound">Inbound processing: provider, inbound address, webhook secret.</param>
public sealed record EmailTenantCredentials(
    string PublicBaseUrl,
    EmailOutboundOptions Outbound,
    EmailInboundOptions Inbound)
{
    /// <summary>
    /// Tenant-level token lifetime (SPEC-003 §17.3, semantics of CFG-220);
    /// null — use the core default (<see cref="EmailOptions.TokenTtl"/>).
    /// Added as an init property, not a positional parameter: the record is public surface of the
    /// Veriqa.Core.ChannelAdapter package, so the existing 3-argument constructor must stay intact
    /// (its consumer lives behind the NuGet boundary — Cloud ChannelCredentialSerializer).
    /// A null arrives only from an external source (deserializing older cloud JSON without the field).
    /// </summary>
    public TimeSpan? TokenTtl { get; init; }
}
