// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Events;

/// <summary>
/// Abstraction for publishing transaction lifecycle events.
/// Consumers — Real-time Communication, Audit Trail and other subsystems.
/// Implemented without MediatR, via direct handler registration in DI.
/// </summary>
public interface ITransactionEventPublisher
{
    /// <summary>
    /// Publishes a transaction event to all registered handlers.
    /// </summary>
    /// <param name="transactionEvent">Event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completing when the publish is done.</returns>
    Task PublishAsync(TransactionEvent transactionEvent, CancellationToken cancellationToken = default);
}
