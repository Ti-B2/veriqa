// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Exception thrown by the transaction store when the idempotency key uniqueness is violated.
/// Occurs on a data race: two transactions with the same (IdempotencyScope, IdempotencyKey)
/// were submitted concurrently, and the second one failed the store's unique constraint.
/// Caught by TransactionService and converted into a repeated lookup by idempotency key,
/// which returns the result of the first successfully persisted transaction.
/// </summary>
public sealed class DuplicateIdempotencyKeyException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception instance indicating the conflicting parameters.
    /// </summary>
    /// <param name="scope">Idempotency scope.</param>
    /// <param name="key">Idempotency key.</param>
    /// <param name="innerException">Inner exception from the store.</param>
    public DuplicateIdempotencyKeyException(string scope, string key, Exception? innerException = null)
        : base(
            $"Idempotency key (Scope='{scope}', Key='{key}') is already taken by another transaction. " +
            "Data race during concurrent creation of a transaction with identical parameters.",
            innerException)
    {
        Scope = scope;
        Key = key;
    }

    /// <summary>
    /// Idempotency scope of the conflicting transaction.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Idempotency key of the conflicting transaction.
    /// </summary>
    public string Key { get; }
}
