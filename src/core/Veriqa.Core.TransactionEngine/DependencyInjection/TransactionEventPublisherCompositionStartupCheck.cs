// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Veriqa.Core.TransactionEngine.Events;

namespace Veriqa.Core.TransactionEngine.DependencyInjection;

/// <summary>
/// Startup gate over the composition of the event publisher: refuses to start when the publisher
/// the container resolves is not the fan-out the engine composed.
/// </summary>
/// <remarks>
/// The engine can only fold the publisher registrations that are in the collection when
/// <c>AddVeriqaTransactionEngine</c> runs. One registered after it wins the resolution and takes
/// the in-process dispatch away with it: the audit trail, the real-time sign-in push and the prompt
/// cleanup stay registered and are never called again — the exact silent failure the fan-out exists
/// to remove. Refusing the start turns it into the loudest possible failure and names the fix.
/// </remarks>
internal sealed class TransactionEventPublisherCompositionStartupCheck : IHostedService
{
    /// <summary>
    /// The publisher the container actually resolves — the one every consumer of the engine gets.
    /// </summary>
    private readonly ITransactionEventPublisher _publisher;

    /// <summary>
    /// Creates the startup check.
    /// </summary>
    /// <param name="publisher">Publisher resolved from the container.</param>
    public TransactionEventPublisherCompositionStartupCheck(ITransactionEventPublisher publisher)
    {
        _publisher = publisher;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The resolved publisher is not the engine's fan-out, so the in-process event handlers would
    /// never be called.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_publisher is CompositeTransactionEventPublisher)
        {
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"The resolved ITransactionEventPublisher ('{_publisher.GetType().FullName}') is not the "
            + "fan-out composed by the transaction engine: every registered ITransactionEventHandler "
            + "— the audit trail, the real-time sign-in push, the prompt cleanup — would stay "
            + "registered and never be called. The publisher was registered after "
            + "AddVeriqaTransactionEngine, and a later registration wins the resolution. Move the "
            + "ITransactionEventPublisher registration above AddVeriqaTransactionEngine, or register "
            + "the transport through the engine builder (UseRabbitMqPublisher): either way it "
            + "becomes an outbound transport of the fan-out instead of replacing it.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
