// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;

using Microsoft.Extensions.DependencyInjection;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Events.Channels;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.TransactionEngine.DependencyInjection;

/// <summary>
/// Builder for configuring the Transaction Engine.
/// Allows choosing the store implementation and other parameters.
/// </summary>
public sealed class TransactionEngineBuilder
{
    /// <summary>
    /// Service collection the builder registers into. Exposed for ecosystem packages that add their
    /// own registrations through an extension method on this builder; not part of the everyday
    /// configuration surface.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>
    /// Indicates that a store has been registered.
    /// The setter is internal so satellite store packages can flag their registration.
    /// </summary>
    internal bool StoreRegistered { get; set; }

    /// <summary>
    /// Creates the builder.
    /// </summary>
    /// <param name="services">Service collection.</param>
    internal TransactionEngineBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Uses the in-memory store (for development and testing).
    /// </summary>
    /// <returns>Builder for chaining.</returns>
    public TransactionEngineBuilder UseInMemoryStore()
    {
        // Register the in-memory store as a Singleton
        Services.AddSingleton<ITransactionStore, InMemoryTransactionStore>();
        StoreRegistered = true;

        return this;
    }

    /// <summary>
    /// Configures the in-process event dispatch based on System.Threading.Channels.
    /// </summary>
    /// <remarks>
    /// In-process dispatch to the registered <see cref="ITransactionEventHandler"/> instances is a
    /// part of the engine and is wired unconditionally, so calling this method is never required
    /// for the audit trail, the real-time sign-in push or the prompt cleanup to receive events.
    /// What it does is set the parameters of that dispatch — today the capacity of its queue.
    /// Publishing events OUT of the process is the separate choice of a transport
    /// (<c>UseRabbitMqPublisher</c>), and it is additive rather than a replacement.
    /// </remarks>
    /// <param name="configure">Optional action for configuring the parameters.</param>
    /// <returns>Builder for chaining.</returns>
    public TransactionEngineBuilder UseChannelsPublisher(Action<ChannelEventPublisherOptions>? configure = null)
    {
        // Apply the user configuration if provided; the dispatcher itself is registered by
        // AddVeriqaTransactionEngine, whichever transport the deployment chose
        if (configure is not null)
        {
            Services.Configure(configure);
        }

        return this;
    }
}
