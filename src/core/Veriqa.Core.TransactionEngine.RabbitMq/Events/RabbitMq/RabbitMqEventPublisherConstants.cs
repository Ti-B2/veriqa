// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Events.RabbitMq;

/// <summary>
/// Constants for the RabbitMQ transaction event publisher.
/// </summary>
internal static class RabbitMqEventPublisherConstants
{
    /// <summary>
    /// Default exchange name for transaction events.
    /// </summary>
    internal const string DefaultExchangeName = "veriqa.transactions";

    /// <summary>
    /// Content-Type for JSON messages.
    /// </summary>
    internal const string ContentTypeJson = "application/json";

    /// <summary>
    /// Routing key prefix for transaction events.
    /// Pattern: {RoutingKeyPrefix}{event_type_in_lowercase}
    /// Example: "transaction.transactioncreatedevent"
    /// </summary>
    internal const string RoutingKeyPrefix = "transaction.";

    /// <summary>
    /// Exchange type (topic — supports pattern-based routing).
    /// </summary>
    internal const string ExchangeType = "topic";

    /// <summary>
    /// Default maximum number of reconnection attempts.
    /// </summary>
    internal const int DefaultMaxReconnectAttempts = 5;

    /// <summary>
    /// Default base reconnection delay in seconds.
    /// </summary>
    internal const int DefaultReconnectBaseDelaySeconds = 2;

    /// <summary>
    /// Default capacity of the internal bounded event queue.
    /// When full, PublishAsync waits for space to free up (back-pressure).
    /// </summary>
    internal const int DefaultQueueCapacity = 1000;
}
