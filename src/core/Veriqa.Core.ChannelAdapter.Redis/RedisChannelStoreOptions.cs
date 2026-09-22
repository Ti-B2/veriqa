// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Redis;

/// <summary>
/// Options of the Redis-backed channel stores.
/// Configured through the fluent API on the channel adapter builder.
/// </summary>
/// <remarks>
/// Every port gets its own key prefix so that a <c>SCAN</c> over one set of keys never walks
/// another one. Record lifetimes are not configured here: a token or a correlation lives exactly as
/// long as the entity says it does (<c>ExpiresAt</c>), and prompt coordinates live as long as the
/// longest transaction the engine can create, plus the margin in which the expiry event that
/// consumes them is published. The single lifetime that has no owning entity — the retention of
/// processed message-ids — is settable below.
/// </remarks>
public sealed class RedisChannelStoreOptions
{
    /// <summary>
    /// Default key prefix of the in-channel prompt coordinates.
    /// </summary>
    public const string DefaultPromptKeyPrefix = "veriqa:ch:prompt:";

    /// <summary>
    /// Default key prefix of the Email Pull-mode action tokens.
    /// </summary>
    public const string DefaultEmailActionTokenKeyPrefix = "veriqa:ch:email:token:";

    /// <summary>
    /// Default key prefix of the Email Push-mode correlation tokens.
    /// </summary>
    public const string DefaultEmailPushCorrelationKeyPrefix = "veriqa:ch:email:corr:";

    /// <summary>
    /// Default key prefix of the processed inbound message-ids (deduplication of redelivery).
    /// </summary>
    public const string DefaultProcessedMessageKeyPrefix = "veriqa:ch:email:msg:";

    /// <summary>
    /// Default retention of a processed message-id record — matches the in-process default, so
    /// switching a deployment to Redis does not silently change the deduplication window.
    /// </summary>
    public static readonly TimeSpan DefaultProcessedMessageRetention = TimeSpan.FromHours(24);

    /// <summary>
    /// Redis connection string (e.g. "localhost:6379").
    /// </summary>
    /// <remarks>
    /// Leave it empty to reuse an <c>IConnectionMultiplexer</c> registered elsewhere in the container
    /// (typically by the Transaction Engine Redis satellite). Setting it while such a registration
    /// wins the resolution is a configuration conflict and stops the host at start-up: the value would
    /// otherwise be ignored and the channel stores would silently land in another Redis. The order of
    /// the two registrations does not matter — the conflict is judged when the host starts.
    /// </remarks>
    public string Configuration { get; set; } = string.Empty;

    /// <summary>
    /// Prefix of the in-channel prompt coordinate keys.
    /// Default: "veriqa:ch:prompt:".
    /// </summary>
    public string PromptKeyPrefix { get; set; } = DefaultPromptKeyPrefix;

    /// <summary>
    /// Prefix of the Email Pull-mode action token keys.
    /// Default: "veriqa:ch:email:token:".
    /// </summary>
    public string EmailActionTokenKeyPrefix { get; set; } = DefaultEmailActionTokenKeyPrefix;

    /// <summary>
    /// Prefix of the Email Push-mode correlation keys (and of the "transaction → live token" index
    /// derived from it).
    /// Default: "veriqa:ch:email:corr:".
    /// </summary>
    public string EmailPushCorrelationKeyPrefix { get; set; } = DefaultEmailPushCorrelationKeyPrefix;

    /// <summary>
    /// Prefix of the processed inbound message-id keys.
    /// Default: "veriqa:ch:email:msg:".
    /// </summary>
    public string ProcessedMessageKeyPrefix { get; set; } = DefaultProcessedMessageKeyPrefix;

    /// <summary>
    /// How long the record of a processed inbound email is kept for deduplication of provider
    /// redelivery. Unlike a correlation, this record has no owning entity to take a lifetime from,
    /// so it is configured here. Default: 24 hours.
    /// </summary>
    public TimeSpan ProcessedMessageRetention { get; set; } = DefaultProcessedMessageRetention;
}
