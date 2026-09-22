// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Exception thrown by the transaction store when the allowed capacity is exceeded.
/// Caught by TransactionService and converted into a controlled Result.Failure
/// with the error code <see cref="Domain.TransactionErrorCodes.StoreCapacityExceeded"/>.
/// </summary>
public sealed class StoreCapacityExceededException : Exception
{
    /// <summary>
    /// Creates an exception instance indicating the exceeded limit.
    /// </summary>
    /// <param name="maxEntries">Maximum allowed number of entries.</param>
    public StoreCapacityExceededException(int maxEntries)
        : base(
            $"In-memory store is full: reached the limit of {maxEntries} transactions. " +
            "Increase Veriqa:TransactionEngine:MaxInMemoryEntries or switch to an external store.")
    {
        MaxEntries = maxEntries;
    }

    /// <summary>
    /// Maximum allowed number of entries at which the limit was reached.
    /// </summary>
    public int MaxEntries { get; }
}
