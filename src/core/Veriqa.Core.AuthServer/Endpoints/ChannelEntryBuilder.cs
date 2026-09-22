// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.AuthServer.UI.Services;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// The entries of a creation answer: the single way in and, when requested, the list of deep-link
/// entries of the channels offered (SPEC-039 C20, C52).
/// </summary>
/// <param name="Entry">The single way in, chosen by L40.</param>
/// <param name="Entries">The list of channel entries; null when the request did not ask for it.</param>
internal sealed record ChannelEntrySet(ChannelEntry Entry, IReadOnlyList<ChannelEntry>? Entries);

/// <summary>
/// Builds the way in that the creation answer carries (SPEC-039 L40, C20), and the list of channel
/// entries when the request asks for it (C52). Both are built FROM THE TRANSACTION at the moment of the
/// answer and stored nowhere: an idempotent repeat therefore gets current entries rather than the ones
/// frozen at the first attempt.
/// </summary>
internal static class ChannelEntryBuilder
{
    /// <summary>
    /// Builds the channel entries of a transaction.
    /// </summary>
    /// <param name="transaction">Transaction the entries lead to.</param>
    /// <param name="includeChannelEntries">Whether the list of channel entries is built besides the
    /// single way in (C52).</param>
    /// <param name="request">Current HTTP request — the fallback source of the public base address.</param>
    /// <param name="resolution">Ownership context of the transaction.</param>
    /// <param name="channelDisplayService">Preparer of the per-channel entry material.</param>
    /// <param name="pageSettingsResolver">Resolver of the level-owned page settings (display override).</param>
    /// <param name="qrMatrixSource">Server-side QR generator — the module matrix and pixel scale the
    /// image of the answer is drawn from.</param>
    /// <param name="configResolver">Canonical level resolver — the source of the confirmation surface.</param>
    /// <param name="serverOptions">OIDC server options — the source of the stated issuer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries, or the failure of the channel preparation.</returns>
    public static async Task<Result<ChannelEntrySet>> BuildAsync(
        Transaction transaction,
        bool includeChannelEntries,
        HttpRequest request,
        ResolutionContext resolution,
        IChannelDisplayService channelDisplayService,
        AuthPageSettingsResolver pageSettingsResolver,
        IQrModuleMatrixSource qrMatrixSource,
        IConfigurationResolver configResolver,
        OidcServerOptions serverOptions,
        CancellationToken cancellationToken)
    {
        // The method picks the kind of the single entry per L40 and fills it; on request it also lists
        // the deep-link entries of every channel of the same display list (C52)

        // The effective set is what the transaction itself states: a preferred channel narrows it to
        // one, otherwise the allowed set applies (an empty one means "every registered channel").
        // Narrowing to the preferred channel is sound because a transaction never carries one outside
        // its own allowed set: a request stating such a pair is refused at creation, so the entry
        // built here can never lead into a channel whose answer would later be refused.
        IReadOnlySet<string> effectiveChannels = string.IsNullOrWhiteSpace(transaction.RequestedChannelType)
            ? transaction.AllowedChannelTypes
            : new HashSet<string>(StringComparer.Ordinal) { transaction.RequestedChannelType };

        var pageSettings = await pageSettingsResolver.ResolveAsync(resolution, cancellationToken);

        var displayOverride = pageSettings.Channels is null && pageSettings.ChannelDisplayMode is null
            ? null
            : new ChannelDisplayOverride(pageSettings.Channels, pageSettings.ChannelDisplayMode);

        var displayResult = await channelDisplayService.GetChannelDisplayDataAsync(
            transaction.Id,
            effectiveChannels,
            resolution,
            displayOverride,
            cancellationToken);

        if (displayResult.IsFailure)
        {
            // Not a single channel offered a way in — the preferred one is not registered or not
            // allowed, or the only one failed to build its link. There is nothing to show, and the page
            // is not an answer either: it would open onto the same empty set.
            return Result<ChannelEntrySet>.Failure(
                displayResult.Error.Code, displayResult.Error.Message, displayResult.Error.Category);
        }

        var channels = displayResult.Value;

        // The third condition of L40, and the one that beats the other two: with the question
        // configured onto the core's own page, a deep link would send the user into a channel where no
        // prompt is ever going to appear, and the transaction would hang to its TTL. It is read from
        // the CONFIGURED value of the axis — the surface effective for a channel is not known before a
        // channel has acted.
        var confirmationMode = (await configResolver.ResolveAsync(
            LoginConfirmationConfigKeys.LoginConfirmation,
            resolution,
            ConfigDimensionValues.None,
            cancellationToken)).Value;

        var questionOnCorePage = confirmationMode is LoginConfirmationMode.OnWebPage;

        // The list of C52 takes the second and the third condition of L40 and NOT the first: it is the
        // very form for several channels. With the question on the core's page every deep link would
        // lead nowhere, so the list is empty; otherwise it keeps the channels with an addressable point,
        // in the order of the display list the configuration gave.
        List<ChannelEntry>? entries = null;
        if (includeChannelEntries)
        {
            entries = [];
            if (!questionOnCorePage)
            {
                foreach (var channel in channels)
                {
                    if (IsAbsoluteChannelAddress(channel.DeepLinkUrl))
                    {
                        entries.Add(await BuildDeepLinkEntryAsync(channel));
                    }
                }
            }
        }

        // The second condition is read off the deep link itself rather than off a list of channels: a
        // channel that has no addressable point until the user has typed something answers with a
        // relative start path (the Email adapter in Pull mode does exactly that).
        var single = channels.Count is 1 ? channels[0] : null;

        if (!questionOnCorePage && single is not null && IsAbsoluteChannelAddress(single.DeepLinkUrl))
        {
            // The only channel passed the same filters, so the list already holds its entry — the image
            // is not drawn a second time.
            var deepLinkEntry = entries is [var listed] ? listed : await BuildDeepLinkEntryAsync(single);

            return Result<ChannelEntrySet>.Success(new ChannelEntrySet(deepLinkEntry, entries));
        }

        var pageUrl = BuildEntryPageUrl(transaction, request, serverOptions);

        var pageEntry = new ChannelEntry
        {
            Kind = ChannelEntryKinds.PageUrl,
            // Strictly null: the channel is not chosen yet, and naming one here would be a guess.
            ChannelType = null,
            Url = pageUrl,
            // No channel: the pixel scale is a per-channel dimension of its key, and an unnamed
            // channel matches no per-channel step — the flat value of the level answers.
            Qr = await RenderQrAsync(pageUrl, string.Empty),
            ValidUntil = transaction.ExpiresAt
        };

        return Result<ChannelEntrySet>.Success(new ChannelEntrySet(pageEntry, entries));

        // A deep-link entry of one channel — the form shared by the single way in and the list (C20, C52).
        async Task<ChannelEntry> BuildDeepLinkEntryAsync(ChannelDisplayInfo channel) =>
            new ChannelEntry
            {
                Kind = ChannelEntryKinds.DeepLink,
                ChannelType = channel.ChannelType,
                Url = channel.DeepLinkUrl,
                // The PNG prepared for the sign-in page (channel.QrCodeBase64) does not go out: the image
                // of the answer is drawn again over the same address and channel, with the mark.
                Qr = await RenderQrAsync(channel.DeepLinkUrl, channel.ChannelType),
                ValidUntil = transaction.ExpiresAt
            };

        // The image of the answer leaves for a screen of the integrator, where no footer of the core
        // exists, so it carries the attribution mark in its pixels (SPEC-015 §4.18, SPEC-039 C20). It is
        // drawn HERE, from the module matrix, rather than inside the QR service: that service also feeds
        // the sign-in page, whose footer already shows the mark, and a mark in its picture would repeat it.
        async Task<string> RenderQrAsync(string content, string channelType)
        {
            var matrix = await qrMatrixSource.GenerateModuleMatrixAsync(
                content,
                channelType,
                resolution,
                cancellationToken);

            var png = QrAttributionImage.ComposePng(matrix);

            return AuthPageConstants.PngDataUriPrefix + Convert.ToBase64String(png);
        }
    }

