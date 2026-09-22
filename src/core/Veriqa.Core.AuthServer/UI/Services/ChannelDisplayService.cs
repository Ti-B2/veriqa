// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Configuration.Enums;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.UI.Services;

/// <summary>
/// Channel data to display on the authentication page.
/// </summary>
public sealed class ChannelDisplayInfo
{
    /// <summary>
    /// Channel type (telegram, whatsapp, max).
    /// </summary>
    public required string ChannelType { get; init; }

    /// <summary>
    /// Deep link URL to start authentication in the channel.
    /// </summary>
    public required string DeepLinkUrl { get; init; }

    /// <summary>
    /// QR code as a Base64 PNG string.
    /// </summary>
    public required string QrCodeBase64 { get; init; }

    /// <summary>
    /// Channel name for the button, taken from the adapter's optional
    /// <see cref="IChannelDisplayMetadata"/> (null — no metadata: built-in channels use their own
    /// localized CTA text, a third-party channel falls back to <see cref="ChannelType"/>).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Inner markup of the channel glyph from the adapter's optional
    /// <see cref="IChannelDisplayMetadata"/> (null — render without an icon).
    /// </summary>
    public string? IconSvgPath { get; init; }

    /// <summary>
    /// Hint shown inside this channel's panel, resolved for THIS channel from
    /// <see cref="Configuration.ChannelDisplayOptions.Hints"/> (null — no level states a hint for the
    /// channel, and the panel markup is unchanged).
    /// </summary>
    public string? Hint { get; init; }
}

/// <summary>
/// Per-request display override resolved from the ui_config record (SPEC-002 §4.6, CFG-211).
/// It can only narrow the global display (filter/reorder the shown channels, switch the mode),
/// never add a channel that is not registered or not allowed by acr_values.
/// </summary>
/// <param name="Channels">Channel codes from the record; null/empty — no set/order override (global).</param>
/// <param name="Mode">Channel display mode from the record; null — the global Mode.</param>
public sealed record ChannelDisplayOverride(
    IReadOnlyList<string>? Channels,
    ChannelDisplayMode? Mode);

