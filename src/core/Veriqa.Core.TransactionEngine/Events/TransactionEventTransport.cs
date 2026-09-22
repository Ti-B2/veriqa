// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Events;

/// <summary>
/// An outbound transport of transaction events: a publisher that carries events out of the process
/// (the RabbitMQ satellite, or one the host registered itself).
/// </summary>
/// <remarks>
/// A wrapper and not the publisher interface itself, because the two roles must be resolvable
/// apart: consumers of the engine ask for the single <see cref="ITransactionEventPublisher"/> and
/// have to get the fan-out, while the fan-out asks for every transport and must not find itself
/// among them. The engine's composition root rewrites each registration of the publisher interface
/// into a registration of this wrapper, so choosing a transport stays one call
/// (<c>UseRabbitMqPublisher</c>) and never silently replaces the in-process dispatch.
/// <para>
/// The wrapper is disposable for the same reason the store's guard is: the container releases a
/// service once per registration it realizes, and after the rewrite the object realized by the
/// replaced registration is this wrapper. A publisher holding a broker connection would otherwise
/// never be disposed on shutdown — its buffer unflushed and its connection left open — merely
/// because the engine started wrapping it. Standing in for one registration is also the LIMIT of
/// what the wrapper does: a publisher registered elsewhere as well (a concrete singleton the
/// interface registration forwards to, say) is still released by those registrations too, exactly as
/// it was before the engine folded it into the fan-out.
/// </para>
/// </remarks>
internal sealed class TransactionEventTransport : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Whether disposing the transport must dispose the publisher as well.
    /// </summary>
    private readonly bool _ownsPublisher;

    /// <summary>
    /// Guards against disposing the publisher twice.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Creates the transport wrapper.
    /// </summary>
    /// <param name="publisher">Publisher carrying events out of the process.</param>
    /// <param name="ownsPublisher">
    /// Whether the wrapper is responsible for disposing <paramref name="publisher"/>. True for a
    /// registration whose result the container would have released itself — it now realizes this
    /// wrapper instead, and without this the publisher would lose that release. False for a
    /// publisher handed to the container as a ready instance: that one belongs to whoever created
    /// it, and the container never released it before either.
    /// </param>
    public TransactionEventTransport(ITransactionEventPublisher publisher, bool ownsPublisher)
    {
        Publisher = publisher;
        _ownsPublisher = ownsPublisher;
    }

    /// <summary>
    /// Publisher carrying events out of the process.
    /// </summary>
    public ITransactionEventPublisher Publisher { get; }

    /// <summary>
    /// Disposes the publisher the wrapper owns, mirroring what the container would have done to it
    /// had the registration not been replaced by one returning this wrapper.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_ownsPublisher)
        {
            return;
        }

        if (Publisher is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        // Same refusal the container itself raises for a service that can only be disposed
        // asynchronously: blocking on DisposeAsync here would deadlock as readily as it does there.
        if (Publisher is IAsyncDisposable)
        {
            throw new InvalidOperationException(
                $"'{Publisher.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_ownsPublisher)
        {
            return;
        }

        if (Publisher is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        (Publisher as IDisposable)?.Dispose();
    }
}