    /// <summary>
    /// Whether the address of a channel is one a user can be sent to as it is.
    /// </summary>
    /// <remarks>
    /// A rooted PATH is not: the Email adapter in Pull mode answers with the start path of its own
    /// page, because until the person has typed an address there is no addressable point to send them
    /// to. Excluding the <c>file</c> scheme is what makes the check say so on every platform — on Unix
    /// <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/> reads a rooted path as an absolute
    /// <c>file:</c> URI, and that path would otherwise pass for a deep link.
    /// </remarks>
    /// <param name="url">Address the channel offered.</param>
    /// <returns><see langword="true"/> when the address is absolute and addresses a channel.</returns>
    private static bool IsAbsoluteChannelAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile;

    /// <summary>
    /// Builds the absolute URL of the entry page of a transaction.
    /// </summary>
    /// <remarks>
    /// One rule with a deterministic order: the issuer the operator stated is the public address of
    /// the server and wins; without one, the address the current request arrived at is used — behind a
    /// reverse proxy the already wired-up forwarded-headers middleware has corrected it by then. No
    /// parsing of <c>X-Forwarded-*</c> here and no "public base address" option of our own.
    /// </remarks>
    /// <param name="transaction">Transaction the page addresses.</param>
    /// <param name="request">Current HTTP request.</param>
    /// <param name="serverOptions">OIDC server options.</param>
    /// <returns>Absolute URL of the entry page.</returns>
    private static string BuildEntryPageUrl(
        Transaction transaction,
        HttpRequest request,
        OidcServerOptions serverOptions)
    {
        var issuer = serverOptions.Issuer;
        var baseAddress = string.IsNullOrWhiteSpace(issuer)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : issuer.TrimEnd('/');

        var sessionId = Uri.EscapeDataString(SessionIdMapper.ToSessionId(transaction.Id));

        return $"{baseAddress}{OidcEndpoints.TransactionEntryPage}"
            + $"?{OidcConstants.SessionIdParameterName}={sessionId}";
    }
}
