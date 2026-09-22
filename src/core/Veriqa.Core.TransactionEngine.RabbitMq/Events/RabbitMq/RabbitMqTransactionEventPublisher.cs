// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Veriqa.Core.TransactionEngine.Events.RabbitMq;

/// <summary>
/// Publisher of transaction events via RabbitMQ.
/// Separates the PublishAsync call (writing to an internal queue) from the actual send to the broker.
/// A background IHostedService manages the connection, declares the exchange, and reads from the queue.
/// On a connection drop it reconnects with exponential backoff.
/// </summary>
internal sealed class RabbitMqTransactionEventPublisher : ITransactionEventPublisher, IHostedService, IAsyncDisposable
{
    /// <summary>
    /// Publisher settings.
    /// </summary>
    private readonly RabbitMqEventPublisherOptions _options;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<RabbitMqTransactionEventPublisher> _logger;

    /// <summary>
    /// Internal event queue that decouples PublishAsync from sending to RabbitMQ.
    /// </summary>
    private readonly Channel<TransactionEvent> _queue;

    /// <summary>
    /// Active connection to RabbitMQ.
    /// </summary>
    private IConnection? _connection;

    /// <summary>
    /// Active RabbitMQ channel used for publishing.
    /// </summary>
    private IChannel? _rabbitChannel;

    /// <summary>
    /// Task of the background event-processing loop.
    /// </summary>
    private Task? _processingTask;

