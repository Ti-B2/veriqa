// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.WhatsApp.Abstractions;
using Veriqa.Core.ChannelAdapter.WhatsApp.Configuration;

namespace Veriqa.Core.ChannelAdapter.WhatsApp;

/// <summary>
/// Checks once at startup that the configured <c>Veriqa:Channels:WhatsApp:Provider</c> names the
/// provider actually registered in the container. Registered by <c>AddWhatsApp</c> only when the
/// channel is enabled — with the channel off there is no provider and nothing to check.
/// </summary>
/// <remarks>
/// The check lives here rather than in the builder because both sides are known only once the
/// container is built: a provider supplied by the host is registered with <c>UseWhatsAppProvider</c>,
/// which may be called after <c>AddWhatsApp</c>. A hosted lifecycle service, as with the email template
/// check: <see cref="StartingAsync"/> runs before any hosted service starts and before the web server
/// binds, so a mismatch surfaces as a failed start rather than as an undelivered message.
/// </remarks>
internal sealed class WhatsAppProviderStartupValidator : IHostedLifecycleService
{
    /// <summary>
    /// Delivery provider resolved from the container.
    /// </summary>
    private readonly IWhatsAppProvider _provider;

    /// <summary>
    /// WhatsApp adapter settings — the source of the expected provider code.
    /// </summary>
    private readonly WhatsAppOptions _options;

    /// <summary>
    /// Creates the startup check.
    /// </summary>
    /// <param name="provider">Delivery provider resolved from the container.</param>
    /// <param name="options">WhatsApp adapter settings.</param>
    public WhatsAppProviderStartupValidator(
        IWhatsAppProvider provider,
        IOptions<WhatsAppOptions> options)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Compares the configured provider code with the code of the registered provider and stops the
    /// host when they differ — the declared and the actual delivery path must not diverge silently.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Provider codes are identifiers, not text — compared ordinally.
        if (!string.Equals(_options.Provider, _provider.ProviderType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The configured WhatsApp provider '{_options.Provider}' does not match the registered "
                + $"provider '{_provider.ProviderType}'. Set "
                + $"{WhatsAppOptions.SectionName}:{nameof(WhatsAppOptions.Provider)} to "
                + $"'{_provider.ProviderType}', or register a provider with code '{_options.Provider}' "
                + "through ChannelAdapterBuilder.UseWhatsAppProvider.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op: the check runs in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the check runs in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the check holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the check holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the check holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
