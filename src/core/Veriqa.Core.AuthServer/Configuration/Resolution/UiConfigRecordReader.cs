// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Veriqa.Core.AuthServer.UiConfig;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// Reader of the <c>ui_config</c> record — the record of the <see cref="ConfigLevel.UiConfig"/> level
/// (SPEC-012 CFG-203, §10.6). The record type belongs to the auth server, which is why the reader
/// lives here while the mechanism and the source live in the configuration assembly. It is the ONLY
/// place that reaches for the record store: every consumer of a record field goes through the
/// resolver (CFG-231, CFG-235).
/// <para>
/// The reader knows no setting keys — which field of the record belongs to which key is stated by the
/// declaration of that key, which names the address it is read at inside the record. Which
/// implementation of the store the contour substituted (self-hosted, cloud, demo) is invisible here as
/// well: the reader asks the container for the registered one and never inspects its type.
/// </para>
/// </summary>
internal sealed class UiConfigRecordReader : IConfigRecordReader<UiConfigRecord>
{
    /// <summary>
    /// Scope factory for resolving <c>IUiConfigStore</c> per read.
    /// </summary>
    /// <remarks>
    /// The store must NOT be constructor-injected: the reader ends up inside the singleton binding
    /// registry, while the store's lifetime is decided by the contour — the cloud contour registers it
    /// as Scoped, because its database context is Scoped. A constructor injection would therefore be a
    /// captive dependency (csharp-rules §2) in exactly the N&gt;1 profile the level exists for.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Logger of a failed record read.
    /// </summary>
    private readonly ILogger<UiConfigRecordReader> _logger;

    /// <summary>
    /// Whether the store behind this reader is live, asked of the store ONCE. Which implementation
    /// stands behind the port is decided by the contour's registration and never changes while the
    /// application runs, so asking on every read would open a container scope per resolution for an
    /// answer that cannot have changed.
    /// </summary>
    private readonly Lazy<bool> _storeIsLive;

    /// <summary>
    /// Creates the reader of the <c>ui_config</c> record.
    /// </summary>
    /// <param name="scopeFactory">Scope factory (for the per-read store).</param>
    /// <param name="logger">Logger of a failed record read.</param>
    public UiConfigRecordReader(IServiceScopeFactory scopeFactory, ILogger<UiConfigRecordReader> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storeIsLive = new Lazy<bool>(ReadStoreLiveness);
    }

    /// <inheritdoc />
    public string Name => "UiConfig";

    /// <inheritdoc />
    /// <remarks>
    /// The answer is the STORE's and is merely translated here: this one reader class serves every
    /// contour, while the store behind it differs — an options monitor in the ordinary deployment
    /// (live, an edit of the configuration reaches the page at once) and a database in the cloud (not
    /// live, and its reads may be cached by the key's lifetime). A second authority on the same
    /// question is exactly what would let the two disagree.
    /// </remarks>
    public bool? IsLive => _storeIsLive.Value;

    /// <summary>
    /// Asks the registered store whether it is live.
    /// </summary>
    /// <returns>The store's answer.</returns>
    private bool ReadStoreLiveness()
    {
        using var scope = _scopeFactory.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IUiConfigStore>().IsLive;
    }

    /// <inheritdoc />
    public async ValueTask<UiConfigRecord?> ReadAsync(
        ResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The level applies only when the context names a record selector.
        if (string.IsNullOrWhiteSpace(context.UiConfigSelector))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IUiConfigStore>();

        var result = await store.GetAsync(
            context.UiConfigSelector,
            context.TenantId,
            context.ApplicationId,
            cancellationToken);

        // An infrastructure failure of the store is not a failure of the resolution: the level stays
        // unset and the value comes from the levels below (CFG-233). The record CONTENTS never go into
        // the log — the selector, the application and the outcome are what an operator needs.
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "The ui_config record could not be read; the level is skipped. "
                + "Selector: {Selector}, ApplicationId: {ApplicationId}, Error: {ErrorCode}",
                context.UiConfigSelector,
                context.ApplicationId,
                result.Error.Code);

            return null;
        }

        return result.Value;
    }
}
