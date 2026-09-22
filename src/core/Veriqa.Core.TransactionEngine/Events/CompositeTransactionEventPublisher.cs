// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.ExceptionServices;

using Veriqa.Core.TransactionEngine.Events.Channels;

namespace Veriqa.Core.TransactionEngine.Events;

/// <summary>
/// The publisher every consumer of the engine resolves: it hands the event to the in-process
/// dispatcher and then to every outbound transport the deployment chose.
/// </summary>
/// <remarks>
/// The two roles are separate types on purpose: with one type in a single publisher slot, choosing a
/// transport would switch the in-process dispatch off — <c>UseRabbitMqPublisher</c> would displace the
/// Channels publisher, and the audit trail, the real-time sign-in push and the prompt cleanup would
/// stop being called altogether, with no error and no warning. Here the dispatch is unconditional and
/// a transport is additive, so the choice of transport is a choice of what leaves the process and
/// nothing else.
/// <para>
/// The dispatcher comes first deliberately: the enqueue is a bounded-channel write, while a
/// transport may reach a broker over the network, and the in-process subscribers must not wait for
/// it. A transport that throws still reaches the caller — the calling code observes a transport
/// failure as it would from a single publisher — but only after every other transport has had the
/// event:
/// one unhealthy transport must not take its neighbours' events with it, and the in-process
/// dispatch is done by then either way.
/// </para>
/// <para>
/// The fan-out therefore has exactly three ways to end, and the caller's own
/// <see cref="CancellationToken"/> — never the type of the exception a transport threw — decides
/// which one it is. <em>Completed</em>: every transport was asked; the caller returns, or receives
/// the refusals if there were any. <em>Refused</em>: a transport threw; the refusal is kept and the
/// next transport is asked. A cancellation nobody asked for belongs here, not below — a transport
/// enforcing its own timeout reports it as an <see cref="OperationCanceledException"/> too, and
/// letting that end the fan-out would silently cost the transports after it their events.
/// <em>Cancelled</em>: the caller's token is signalled; the fan-out stops and leaves as an
/// <see cref="OperationCanceledException"/> — the shape every
/// <c>catch (OperationCanceledException) when (token.IsCancellationRequested)</c> up the stack is
/// written for — carrying the refusals already seen as its inner exception rather than instead of
/// it, because a refused delivery is what the caller must not lose.
/// </para>
/// </remarks>
internal sealed class CompositeTransactionEventPublisher : ITransactionEventPublisher
{
    /// <summary>
    /// Message of the cancellation that ends a fan-out which had already seen refusals: it tells
    /// the reader why the exception carries an inner one, the cancellation itself being no failure.
    /// </summary>
    private const string CancelledWithRefusalsMessage =
        "The event fan-out was cancelled by the caller after one or more transports had refused the event.";

    /// <summary>
    /// In-process dispatcher of the engine.
    /// </summary>
    private readonly ChannelTransactionEventDispatcher _dispatcher;

    /// <summary>
    /// Outbound transports chosen by the deployment; empty when events stay in the process.
    /// </summary>
    private readonly IEnumerable<TransactionEventTransport> _transports;

    /// <summary>
    /// Creates the fan-out publisher.
    /// </summary>
    /// <param name="dispatcher">In-process dispatcher.</param>
    /// <param name="transports">Outbound transports.</param>
    public CompositeTransactionEventPublisher(
        ChannelTransactionEventDispatcher dispatcher,
        IEnumerable<TransactionEventTransport> transports)
    {
        _dispatcher = dispatcher;
        _transports = transports;
    }

    /// <inheritdoc />
    public async Task PublishAsync(TransactionEvent transactionEvent, CancellationToken cancellationToken = default)
    {
        // In-process subscribers first — the enqueue does not wait for the handling
        await _dispatcher.EnqueueAsync(transactionEvent, cancellationToken);

        List<Exception>? failures = null;
        var cancelledByCaller = false;

        foreach (var transport in _transports)
        {
            // The caller's token is the source of truth for "who cancelled", and it is read before
            // the transport is called rather than after it threw: a transport that observes the
            // token is not asked for work already called off, and one that ignores it does not get
            // to keep the fan-out running past the moment the caller ended it.
            if (cancellationToken.IsCancellationRequested)
            {
                cancelledByCaller = true;
                break;
            }

            try
            {
                await transport.Publisher.PublishAsync(transactionEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller's cancellation, observed by the transport and arriving through it
                cancelledByCaller = true;
                break;
            }
            catch (Exception exception)
            {
                // Anything else is this transport's own refusal — a cancellation the caller never
                // asked for included. It is kept and rethrown below rather than swallowed, and the
                // transports after it still get the event.
                (failures ??= []).Add(exception);
            }
        }

        if (cancelledByCaller)
        {
            throw CancelledFanOut(failures, cancellationToken);
        }

        if (failures is null)
        {
            return;
        }

        // A single failure travels on as itself, stack trace intact — with the one transport of the
        // shipped composition the caller sees exactly what it saw before.
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(failures);
    }

    /// <summary>
    /// Builds the exception a fan-out the caller cancelled ends with.
    /// </summary>
    /// <param name="failures">Refusals seen before the cancellation; <c>null</c> when there were none.</param>
    /// <param name="cancellationToken">The caller's token, signalled by the time this is called.</param>
    /// <returns>
    /// A bare cancellation when nothing had refused the event, and one carrying the refusals as its
    /// inner exception otherwise: the caller must be able to catch a cancellation as a
    /// cancellation, and must not lose a refused delivery to a shutdown either.
    /// </returns>
    private static OperationCanceledException CancelledFanOut(
        List<Exception>? failures,
        CancellationToken cancellationToken)
    {
        if (failures is null)
        {
            return new OperationCanceledException(cancellationToken);
        }

        var refusals = failures.Count == 1 ? failures[0] : new AggregateException(failures);

        return new OperationCanceledException(CancelledWithRefusalsMessage, refusals, cancellationToken);
    }
}
