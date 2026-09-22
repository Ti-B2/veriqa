// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.Metrics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Diagnostics;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Events;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Extension methods for registering channel adapters in the DI container.
/// </summary>
public static class ChannelAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Veriqa channel adapter system.
    /// If no channel is enabled (all Enabled=false), the server starts
    /// without active adapters — CA-110 issues a warning, not an exception,
    /// so that profiles without channel configuration (e.g. demo-core) start correctly.
    /// </summary>
    /// <remarks>
    /// The channel contour is NOT a deployment form of its own, and nothing registered here stands in
    /// for a contour the deployment did not compose. A host that calls this method and nothing else
    /// compiles and builds a container, but it composes no transaction engine — so it has nothing to
    /// confirm.
    /// <para>
    /// The keys this contour OWNS travel with it: the call below registers their catalogs, and the
    /// reader of catalogs comes with that registration, so their levels are bound wherever the contour
    /// is composed. The one key the contour merely READS is the inbound-verification axis
    /// (<see cref="ChannelInboundVerificationConfigKeys"/>), whose catalog belongs to the auth server —
    /// and the contour REFUSES to start a deployment that enabled a channel without declaring that
    /// value. So a host composing the channels without the auth server states that one catalog itself,
    /// explicitly (<c>AddVeriqaConfigKeyCatalogs</c>), which is the whole of what a deployment composing
    /// the auth server gets from it. Such hosts exist outside tests, and a host that skips this reads
    /// none of the values it wrote for that axis.
    /// </para>
    /// </remarks>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="configure">Adapter configuration action.</param>
    /// <returns>Service collection for call chaining.</returns>
    public static IServiceCollection AddVeriqaChannelAdapters(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ChannelAdapterBuilder> configure)
    {
        // Report on a channel configuration left at the top-level "Channels" key. Registered
        // unconditionally: the very case it has to catch is "no channel is enabled BECAUSE the
        // configuration stayed at the old path", where nothing else in the pipeline speaks up.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, LegacyChannelsSectionStartupDiagnosticsService>());

        // The two halves of a channel disagreeing — an adapter with no core-level declaration, or a
        // declaration reporting a channel enabled that no adapter serves (SPEC-003 CA-198/CA-199). Reported
        // and never fatal. Registered BEFORE the inbound-verification pass below, because that pass
        // refuses the start of a deployment that enabled a channel without declaring the verification
        // of its inbound events — and a channel enabled with no adapter behind it walks straight into
        // that refusal, which alone tells the operator nothing about the missing half.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ChannelDeclarationStartupDiagnosticsService>());

        // Declared inbound verification of every channel enabled at core level (SPEC-003
        // CA-196/CA-197): a channel switched on without a declared value stops the host, and a polling
        // channel declaring something other than an outbound fetch is warned about. Registered
        // unconditionally and idempotently: it walks whatever channels registered a
        // CoreChannelDeclaration, so a deployment that added none is checked in zero steps.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ChannelInboundVerificationStartupValidator>());

        // The catalogs this CONTOUR owns, stated at its own composition — the channel capability, the
        // design of the pages of the core, the login-confirmation axis and the display of the outcome
        // receipt. They travel with the channels because that is where they are declared: a host that
        // composes the channels without the auth server carries their declarations all the same, and
        // nothing else in such a host would name them. The call guards each catalog on its own, so
        // composing the contour twice, or alongside another owner naming a catalog it already holds,
        // still leaves one registration of each. The keys of an INDIVIDUAL channel are not here: each
        // channel registers its own catalog at its own registration, so a deployment declares the
        // credentials of the channels it added and of no others.
        services.AddVeriqaConfigKeyCatalogs(
            configuration,
            ChannelConfigKeys.Catalog,
            CorePageBrandingConfigKeys.Catalog,
            LoginConfirmationConfigKeys.Catalog,
            OutcomeNoticeConfigKeys.Catalog);

        // Metrics of the contour. AddMetrics is the framework's own way of bringing IMeterFactory in
        // and is idempotent, so it is called here rather than assumed: an integrator may consume this
        // assembly without ServiceDefaults, and a missing factory would then fail at resolve time.
        services.AddMetrics();
        // Built by a factory rather than by type: the constructor is internal on purpose (recording
        // into a series integrators build dashboards on is the component's own business), and the
        // container only ever calls public constructors.
        services.TryAddSingleton(provider => new ChannelAdapterMetrics(
            provider.GetRequiredService<IMeterFactory>()));

        // Channel track multi-tenancy seam (SPEC-003 §17.4):
        // the default credential provider (N=1 via the TASK-040 resolver), the per-tenant client
        // factory, the channels_enabled resolver. Without a mandatory external dependency (invariant-1);
        // the tenant-level Cloud provider is added opt-in on top (invariant-2).
        services.AddChannelMultiTenancy();

        // Create the builder and hand it to the user for configuration
        var builder = new ChannelAdapterBuilder(services, configuration);
        configure(builder);

        // Clock of the channel contour: token TTL, correlation expiry and prompt retention read it
        // instead of the static system clock. Registered by the component that consumes it rather
        // than inherited from a neighbour. The slot is normally already taken by the resolver inside
        // AddChannelMultiTenancy above, so this line is the guarantee that the contour resolves a
        // clock at all, not the point where the provider is chosen: a host substituting the provider
        // registers it before AddVeriqaChannelAdapters, not inside the configure callback. TryAdd is
        // idempotent, so the instance stays single.
        services.TryAddSingleton(TimeProvider.System);

        // Login confirmation settings (SPEC-012 §4.4): a single LoginConfirmationMode. The key states
        // the section and the member its core level is read at, so no getter of its own is registered
        // here; the options binding below stays what the rest of the contour reads the section through.
        services.Configure<LoginConfirmationOptions>(
            configuration.GetSection(LoginConfirmationOptions.SectionName));

        // Display of the outcome receipt: the desired intent of the deployment, which is the core level
        // of a key a tenant and an application may override. Bound the same way the login-confirmation
        // section is — the key states the section and the member its core level is read at, and this
        // binding is what the section is read through outside that resolution.
        services.Configure<OutcomeNoticeOptions>(
            configuration.GetSection(OutcomeNoticeOptions.SectionName));

        // Channel message localization (SPEC-017 §7.2, ICC-050):
        // resolving Natural Keys into the recipient's language from the host's locale files
        services.Configure<ChannelLocalizationOptions>(
            configuration.GetSection(ChannelLocalizationOptions.SectionName));

        // A locale tag that denotes no locale, and a regional mapping nothing looks up, are both
        // invisible at run time — the messages simply come out in the base wording and the dates in
        // the conventions the operator meant to replace. Judged at start instead.
        services.AddSingleton<IValidateOptions<ChannelLocalizationOptions>, ChannelLocalizationOptionsValidator>();
        services.AddOptionsWithValidateOnStart<ChannelLocalizationOptions>();
        // The single point at which the render points of the channel contour obtain a message — its
        // contract and its template ladder, each through the canonical resolver, so a deployment's
        // variants reach the rendered text instead of being validated at startup and then ignored
        // (SPEC-036 TPL-056).
        services.TryAddSingleton<IMessageTemplateAccessor, MessageTemplateAccessor>();

        services.TryAddSingleton<IConfirmationPromptLocalizer, LocaleFileConfirmationPromptLocalizer>();

        // Terminal text of a transaction (SPEC-036 TPL-123, SPEC-039 R51): the ONE port every render
        // point of an outcome takes its wording from. Public, unlike the accessor behind it — the
        // static webhook pipeline has no container of its own, so a host composing that pipeline hands
        // the port in, and takes it from here.
        services.TryAddSingleton<IOutcomeReceiptText, OutcomeReceiptTextResolver>();

        // Confirmation context (SPEC-017 §7): the context factory, the prompt orchestrator
        // and the no-op anomaly heuristic (replaced by the host when a history source exists, ICC-072)
        services.TryAddSingleton<IInitiatorAnomalyDetector, NoOpInitiatorAnomalyDetector>();
        services.TryAddSingleton<IConfirmationPromptContextFactory, ConfirmationPromptContextFactory>();
        services.TryAddSingleton<IConfirmationPromptOrchestrator, ConfirmationPromptOrchestrator>();

        // Expiry-edit of the in-channel prompt (SPEC-003 §4.5): the store where Telegram/Max record the
        // sent prompt coordinates, and the lifecycle handler that, on TransactionExpiredEvent, edits that
        // message to an "expired" status and removes the stale buttons. The default store is in-process
        // (single instance / self-hosted / demo); a distributed store can be layered on later.
        services.TryAddSingleton<IChannelPromptMessageStore, InMemoryChannelPromptMessageStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransactionEventHandler, ChannelPromptExpiryHandler>());

        // Deep-link prefill context (SPEC-003 §10.4): a concrete internal collaborator of the channel
        // contour, registered by its own type — there is no interface to substitute, so the context can
        // only come from the stored transaction.
        services.TryAddSingleton<DeepLinkPrefillContextReader>();

        // Branding of the generated service pages and the resource scope of a request. The auth
        // server registers the very same services (a host may run it with no adapter enabled), so
        // the pair lives in one method both composition roots call — the reasoning is there.
        services.AddCorePageUiServices();

        // Effective confirmation surface resolution (SPEC-012 §4.4.2): the layer resolver →
        // expansion of the ChannelDefault sentinel from the channel's single confirmation fact →
        // routing of an in-channel confirmation the channel cannot perform to the core's surface.
        services.TryAddSingleton<IEffectiveConfirmationSurfaceResolver, EffectiveConfirmationSurfaceResolver>();

        // Display intent of the outcome receipt: the ONE port both points that build a notice — the
        // live pipeline and the background expiry handler — take the resolved intent from. Public
        // like the receipt text port above and for the same reason: the static webhook pipeline has no
        // container of its own, so the host composing it hands the port in and takes it from here.
        services.TryAddSingleton<IOutcomeNoticeDisplayIntentSource, OutcomeNoticeDisplayIntentSource>();

        // Channel state on the standard health-checks surface: one check asking every registered
        // adapter the very question its own contract answers. It is registered here, in the channel
        // contour's composition root, so a host only has to publish an endpoint selecting it by tag.
        //
        // failureStatus is Degraded on purpose, and it does not merely repeat the ceiling the check
        // body already holds: should an exception escape that body (resolving the check's own
        // dependencies, cancellation of the linked source), the health check service takes the status
        // from the registration rather than from the body — and the general health endpoint, which
        // aggregates every check without a tag filter, would then answer 503 on a replica that is
        // perfectly fine.
        //
        // The guard keeps the registration idempotent. AddVeriqaChannelAdapters may run more than once
        // over one collection (host + extension library), while check names have to be unique: a second
        // registration under the same name makes the health check service throw when it is built.
        if (services.All(descriptor => descriptor.ServiceType != typeof(ChannelAdapterHealthCheck)))
        {
            services.AddSingleton<ChannelAdapterHealthCheck>();

            // The very same instance also runs as a hosted service, and only to take the first
            // snapshot of the channels at start-up: the check never probes a channel from the
            // request that reads its report, so without a start-up round the first reader would see
            // the not-answered-yet placeholder instead of the channel states.
            services.AddHostedService(provider => provider.GetRequiredService<ChannelAdapterHealthCheck>());

            services.AddHealthChecks()
                .AddCheck<ChannelAdapterHealthCheck>(
                    name: ChannelHealthCheckNames.Name,
                    failureStatus: HealthStatus.Degraded,
                    tags: [ChannelHealthCheckNames.Tag]);
        }

        // CA-110: a warning if no channel is enabled.
        // No exception is thrown — the AuthServer may start without channel adapters
        // for development/demo scenarios (e.g. the demo-core profile).
        //
        // The builder flag alone is not the whole answer: HasAdapters is internal and only the
        // builder's own AddChannel/AddXxx methods raise it, so an adapter registered straight into
        // the (public) service collection — by an ecosystem package extending this builder, or by
        // the host itself — would leave the flag down and produce a warning about an empty channel
        // set while the container already holds one. The collection scan is the same technique the
        // transaction engine uses for its store and publisher defaults.
        if (!builder.HasAdapters
            && !services.Any(descriptor => descriptor.ServiceType == typeof(IChannelAdapter)))
        {
            // Log a startup warning via deferred logging
            services.AddHostedService<ChannelAdapterStartupWarningService>();
        }

        return services;
    }
}
