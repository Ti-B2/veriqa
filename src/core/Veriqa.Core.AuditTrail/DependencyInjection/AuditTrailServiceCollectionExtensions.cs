// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veriqa.Core.AuditTrail.Receiver;
using Veriqa.Core.AuditTrail.Retention;
using Veriqa.Core.Contracts.Audit;
using Veriqa.Core.TransactionEngine.Events;

namespace Veriqa.Core.AuditTrail.DependencyInjection;

/// <summary>
/// Registration of the audit trail (SPEC-011): the event-bus receiver, the chosen sink and the
/// retention process. The satellite is opt-in — a host that never calls this method has no audit
/// trail, and no core library references this package.
/// </summary>
public static class AuditTrailServiceCollectionExtensions
{
    /// <summary>
    /// Identifier of the package the EF Core sink ships in. Named in the "no sink selected" failure:
    /// the method it tells the caller to call is not in this package, and an instruction that cannot
    /// be followed as written is not an instruction.
    /// </summary>
    private const string EfCoreSinkPackageId = "Veriqa.Core.AuditTrail.EntityFrameworkCore";

    /// <summary>
    /// Registers the audit receiver, the sink chosen by the configuration delegate and — for a
    /// built-in sink — the retention process. A custom sink owns the retention of its own storage,
    /// so no sweep is registered for it and the host says so at startup.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Action choosing the sink and configuring the retention sweep.</param>
    /// <returns>Service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no sink is registered at all (a journal with a silent in-memory fallback loses
    /// records without a single failure, so the startup fails instead), when a sink is
    /// registered after a built-in one and would shadow it — the records and the retention sweep
    /// would then belong to different stores — or when the configuration delegate selects both a
    /// built-in and a custom sink, in whichever order, so that one of the two choices would be
    /// dropped without a word.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a retention sweep parameter is outside the range its consumer accepts: the
    /// sweep interval outside <see cref="AuditRetentionOptions.MinSweepInterval"/> ..
    /// <see cref="AuditRetentionOptions.MaxSweepInterval"/>, or the batch size below
    /// <see cref="AuditRetentionOptions.MinBatchSize"/>.
    /// </exception>
    public static IServiceCollection AddVeriqaAuditTrail(
        this IServiceCollection services,
        Action<AuditTrailBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // The sweep parameters live once per collection: a repeated AddVeriqaAuditTrail hands the
        // builder the instance an earlier call already registered, instead of a fresh one carrying
        // the class defaults. The container resolves the last registration, so a fresh instance
        // would silently roll the parameters of an earlier call back to the defaults; sharing the
        // instance makes both directions agree with each other — a call that never touches
        // ConfigureRetention changes nothing, and a call that does configures the sweep already
        // registered.
        var registeredRetentionOptions = FindRegisteredOptions<AuditRetentionOptions>(services);
        var registeredRecordOptions = FindRegisteredOptions<AuditRecordOptions>(services);

        var builder = new AuditTrailBuilder(
            services,
            registeredRetentionOptions ?? new AuditRetentionOptions(),
            registeredRecordOptions ?? new AuditRecordOptions());

        configure(builder);

        // Both a built-in and a custom sink asked for explicitly: whichever Use* call came last
        // wins and the other choice is dropped without a word. The check runs here rather than
        // inside the Use* methods because the delegate is free to call them in either order — only
        // once it has run is the pair of choices known.
        if (builder.BuiltInSinkRegistered && builder.CustomSinkRegistered)
        {
            throw new InvalidOperationException(
                $"Both a built-in audit sink ({nameof(AuditTrailBuilder)}.UseInMemorySink() or "
                + $"UseEfCoreSink(...)) and a custom one "
                + $"({nameof(AuditTrailBuilder)}.UseSink<TSink>()) were selected inside "
                + $"{nameof(AddVeriqaAuditTrail)}. Keep exactly one: the custom sink would receive "
                + "the records while the retention sweep kept clearing the built-in store.");
        }

        // Everything below is decided on the collection rather than on the builder flags, because
        // the flags see only half of it: they are internal, only the builder's own Use* methods
        // raise them, and a sink arrives just as well through the (public) Services — from an
        // ecosystem package extending this builder, from the host itself, or from an earlier call
        // of this method. Two positions carry the whole answer. The container resolves the *last*
        // registration of a service, and the retention sweep deletes through IAuditRetentionStore,
        // which is internal to this assembly and therefore registered by the shipped sinks alone,
        // each of them right beside its own IAuditSink. So the later of the two positions says
        // whether the records and the sweep still belong to the same store.
        var lastSinkIndex = LastIndexOfService(services, typeof(IAuditSink));
        var lastRetentionStoreIndex = LastIndexOfService(services, typeof(IAuditRetentionStore));

        if (lastSinkIndex < 0)
        {
            throw new InvalidOperationException(
                $"No audit sink was selected. Call {nameof(AuditTrailBuilder)}.UseInMemorySink(), "
                + $"UseEfCoreSink(...) (the {EfCoreSinkPackageId} package) or "
                + $"{nameof(AuditTrailBuilder)}.UseSink<TSink>() inside "
                + $"{nameof(AddVeriqaAuditTrail)}.");
        }

        // A sink registered after a built-in one shadows it: the records would go to the newcomer
        // while the sweep kept deleting from the store of the built-in sink nobody writes to any
        // more — silently, which is the one outcome the open Services must not buy us. The reverse
        // order is not this case and is not refused: there the built-in sink is the last
        // registration, so it owns both the records and their deletion.
        if (lastRetentionStoreIndex >= 0 && lastSinkIndex > lastRetentionStoreIndex)
        {
            throw new InvalidOperationException(
                $"An {nameof(IAuditSink)} is registered after the built-in audit sink chosen "
                + $"through {nameof(AuditTrailBuilder)}.UseInMemorySink() or "
                + $"UseEfCoreSink(...): it would receive the records "
                + "while the retention sweep kept deleting from the built-in store. Keep exactly "
                + "one sink — either drop the built-in choice, or drop the later registration "
                + $"({nameof(AuditTrailBuilder)}.UseSink<TSink>(), a direct registration into "
                + $"{nameof(AuditTrailBuilder)}.{nameof(AuditTrailBuilder.Services)}, or a "
                + $"following {nameof(AddVeriqaAuditTrail)} call).");
        }

        // The sweep parameters are host-owned and have no configuration section, so they are
        // validated here, at registration time, rather than through IValidateOptions on start.
        ValidateRetentionOptions(builder.RetentionOptions);

        // Only the first call registers: every later one has just configured this very instance
        // through the builder, and a second descriptor of the same instance would say nothing new.
        if (registeredRetentionOptions is null)
        {
            services.AddSingleton(Options.Create(builder.RetentionOptions));
        }

        // Same rule for the record content parameters, and for the same reason: the receiver
        // resolves one instance, and every call of this method configures that one.
        if (registeredRecordOptions is null)
        {
            services.AddSingleton(Options.Create(builder.RecordOptions));
        }

        // Clock of the retention cutoff. Registered by this component itself: the audit satellite is
        // opt-in and wires up independently of the auth server, so a host that took only the audit
        // must still be able to resolve the provider. TryAdd is idempotent — a host that also wired
        // a neighbouring component keeps the single instance it already has.
        services.TryAddSingleton(TimeProvider.System);

        // The receiver is an ordinary bus subscriber; TryAddEnumerable keeps a repeated
        // registration from adding a second one (and therefore a duplicate record per event).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransactionEventHandler, AuditTransactionEventHandler>());

        if (lastRetentionStoreIndex >= 0)
        {
            // Retention runs for every built-in sink — the one the check above has just confirmed
            // to be the sink the records go to. It bounds the lifetime of records already written
            // and is not subject to the logging-mode gate. AddHostedService is idempotent
            // (TryAddEnumerable), so a repeated registration adds no second sweep.
            services.AddHostedService<AuditRetentionService>();
        }
        else
        {
            // Any other sink — chosen through UseSink<TSink>() or registered straight into the
            // collection — stores records where its own implementation decides, and the deletion
            // side of that storage (IAuditRetentionStore) is deliberately internal to this
            // assembly, so the sweep has nothing to sweep and is not registered at all. Registering
            // it anyway would fail the host start on an unresolvable dependency; skipping it
            // silently would leave an operator waiting for a sweep that never comes — hence the
            // startup notice. Its logger only exists once the provider is built, so the notice is
            // deferred into a hosted service (as the channel adapter startup warning is). A factory
            // rather than AddHostedService<T>(): whether ConfigureRetention was called is known
            // here, at registration, and travels into the service through its constructor.
            services.AddSingleton<IHostedService>(serviceProvider =>
                new CustomSinkRetentionNoticeService(
                    serviceProvider.GetRequiredService<ILogger<CustomSinkRetentionNoticeService>>(),
                    serviceProvider.GetRequiredService<IAuditSink>(),
                    builder.RetentionConfigured));
        }

        return services;
    }

