// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Services;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Reader of the deep-link prefill context (SPEC-003 §10.4): the pieces a channel needs to render a
/// formatted prefill message but cannot see from <c>GetDeepLinkAsync</c>, which receives only a
/// <see cref="TransactionId"/> — the ownership levels the prefill message is resolved over
/// (SPEC-036 TPL-116), the human-readable application name
/// (<see cref="InitiatorContextSnapshot.ClientApplicationName"/>) and the locale stated by the request
/// that created the transaction (the recipient-locale-chain fallback the messenger channels already use
/// at generation time — the channel identity does not exist yet when the QR is built).
/// <para>
/// Plain transaction data, no configuration involved: the reader is a shared implementation detail of
/// the channel contour and not a replaceable contract.
/// </para>
/// </summary>
internal sealed class DeepLinkPrefillContextReader
{
    /// <summary>
    /// Transaction store — singleton in every contour (InMemory/EfCore/Redis), so constructor injection
    /// is safe here (no captive dependency).
    /// </summary>
    private readonly ITransactionStore _transactionStore;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<DeepLinkPrefillContextReader> _logger;

    /// <summary>
    /// Creates the prefill context reader.
    /// </summary>
    /// <param name="transactionStore">Transaction store.</param>
    /// <param name="logger">Logger.</param>
    public DeepLinkPrefillContextReader(
        ITransactionStore transactionStore,
        ILogger<DeepLinkPrefillContextReader> logger)
    {
        _transactionStore = transactionStore ?? throw new ArgumentNullException(nameof(transactionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads the prefill context (ownership levels + application name + page locale) of the transaction.
    /// </summary>
    /// <remarks>
    /// Never throws: an unavailable store is not a reason to block deep-link generation — it degrades to
    /// null (plus a WARNING in the log) and the channel renders the minimal, application-name-less variant
    /// in the base language.
    /// </remarks>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="channelType">Channel type (telegram/whatsapp/max/email from <c>ChannelTypes</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The prefill context, or null when no context is available (use the minimal variant).</returns>
    public async Task<DeepLinkPrefillContext?> ReadAsync(
        TransactionId transactionId,
        string channelType,
        CancellationToken cancellationToken = default)
    {
        // Contract: never throws and never blocks deep-link generation — any failure degrades to null
        // (the channel renders the minimal variant in the base language).
        try
        {
            var transaction = await _transactionStore.GetByIdAsync(transactionId, cancellationToken);
            if (transaction is null)
            {
                return null;
            }

            var applicationName = transaction.InitiatorContextSnapshot?.ClientApplicationName;
            var uiLocale = transaction.GetUiLocale();
            var uiTimeZone = transaction.GetUiTimeZone();

            // A transaction that was read is always worth carrying now: even with no application name
            // and no locale on it, it states the ownership levels the prefill wording is resolved over,
            // and dropping the context here would send that resolution to the core level. The three
            // optional fields keep degrading on their own — the channel already reads them as nullable.
            return new DeepLinkPrefillContext(
                TransactionResolutionContext.For(transaction), applicationName, uiLocale, uiTimeZone);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal request cancellation — propagate, this is not a context-resolution failure
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unexpected failure reading the deep-link prefill context; using the minimal variant. "
                + "ChannelType: {ChannelType}",
                channelType);

            return null;
        }
    }
}

/// <summary>
/// Deep-link prefill context (SPEC-003 §10.4): the ownership the prefill message is resolved over, the
/// application name shown in the prefill title and the locale of the transaction. The three latter fields
/// are optional — any of them may be null and the renderer degrades accordingly (no title line without
/// <see cref="ApplicationName"/>; base language without <see cref="UiLocale"/>).
/// </summary>
/// <param name="Ownership">Ownership levels of the transaction (SPEC-036 TPL-116); the resolution of
/// the prefill message runs over them.</param>
/// <param name="ApplicationName">Human-readable client application name, or null when unavailable.</param>
/// <param name="UiLocale">Locale tag stated by the request that created the transaction (read through
/// <c>Transaction.GetUiLocale()</c>), or null when it states none.</param>
/// <param name="UiTimeZone">Time zone the moments of the message are shown in (IANA identifier from
/// <c>OidcContext.UiTimeZone</c>), or null — UTC with its marker. It travels with the locale and
/// never apart from it.</param>
internal sealed record DeepLinkPrefillContext(
    ResolutionContext Ownership,
    string? ApplicationName,
    string? UiLocale,
    string? UiTimeZone);
