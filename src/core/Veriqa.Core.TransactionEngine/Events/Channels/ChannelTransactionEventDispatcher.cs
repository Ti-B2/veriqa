// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.TransactionEngine.Events.Channels;

/// <summary>
/// In-process dispatcher of transaction events: queues an event in a bounded
/// System.Threading.Channels channel and, from a background IHostedService, hands it to every
/// registered ITransactionEventHandler. The calling code does not wait for the handlers to finish
/// (back-pressure: when the queue is full, the enqueue waits for space, but never for the handling).
/// The bounded channel caps the queue size to guard against OOM under overload.
/// </summary>
/// <remarks>
/// Dispatching to handlers is a part of the engine and not of an outbound transport: the audit
/// trail, the real-time sign-in push and the prompt cleanup are in-process subscribers, and they
/// must keep receiving events whichever transport the deployment publishes events outward with.
/// That is why this type is no longer an <see cref="ITransactionEventPublisher"/> of its own — the
/// publisher the container resolves is the fan-out, and it feeds this dispatcher always.
/// </remarks>
internal sealed class ChannelTransactionEventDispatcher : IHostedService
{
    /// <summary>
    /// Internal channel for the event queue.
    /// Bounded: when the limit is reached, WriteAsync waits for space to free up (back-pressure).
    /// </summary>
    private readonly Channel<TransactionEvent> _channel;

    /// <summary>
    /// Registered event handlers.
    /// </summary>
    private readonly IEnumerable<ITransactionEventHandler> _handlers;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<ChannelTransactionEventDispatcher> _logger;

    /// <summary>
    /// The background queue-processing task.
    /// </summary>
    private Task? _processingTask;

    /// <summary>
    /// Cancellation token source for forcibly stopping the background processor.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Creates an instance of the event dispatcher.
    /// </summary>
    /// <param name="handlers">Registered handlers.</param>
    /// <param name="options">Dispatcher settings.</param>
    /// <param name="logger">Logger.</param>
    public ChannelTransactionEventDispatcher(
        IEnumerable<ITransactionEventHandler> handlers,
        IOptions<ChannelEventPublisherOptions> options,
        ILogger<ChannelTransactionEventDispatcher> logger)
    {
        // Initialize dependencies and create the bounded channel with a configurable capacity
        _handlers = handlers;
        _logger = logger;

        _channel = Channel.CreateBounded<TransactionEvent>(
            new BoundedChannelOptions(options.Value.Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    /// <summary>
    /// Queues an event for the in-process handlers.
    /// </summary>
    /// <param name="transactionEvent">Event to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the event is queued.</returns>
    public async Task EnqueueAsync(TransactionEvent transactionEvent, CancellationToken cancellationToken = default)
    {
        // Publish to the queue. When the channel is full, wait for space to free up (back-pressure).
        // When the channel is closed (shutdown), log and drop the event — do not break the calling code.
        try
        {
            await _channel.Writer.WriteAsync(transactionEvent, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning(
                "Event {EventType} for transaction {TransactionId} dropped: the event channel is closed (application is shutting down)",
                transactionEvent.GetType().Name,
                transactionEvent.TransactionId.ToString());
        }
    }

    /// <summary>
    /// Starts the background event-queue processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when startup finishes.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Create the CTS for forced shutdown and start the background processor
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessEventsAsync(_cts.Token);

        _logger.LogInformation("Background transaction event processor (Channels) started");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the background processor: completes the Channel and waits for the remaining events to be processed.
    /// When the graceful shutdown timeout expires, forcibly stops processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (the host's graceful shutdown timeout).</param>
    /// <returns>A task that completes when the stop finishes.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Signal that there will be no new events — the writer is completed
        _channel.Writer.TryComplete();

        try
        {
            if (_processingTask is not null)
            {
                // Let the background processor finish reading and processing the remaining events
                try
                {
                    await _processingTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The graceful shutdown timeout expired — forcibly stop reading from the channel
                    if (_cts is not null)
                    {
                        await _cts.CancelAsync();
                    }

                    try
                    {
                        // The processing task is this dispatcher's own work (started in StartAsync);
                        // awaiting it after cancellation is safe — VSTHRD003 does not apply (not a
                        // "foreign" Task).
#pragma warning disable VSTHRD003
                        await _processingTask;
#pragma warning restore VSTHRD003
                    }
                    catch (OperationCanceledException) when (_cts?.IsCancellationRequested ?? false)
                    {
                        // Normal forced shutdown of the background processor
                    }
                }
            }
        }
        finally
        {
            _cts?.Dispose();
        }

        _logger.LogInformation("Background transaction event processor (Channels) stopped");
    }

    /// <summary>
    /// Background loop that reads events from the Channel and invokes handlers.
    /// Completes when the Channel is closed (TryComplete) and all events are processed,
    /// or on the CancellationToken when forcibly stopped.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when processing finishes.</returns>
    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        // Read events from the channel and invoke handlers sequentially
        try
        {
            await foreach (var transactionEvent in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                await DispatchToHandlersAsync(transactionEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown on forced cancellation
        }
    }

    /// <summary>
    /// Invokes all handlers for a single event.
    /// A failure in one handler does not stop processing of the others.
    /// </summary>
    /// <param name="transactionEvent">The event to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when processing finishes.</returns>
    private async Task DispatchToHandlersAsync(TransactionEvent transactionEvent, CancellationToken cancellationToken)
    {
        // Invoke all registered handlers sequentially
        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(transactionEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal cancellation — abort processing
                throw;
            }
            catch (Exception ex)
            {
                // Log the handler error, but do not stop processing the others
                _logger.LogError(
                    ex,
                    "Error in handler {HandlerType} while processing event {EventType} for transaction {TransactionId}",
                    handler.GetType().Name,
                    transactionEvent.GetType().Name,
                    transactionEvent.TransactionId.ToString());
            }
        }
    }
}