    /// <summary>
    /// Returns the position of the last non-keyed registration of a service, or -1 when there is
    /// none. The position, not merely the presence, is what the registration reasons about: the
    /// container resolves the last registration of a service, so two positions compared against
    /// each other say which of two competing registrations the host will actually run on.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="serviceType">Service type to look for.</param>
    /// <returns>Index of the last non-keyed descriptor of that service type, or -1.</returns>
    private static int LastIndexOfService(IServiceCollection services, Type serviceType)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            // Keyed descriptors are skipped: the receiver and the sweep resolve their dependencies
            // without a key, so a keyed registration is neither the sink the records go to nor a
            // shadow over a built-in one.
            if (!services[index].IsKeyedService && services[index].ServiceType == serviceType)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns the parameters an earlier call of this method registered into the collection, or
    /// <see langword="null"/> when this is the first call. Only an instance registration is taken:
    /// it is the shape this method registers, and the instance is the one the consumer will
    /// resolve, so configuring it is the same thing as configuring that consumer.
    /// </summary>
    /// <typeparam name="TOptions">Parameters to look for.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <returns>Registered parameters, or <see langword="null"/>.</returns>
    private static TOptions? FindRegisteredOptions<TOptions>(IServiceCollection services)
        where TOptions : class
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];

            // Keyed registrations are skipped for the same reason as in LastIndexOfService: the
            // consumers resolve their parameters without a key.
            if (!descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(IOptions<TOptions>)
                && descriptor.ImplementationInstance is IOptions<TOptions> registered)
            {
                return registered.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Rejects retention sweep parameters the sweep cannot actually run on. Every bound below is
    /// the contract of the consumer of that parameter, and it is checked here — while the caller of
    /// the registration is still on the stack — rather than at host start, where the same value
    /// surfaces as a framework exception naming neither the option nor the limit and stops the host.
    /// </summary>
    /// <param name="options">Sweep parameters to validate.</param>
    private static void ValidateRetentionOptions(AuditRetentionOptions options)
    {
        // Consumer of the interval: the PeriodicTimer of the sweep. It truncates the period to
        // whole milliseconds and accepts 1 .. uint.MaxValue - 1 of them, so both ends come from
        // there rather than from a policy of ours. Both are compared on the TimeSpan itself: a
        // fraction of a millisecond past either end is rejected instead of being truncated away,
        // which costs nothing at bounds of one millisecond and 49.7 days.
        //
        // The lower bound also covers zero and negative intervals, and that is the stricter half:
        // the timer takes -1 ms as "never tick", so a negative interval would pass its constructor
        // and silently leave the journal with a single sweep at startup and none after it.
        if (options.SweepInterval < AuditRetentionOptions.MinSweepInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.SweepInterval,
                $"{nameof(AuditRetentionOptions)}.{nameof(AuditRetentionOptions.SweepInterval)} must be at least "
                + $"{AuditRetentionOptions.MinSweepInterval} (the smallest period the sweep timer accepts).");
        }

        if (options.SweepInterval > AuditRetentionOptions.MaxSweepInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.SweepInterval,
                $"{nameof(AuditRetentionOptions)}.{nameof(AuditRetentionOptions.SweepInterval)} must not exceed "
                + $"{AuditRetentionOptions.MaxSweepInterval} (the largest period the sweep timer accepts).");
        }

        // Consumer of the batch size: the sweep loop, which repeats until a batch comes back short
        // of the requested size. A batch of zero never does — the pass would spin forever deleting
        // nothing — so the lower bound is one record. An upper bound has no source in the consumer:
        // the batch is a limit over the expired records, and any value above the backlog just
        // clears it in one pass.
        if (options.BatchSize < AuditRetentionOptions.MinBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BatchSize,
                $"{nameof(AuditRetentionOptions)}.{nameof(AuditRetentionOptions.BatchSize)} must be at least "
                + $"{AuditRetentionOptions.MinBatchSize} (a smaller batch never ends the sweep loop).");
        }
    }
}
