// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;

using Microsoft.Extensions.DependencyInjection;
using Veriqa.Core.AuditTrail.Retention;
using Veriqa.Core.AuditTrail.Store;
using Veriqa.Core.Contracts.Audit;

namespace Veriqa.Core.AuditTrail.DependencyInjection;

/// <summary>
/// Builder configuring the audit trail: the sink the journal is written to and the retention sweep.
/// Obtained inside <see cref="AuditTrailServiceCollectionExtensions.AddVeriqaAuditTrail"/>.
/// </summary>
public sealed class AuditTrailBuilder
{
    /// <summary>
    /// Service collection the builder registers into. Exposed for ecosystem packages that add their
    /// own registrations through an extension method on this builder; not part of the everyday
    /// configuration surface.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>
    /// Indicates that one of the shipped sinks has been chosen here. Read together with
    /// <see cref="CustomSinkRegistered"/> to refuse a delegate that asks for both at once, in
    /// whichever order it asks; which sink the container ends up resolving, and who owns the
    /// retention of its storage, is read off the service collection instead — a sink also arrives
    /// through <see cref="Services"/>, where no flag of this builder is raised at all.
    /// The setter is internal so a satellite sink package can flag its registration: a shipped sink
    /// that ships in a package of its own is still a shipped sink, and the pair-of-choices check
    /// has to see it.
    /// </summary>
    internal bool BuiltInSinkRegistered { get; set; }

    /// <summary>
    /// Indicates that a custom sink has been chosen here through <see cref="UseSink{TSink}"/>.
    /// Read together with <see cref="BuiltInSinkRegistered"/> — see the note there.
    /// </summary>
    internal bool CustomSinkRegistered { get; private set; }

    /// <summary>
    /// Indicates that <see cref="ConfigureRetention"/> has been called. Used to say out loud that
    /// the parameters it set are ignored when the sweep they configure is not registered at all.
    /// </summary>
    internal bool RetentionConfigured { get; private set; }

    /// <summary>
    /// Content parameters of a record accumulated by <see cref="ConfigureRecord"/>. Supplied by the
    /// caller for the same reason as <see cref="RetentionOptions"/>: on a repeated
    /// <see cref="AuditTrailServiceCollectionExtensions.AddVeriqaAuditTrail"/> it is the instance
    /// the earlier call registered, so that this call configures the receiver already registered
    /// instead of rolling its parameters back to the class defaults.
    /// </summary>
    internal AuditRecordOptions RecordOptions { get; }

    /// <summary>
    /// Retention sweep parameters accumulated by <see cref="ConfigureRetention"/>.
    /// Validated once, after the configuration delegate has run. Supplied by the caller rather
    /// than created here: on a repeated
    /// <see cref="AuditTrailServiceCollectionExtensions.AddVeriqaAuditTrail"/> it is the instance
    /// the earlier call registered, so that this call configures the sweep already registered
    /// instead of replacing its parameters with the class defaults.
    /// </summary>
    internal AuditRetentionOptions RetentionOptions { get; }

    /// <summary>
    /// Creates the builder.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="retentionOptions">Sweep parameters the configuration delegate configures.</param>
    /// <param name="recordOptions">Record content parameters the configuration delegate configures.</param>
    internal AuditTrailBuilder(
        IServiceCollection services,
        AuditRetentionOptions retentionOptions,
        AuditRecordOptions recordOptions)
    {
        Services = services;
        RetentionOptions = retentionOptions;
        RecordOptions = recordOptions;
    }

    /// <summary>
    /// Uses the in-process sink (development and running without a database).
    /// </summary>
    /// <returns>Builder for chaining.</returns>
    public AuditTrailBuilder UseInMemorySink()
    {
        // The concrete type is registered once, and the abstractions resolve to that one instance:
        // two independent registrations would give the receiver and the retention sweep separate
        // journals, and the sweep would silently clean an empty one.
        Services.AddSingleton<InMemoryAuditSink>();
        Services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<InMemoryAuditSink>());
        Services.AddSingleton<IAuditRetentionStore>(sp => sp.GetRequiredService<InMemoryAuditSink>());

        BuiltInSinkRegistered = true;

        return this;
    }

    /// <summary>
    /// Uses a custom audit sink. Records go wherever the implementation stores them, and the
    /// retention of that storage belongs to the implementation: the built-in retention sweep is not
    /// registered for a custom sink.
    /// </summary>
    /// <typeparam name="TSink">Custom sink type.</typeparam>
    /// <returns>Builder for chaining.</returns>
    public AuditTrailBuilder UseSink<TSink>()
        where TSink : class, IAuditSink
    {
        // AddSingleton, not TryAddSingleton: this is the choice of a sink, not a shipped default.
        // The last registration of a service is the one resolved, which is what makes the last
        // Use* call win when the method is called more than once.
        Services.AddSingleton<IAuditSink, TSink>();

        CustomSinkRegistered = true;

        return this;
    }

    /// <summary>
    /// Configures what a record carries beyond the safe attributes every record carries. Not
    /// calling it leaves the class defaults in place — nothing beyond those attributes.
    /// </summary>
    /// <param name="configure">Action configuring the record content parameters.</param>
    /// <returns>Builder for chaining.</returns>
    public AuditTrailBuilder ConfigureRecord(Action<AuditRecordOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(RecordOptions);

        return this;
    }

    /// <summary>
    /// Configures the retention sweep parameters. Not calling it leaves the class defaults in place.
    /// </summary>
    /// <param name="configure">Action configuring the sweep parameters.</param>
    /// <returns>Builder for chaining.</returns>
    public AuditTrailBuilder ConfigureRetention(Action<AuditRetentionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(RetentionOptions);

        RetentionConfigured = true;

        return this;
    }
}