    /// <summary>
    /// Cancellation token source that manages the background processor's lifecycle.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Indicates whether resources have been released.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Creates an instance of the RabbitMQ event publisher.
    /// </summary>
    /// <param name="options">Connection and behavior settings.</param>
    /// <param name="logger">Logger.</param>
    public RabbitMqTransactionEventPublisher(
        IOptions<RabbitMqEventPublisherOptions> options,
        ILogger<RabbitMqTransactionEventPublisher> logger)
    {
        // Store the settings and create the internal bounded event queue.
        // The bounded channel caps memory when RabbitMQ degrades — on overflow
        // PublishAsync waits for space (back-pressure), preventing uncontrolled growth.
        _options = options.Value;
        _logger = logger;
        _queue = Channel.CreateBounded<TransactionEvent>(
            new BoundedChannelOptions(_options.QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    /// <inheritdoc />
    public async Task PublishAsync(TransactionEvent transactionEvent, CancellationToken cancellationToken = default)
    {
        // Write the event to the internal queue — the background processor will send it to RabbitMQ
        try
        {
            await _queue.Writer.WriteAsync(transactionEvent, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning(
                "Event {EventType} for transaction {TransactionId} dropped: the internal queue is closed (application is shutting down)",
                transactionEvent.GetType().Name,
                transactionEvent.TransactionId.ToString());
        }
    }

    /// <summary>
    /// Starts the background processor: establishes the connection to RabbitMQ and begins processing the queue.
    /// </summary>
    /// <param name="cancellationToken">Startup cancellation token.</param>
    /// <returns>Startup completion task.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Establish the connection to RabbitMQ and declare the exchange
        await ConnectAsync(cancellationToken);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessQueueAsync(_cts.Token);

        _logger.LogInformation(
            "RabbitMQ transaction event publisher started. Exchange: {ExchangeName}",
            _options.ExchangeName);
    }

    /// <summary>
    /// Stops the background processor: completes the internal queue, waits for processing to finish,
    /// and closes the RabbitMQ connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (host graceful-shutdown timeout).</param>
    /// <returns>Stop completion task.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Signal that there are no more incoming events
        _queue.Writer.TryComplete();

        try
        {
            if (_processingTask is not null)
            {
                try
                {
                    await _processingTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The graceful-shutdown timeout elapsed — forcibly stop the processor
                    if (_cts is not null)
                    {
                        await _cts.CancelAsync();
                    }

                    try
                    {
                        // The processing task is this publisher's own work (started in StartAsync);
                        // awaiting it after cancellation is safe — VSTHRD003 does not apply (not a
                        // "foreign" Task).
#pragma warning disable VSTHRD003
                        await _processingTask;
#pragma warning restore VSTHRD003
                    }
                    catch (OperationCanceledException) when (_cts?.IsCancellationRequested ?? false)
                    {
                        // Normal forced stop
                    }
                }
            }
        }
        finally
        {
            await CloseConnectionAsync();
            _cts?.Dispose();
            _cts = null;
        }

        _logger.LogInformation("RabbitMQ transaction event publisher stopped");
    }

    /// <summary>
    /// Releases the RabbitMQ connection resources.
    /// Guarantees that the background processor is stopped and the internal queue is completed,
    /// to prevent publishing into an already-closed connection.
    /// </summary>
    /// <returns>Resource-release task.</returns>
    public async ValueTask DisposeAsync()
    {
        // Prevent releasing resources more than once
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Cancel the background processor first: it interrupts ReadAllAsync immediately,
        // without waiting for the natural completion of the queue.
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        // Complete the writer after cancellation: new events are no longer accepted
        _queue.Writer.TryComplete();

        if (_processingTask is not null)
        {
            try
            {
                // The processing task is this publisher's own work (started in StartAsync); draining
                // it here is safe — VSTHRD003 does not apply (not a "foreign" Task).
#pragma warning disable VSTHRD003
                await _processingTask;
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Normal stop on cancellation
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finishing the RabbitMQ background processor in DisposeAsync");
            }
        }

        await CloseConnectionAsync();
        _cts?.Dispose();
    }

    /// <summary>
    /// Establishes the connection to RabbitMQ and declares the exchange.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection completion task.</returns>
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        // Create the connection factory and establish the connection
        IConnectionFactory factory = new ConnectionFactory
        {
            Uri = new Uri(_options.ConnectionString)
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _rabbitChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Declare a durable topic exchange — it survives a RabbitMQ restart
        await _rabbitChannel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: RabbitMqEventPublisherConstants.ExchangeType,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Closes the RabbitMQ channel and connection.
    /// </summary>
    /// <returns>Close completion task.</returns>
    private async Task CloseConnectionAsync()
    {
        // Close the RabbitMQ channel and connection, ignoring errors during shutdown
        if (_rabbitChannel is not null)
        {
            try
            {
                await _rabbitChannel.CloseAsync();
                _rabbitChannel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing the RabbitMQ channel");
            }
            finally
            {
                _rabbitChannel = null;
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing the RabbitMQ connection");
            }
            finally
            {
                _connection = null;
            }
        }
    }

    /// <summary>
    /// Background loop reading events from the internal queue and publishing to RabbitMQ.
    /// On a publish error it reconnects with an exponential delay.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing completion task.</returns>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        // Read events from the queue and publish to RabbitMQ, reconnecting on errors
        try
        {
            await foreach (var transactionEvent in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                await PublishToRabbitMqWithRetryAsync(transactionEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal stop on forced cancellation
        }
    }

    /// <summary>
    /// Publishes an event to RabbitMQ with retries on a connection drop.
    /// </summary>
    /// <param name="transactionEvent">Event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Publish completion task.</returns>
    private async Task PublishToRabbitMqWithRetryAsync(
        TransactionEvent transactionEvent,
        CancellationToken cancellationToken)
    {
        // Try to publish the event at most MaxReconnectAttempts times.
        // A reconnect error does not break the loop — the attempt counter keeps growing,
        // which protects ProcessQueueAsync from terminating when RabbitMQ is permanently unavailable.
        // We guarantee at least one attempt with an incorrect MaxReconnectAttempts <= 0.
        var maxAttempts = Math.Max(1, _options.MaxReconnectAttempts);
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await PublishSingleEventAsync(transactionEvent, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal cancellation — stop the attempts
                throw;
            }
            catch (Exception ex)
            {
                var isLastAttempt = attempt >= maxAttempts - 1;

                if (isLastAttempt)
                {
                    _logger.LogError(
                        ex,
                        "Failed to publish event {EventType} for transaction {TransactionId} after {Attempts} attempts. Event lost.",
                        transactionEvent.GetType().Name,
                        transactionEvent.TransactionId.ToString(),
                        maxAttempts);
                    return;
                }

                var delaySeconds = CalculateReconnectDelay(attempt);

                _logger.LogWarning(
                    ex,
                    "Error publishing event {EventType} to RabbitMQ. Attempt {Attempt}/{MaxAttempts}. Reconnecting in {DelaySeconds}s.",
                    transactionEvent.GetType().Name,
                    attempt + 1,
                    maxAttempts,
                    delaySeconds);

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds),
                    cancellationToken);

                // We do not rethrow the reconnect error — it is already logged in ReconnectAsync.
                // The next iteration will retry PublishSingleEventAsync, which will throw again
                // if the connection is not restored, correctly exhausting MaxReconnectAttempts.
                try
                {
                    await ReconnectAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // The error is logged in ReconnectAsync — continue the loop
                }
            }
        }
    }

    /// <summary>
    /// Publishes a single event to RabbitMQ.
    /// Serializes the event to JSON and sends it to the configured exchange.
    /// </summary>
    /// <param name="transactionEvent">Event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Publish completion task.</returns>
    private async Task PublishSingleEventAsync(
        TransactionEvent transactionEvent,
        CancellationToken cancellationToken)
    {
        // Serialize the event to JSON and publish to the RabbitMQ exchange
        if (_rabbitChannel is null)
        {
            throw new InvalidOperationException("RabbitMQ channel is not initialized");
        }

        var eventTypeName = transactionEvent.GetType().Name.ToLowerInvariant();
        var routingKey = RabbitMqEventPublisherConstants.RoutingKeyPrefix + eventTypeName;

        var json = JsonSerializer.Serialize(transactionEvent, transactionEvent.GetType());
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            ContentType = RabbitMqEventPublisherConstants.ContentTypeJson,
            DeliveryMode = DeliveryModes.Persistent
        };

        await _rabbitChannel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reconnects to RabbitMQ: closes the current connection and establishes a new one.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reconnect completion task.</returns>
    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        // Close the old connection and create a new one; on error, log and rethrow
        await CloseConnectionAsync();

        try
        {
            await ConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error while reconnecting to RabbitMQ");
            throw;
        }
    }

    /// <summary>
    /// Computes the reconnect delay with exponential growth.
    /// </summary>
    /// <param name="attempt">Attempt number (0-based).</param>
    /// <returns>Delay in seconds.</returns>
    private int CalculateReconnectDelay(int attempt)
    {
        // Exponential growth: base * 2^attempt
        return _options.ReconnectBaseDelaySeconds * (int)Math.Pow(2, attempt);
    }
}
