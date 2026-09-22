// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Store record of an in-channel confirmation prompt already sent for a transaction
/// (SPEC-003 §4.5). Written by the core right after the adapter reports the message reference, so
/// that a later lifecycle event (TTL expiry) can address that exact message.
/// </summary>
/// <remarks>
/// The message coordinates themselves live in the MIT contract shape
/// (<see cref="ChannelMessageRef"/>); everything else here is core bookkeeping the adapter never
/// sees — which channel was called, which tenant owned the send, which locale and which recipient.
/// </remarks>
public sealed record ChannelPromptMessageRef
{
    /// <summary>
    /// Type of the channel that sent the prompt (e.g. "telegram", "max").
    /// The expiry handler resolves the adapter by this type when no ambient context is left.
    /// </summary>
    public required string ChannelType { get; init; }

    /// <summary>
    /// Channel-defined coordinates of the sent prompt message.
    /// </summary>
    public required ChannelMessageRef Message { get; init; }

    /// <summary>
    /// Recipient within the channel, captured at send time. The background TTL path knows nothing
    /// else about the user, and the outcome notice requires a recipient.
    /// </summary>
    public required string ChannelUserId { get; init; }

    /// <summary>
    /// Tenant that owns the prompt (CA-164). Null — the default implicit tenant (self-hosted N=1).
    /// A background handler has no ambient request context, so the tenant is captured here and
    /// restored (<see cref="MultiTenancy.ChannelTenantContext.BeginScope"/>) before the outcome notice.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Recipient locale captured at send time (SPEC-017 §7.2), used to localize the terminal
    /// status text. Null — base language.
    /// </summary>
    public string? RecipientLocale { get; init; }

    /// <summary>
    /// Recipient time zone captured at send time (IANA identifier; null — none was stated). The other
    /// half of the same snapshot the locale is: how the recipient READS the terminal text, locale for
    /// its wording and zone for the moments in it (SPEC-036 TPL-016). The background TTL path has no
    /// transaction left to read either from, so both travel here; a record written before this field
    /// existed reads back with none, exactly as a transaction that stated no zone does.
    /// </summary>
    public string? RecipientTimeZone { get; init; }
}