/// <summary>
/// Interface of the service that prepares channel data for the authentication page.
/// </summary>
public interface IChannelDisplayService
{
    /// <summary>
    /// Prepares channel data for display: deep links and QR codes.
    /// </summary>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="allowedChannelTypes">Allowed channel types.</param>
    /// <param name="resolution">
    /// Resolution context of the request, built ONCE at the entry of the sign-in page and passed
    /// explicitly. <see cref="ResolutionContext.Core"/> outside a request — the degenerate N=1 of the
    /// same path, not a separate branch (CFG-202).
    /// </param>
    /// <param name="displayOverride">Per-request ui_config display override (null — global behavior, CFG-211).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of channel data to display.</returns>
    Task<Result<IReadOnlyList<ChannelDisplayInfo>>> GetChannelDisplayDataAsync(
        TransactionId transactionId,
        IReadOnlySet<string> allowedChannelTypes,
        ResolutionContext resolution,
        ChannelDisplayOverride? displayOverride,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service that prepares channel data for the authentication page (SPEC-007 §3, §4).
/// Aggregates deep links and QR codes from the registered channel adapters.
/// Honors the effective channel display mode (the <c>ChannelDisplay.Mode</c> key, resolved through
/// <see cref="AuthPageSettingsResolver"/>) and <see cref="ChannelDisplayOptions.ChannelOrder"/>.
/// </summary>
internal sealed class ChannelDisplayService : IChannelDisplayService
{
    /// <summary>
    /// Registered channel adapters.
    /// </summary>
    private readonly IEnumerable<IChannelAdapter> _adapters;

    /// <summary>
    /// QR code generation service.
    /// </summary>
    private readonly IQrCodeService _qrCodeService;

    /// <summary>
    /// Channel display settings — the ORDER of the channels alone. The mode is a setting owned by a
    /// level and is resolved through <see cref="_pageSettings"/>, not read from the bound section.
    /// </summary>
    private readonly IOptions<VeriqaOptions> _options;

    /// <summary>
    /// Gathering point of the level-owned sign-in page settings (SPEC-012 §10.6) — the source of the
    /// effective hint of a channel. The hint is a setting owned by a level (core/tenant and below) and
    /// cut by the <c>channel</c> dimension inside it, so it is resolved for a named channel rather
    /// than read from options directly.
    /// </summary>
    private readonly AuthPageSettingsResolver _pageSettings;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<ChannelDisplayService> _logger;

    /// <summary>
    /// Creates the channel data preparation service.
    /// </summary>
    /// <param name="adapters">Registered channel adapters.</param>
    /// <param name="qrCodeService">QR code generation service.</param>
    /// <param name="options">Veriqa settings (ChannelDisplay.ChannelOrder).</param>
    /// <param name="pageSettings">Resolver of the level-owned sign-in page settings.</param>
    /// <param name="logger">Logger.</param>
    public ChannelDisplayService(
        IEnumerable<IChannelAdapter> adapters,
        IQrCodeService qrCodeService,
        IOptions<VeriqaOptions> options,
        AuthPageSettingsResolver pageSettings,
        ILogger<ChannelDisplayService> logger)
    {
        _adapters = adapters;
        _qrCodeService = qrCodeService;
        _options = options;
        _pageSettings = pageSettings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ChannelDisplayInfo>>> GetChannelDisplayDataAsync(
        TransactionId transactionId,
        IReadOnlySet<string> allowedChannelTypes,
        ResolutionContext resolution,
        ChannelDisplayOverride? displayOverride,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        // The method collects a deep link and QR for each available channel, honoring the Mode/ChannelOrder settings
        // and the per-request ui_config display override (CFG-211 narrowing: filter/reorder/mode, never add).

        var channelDisplay = _options.Value.ChannelDisplay;
        var result = new List<ChannelDisplayInfo>();

        // Effective mode: the record's mode narrows the global one. A caller that states no override
        // asks the mechanism for the same chain the override was filled from (core → ui_config), so
        // the mode is the resolved value of the key either way and never a field of the bound section.
        var effectiveMode = displayOverride?.Mode
            ?? await _pageSettings.ResolveChannelDisplayModeAsync(resolution, cancellationToken);

        // Normalize the record's channel override the same way as ChannelOrder (drop empty/whitespace,
        // trim, dedup case-insensitive). A non-empty record list REPLACES the global ChannelOrder as the
        // source of both display order and the shown-set filter (CFG-211); an empty/null list — no override.
        var normalizedOverrideChannels = displayOverride?.Channels?
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .DistinctBy(type => type, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasChannelSetOverride = normalizedOverrideChannels is { Count: > 0 };

        // Normalize the effective order source once: the record's channel list when it overrides,
        // otherwise the global ChannelOrder. Used uniformly for sorting and Selective/override filtering.
        var normalizedChannelOrder = hasChannelSetOverride
            ? normalizedOverrideChannels!
            : channelDisplay.ChannelOrder
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Select(type => type.Trim())
                .DistinctBy(type => type, StringComparer.OrdinalIgnoreCase)
                .ToList();

        // Build the set of allowed channels when it must be filtered to a list: either Selective mode,
        // or an explicit record channel-set override (CFG-211 narrowing — only the listed channels show).
        // null — no such filter (the check inside the loop is skipped).
        var selectiveAllowed = (effectiveMode is ChannelDisplayMode.Selective || hasChannelSetOverride)
            ? new HashSet<string>(normalizedChannelOrder, StringComparer.OrdinalIgnoreCase)
            : null;

        // Determine the final set of adapters honoring ChannelOrder (display order)
        var orderedAdapters = ApplyChannelOrder(_adapters, normalizedChannelOrder);

        // Every deep link and QR below is built by an adapter from ITS TENANT'S credentials, and the
        // tenant of this request is the one the resolution context states (CFG-202, CA-164). Without
        // the scope the whole page would be built from the core credentials, whoever is signing in.
        // The context is the page's own, so a request that states no tenant opens the scope for the
        // default implicit one — the self-hosted behaviour unchanged.
        using var tenantScope = ChannelTenantContext.BeginScope(resolution.TenantId);

        foreach (var adapter in orderedAdapters)
        {
            // Filter by allowed types (an empty list = all channels)
            if (allowedChannelTypes.Count > 0
                && !allowedChannelTypes.Contains(adapter.ChannelType))
            {
                continue;
            }

            // Selective Mode: include only channels from the normalized ChannelOrder.
            if (selectiveAllowed is not null && !selectiveAllowed.Contains(adapter.ChannelType))
            {
                continue;
            }

            // A third-party adapter may both fail and throw here, and its deep link goes straight
            // into the QR encoder, which rejects an over-long payload. Neither may take the whole
            // sign-in page down: the channel is skipped exactly like a Failure, the rest render.
            string deepLinkUrl;
            string qrCodeBase64;

            try
            {
                var deepLinkResult = await adapter.GetDeepLinkAsync(transactionId, cancellationToken);
                if (deepLinkResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Failed to get a deep link for channel {ChannelType}: {Error}",
                        adapter.ChannelType,
                        deepLinkResult.Error.Message);
                    continue;
                }

                deepLinkUrl = deepLinkResult.Value;
                qrCodeBase64 = await _qrCodeService.GenerateBase64PngAsync(deepLinkUrl, adapter.ChannelType, resolution, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Channel {ChannelType} failed to produce a deep link or its QR code — the channel is skipped",
                    adapter.ChannelType);
                continue;
            }

            // Optional display metadata (public SPI): a third-party adapter may declare a button
            // label and a glyph. Built-in adapters do not implement it — they keep their localized
            // CTA texts and bundled icons, so behavior stays 1:1.
            var (displayName, iconSvgPath) = ReadDisplayMetadata(adapter);

            // The hint is asked for THIS channel: the channel is a dimension of the key (CFG-237), so
            // the level that owns the hint of this channel answers it and the service picks nothing
            // out of a resolved map. A channel no level states a hint for gets null, and its panel is
            // rendered exactly as it is today.
            var hint = await _pageSettings.ResolveChannelHintAsync(resolution, adapter.ChannelType, cancellationToken);

            result.Add(new ChannelDisplayInfo
            {
                ChannelType = adapter.ChannelType,
                DeepLinkUrl = deepLinkUrl,
                QrCodeBase64 = qrCodeBase64,
                DisplayName = displayName,
                IconSvgPath = iconSvgPath,
                Hint = hint
            });
        }

        // If no available channel was found — return an error
        // so AuthorizeEndpoint can handle the situation correctly (503)
        if (result.Count is 0)
        {
            _logger.LogError(
                "No available channels found for transaction {TransactionId}. Adapters: {AdapterCount}, Mode: {Mode}, AllowedChannelTypes: {AllowedCount}",
                transactionId,
                _adapters.Count(),
                effectiveMode,
                allowedChannelTypes.Count);

            return Result<IReadOnlyList<ChannelDisplayInfo>>.Failure(
                ChannelDisplayErrorCodes.NoChannelsAvailable, "No authentication channels available");
        }

        return Result<IReadOnlyList<ChannelDisplayInfo>>.Success(result.AsReadOnly());
    }

    /// <summary>
    /// Reads the optional display metadata of a third-party adapter (public SPI) and degrades it to
    /// safe values: a blank label becomes null (the caller falls back to the channel type — an empty
    /// string would render a nameless tab and button), an over-long label or glyph is dropped
    /// (an unbounded label stretches the CTA past the card, an unbounded glyph bloats every page
    /// render and its allowlist scan), and glyph markup that fails the allowlist is dropped as well
    /// (the channel renders without an icon). The adapter's properties are third-party code
    /// on the sign-in page path, so a throwing getter must not take the whole page down either —
    /// including a spurious timeout, which is why the catch is unfiltered (there is no cancellation
    /// token on this path, so no exception here can be a real cancellation of an awaited operation).
    /// </summary>
    /// <param name="adapter">Channel adapter.</param>
    /// <returns>Display label and glyph markup; null for each value that is absent or rejected.</returns>
    private (string? DisplayName, string? IconSvgPath) ReadDisplayMetadata(IChannelAdapter adapter)
    {
        if (adapter is not IChannelDisplayMetadata displayMetadata)
        {
            return (null, null);
        }

        string? displayName;
        string? iconSvgPath;

        try
        {
            displayName = displayMetadata.DisplayName;
            iconSvgPath = displayMetadata.IconSvgPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Channel {ChannelType} failed to provide display metadata — rendering without it",
                adapter.ChannelType);
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = null;
        }
        else if (displayName.Length > CustomChannelConstants.MaxDisplayNameLength)
        {
            _logger.LogWarning(
                "Channel {ChannelType} declared a display label of {Length} characters, over the " +
                "{Limit}-character limit — falling back to the channel type",
                adapter.ChannelType,
                displayName.Length,
                CustomChannelConstants.MaxDisplayNameLength);
            displayName = null;
        }

        if (iconSvgPath is not null && iconSvgPath.Length > CustomChannelConstants.MaxIconSvgPathLength)
        {
            _logger.LogWarning(
                "Channel {ChannelType} declared glyph markup of {Length} characters, over the " +
                "{Limit}-character limit — rendering without an icon",
                adapter.ChannelType,
                iconSvgPath.Length,
                CustomChannelConstants.MaxIconSvgPathLength);
            iconSvgPath = null;
        }

        if (iconSvgPath is not null && !ChannelIconSvg.IsValidCustomPath(iconSvgPath))
        {
            _logger.LogWarning(
                "Channel {ChannelType} declared glyph markup that is not allowed on the sign-in page " +
                "(only plain SVG shape elements with quoted attributes) — rendering without an icon",
                adapter.ChannelType);
            iconSvgPath = null;
        }

        return (displayName, iconSvgPath);
    }

    /// <summary>
    /// Orders the adapters according to the ChannelOrder configuration.
    /// Adapters not mentioned in ChannelOrder are appended at the end in their original order.
    /// </summary>
    /// <param name="adapters">Registered adapters.</param>
    /// <param name="channelOrder">Desired channel order.</param>
    /// <returns>Ordered sequence of adapters.</returns>
    private static IEnumerable<IChannelAdapter> ApplyChannelOrder(
        IEnumerable<IChannelAdapter> adapters,
        IReadOnlyList<string> channelOrder)
    {
        // Order by index in channelOrder; adapters outside the list get int.MaxValue (go last)
        if (channelOrder.Count is 0)
        {
            return adapters;
        }

        // The incoming channelOrder is already normalized (Trim + distinct, case-insensitive) by the caller.
        // Build a Dictionary for O(1) lookup of a channel's position.
        var orderLookup = channelOrder
            .Select((type, index) => (type, index))
            .ToDictionary(
                x => x.type,
                x => x.index,
                StringComparer.OrdinalIgnoreCase);

        return adapters.OrderBy(a =>
            orderLookup.TryGetValue(a.ChannelType, out var index) ? index : int.MaxValue);
    }
}
