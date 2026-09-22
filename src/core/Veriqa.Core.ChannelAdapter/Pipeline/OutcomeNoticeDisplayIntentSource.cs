// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Implementation of the display intent port over the canonical level resolver (anti-fork CFG-235 —
/// it builds no precedence of its own): key
/// <see cref="OutcomeNoticeConfigKeys.DisplayIntent"/> → the effective value for the ownership the
/// context names. A level stating nothing yields to the level below it, and a deployment where none
/// of them states anything is answered with the shipped intent, which the key declares as its default.
/// </summary>
internal sealed class OutcomeNoticeDisplayIntentSource : IOutcomeNoticeDisplayIntentSource
{
    /// <summary>
    /// Canonical level resolver.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Creates the display intent source.
    /// </summary>
    /// <param name="resolver">Canonical level resolver.</param>
    public OutcomeNoticeDisplayIntentSource(IConfigurationResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <inheritdoc />
    public async ValueTask<OutcomeNoticeDisplayIntent> ResolveAsync(
        ResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The key has no dimensions: an owner states one intent for all of its channels and for every
        // outcome, so the levels of the key are the only thing the answer depends on.
        var resolved = await _resolver.ResolveAsync(
            OutcomeNoticeConfigKeys.DisplayIntent,
            context,
            ConfigDimensionValues.None,
            cancellationToken);

        return resolved.Value;
    }
}
