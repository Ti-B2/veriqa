// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Events;

/// <summary>
/// Transaction event handler.
/// Registered in DI and receives notifications about lifecycle events.
/// </summary>
public interface ITransactionEventHandler
{
    /// <summary>
    /// Handles a transaction event.
    /// </summary>
    /// <param name="transactionEvent">Event to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completing when the handling is done.</returns>
    Task HandleAsync(TransactionEvent transactionEvent, CancellationToken cancellationToken = default);
}
