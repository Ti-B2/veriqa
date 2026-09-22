// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.TransactionEngine.DependencyInjection;

/// <summary>
/// Startup gate over the composition of the transaction store: refuses to start when the store the
/// container resolves is not the one the engine guarded.
/// </summary>
/// <remarks>
/// The engine can only wrap the registrations that are in the collection when
/// <c>AddVeriqaTransactionEngine</c> runs. A store registered after it wins the resolution and takes
/// the state-transition guard away with it — a bypass that would otherwise be completely silent,
/// which is exactly what the guard exists to remove. Refusing the start turns it into the loudest
/// possible failure and names the fix.
/// </remarks>
internal sealed class TransactionStoreGuardStartupCheck : IHostedService
{
    /// <summary>
    /// The store the container actually resolves — the one every consumer of the engine will get.
    /// </summary>
    private readonly ITransactionStore _store;

    /// <summary>
    /// Creates the startup check.
    /// </summary>
    /// <param name="store">Store resolved from the container.</param>
    public TransactionStoreGuardStartupCheck(ITransactionStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The resolved store is not guarded, so a forbidden state transition could be written unnoticed.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_store is StateTransitionGuardingTransactionStore)
        {
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(
            $"The resolved ITransactionStore ('{_store.GetType().FullName}') is not guarded by the " +
            "transaction engine: a write moving a transaction along a transition the state machine " +
            "forbids would reach the storage unnoticed. The store was registered after " +
            "AddVeriqaTransactionEngine, and a later registration wins the resolution. Move the " +
            "ITransactionStore registration above AddVeriqaTransactionEngine, or register the store " +
            "through the engine builder (UseInMemoryStore, UseEfCoreStore, UseRedisStore).");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
