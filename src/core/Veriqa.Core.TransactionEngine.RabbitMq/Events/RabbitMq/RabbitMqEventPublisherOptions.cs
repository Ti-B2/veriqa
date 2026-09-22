// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Events.RabbitMq;

/// <summary>
/// Settings of the RabbitMQ transaction event publisher.
/// </summary>
public sealed class RabbitMqEventPublisherOptions
{
    /// <summary>
    /// RabbitMQ connection string (amqp://user:pass@host:port/vhost).
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Exchange name for publishing transaction events.
    /// Default: "veriqa.transactions".
    /// </summary>
    public string ExchangeName { get; set; } = RabbitMqEventPublisherConstants.DefaultExchangeName;

    /// <summary>
    /// Capacity of the internal event queue (bounded channel capacity).
    /// When the limit is reached, PublishAsync waits for space to free up (back-pressure).
    /// Default: <see cref="RabbitMqEventPublisherConstants.DefaultQueueCapacity"/>.
    /// </summary>
    public int QueueCapacity { get; set; } = RabbitMqEventPublisherConstants.DefaultQueueCapacity;

    /// <summary>
    /// Maximum number of reconnection attempts after a connection loss.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = RabbitMqEventPublisherConstants.DefaultMaxReconnectAttempts;

    /// <summary>
    /// Base reconnection delay (seconds). Grows exponentially.
    /// </summary>
    public int ReconnectBaseDelaySeconds { get; set; } = RabbitMqEventPublisherConstants.DefaultReconnectBaseDelaySeconds;
}
