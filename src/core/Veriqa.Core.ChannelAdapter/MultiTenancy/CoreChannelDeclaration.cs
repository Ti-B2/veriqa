// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Core-level facts of ONE channel, declared by the channel's own registration. The channel contour
/// reads them from here rather than from the channel's <c>Options</c> by name: the set of channels the
/// core level makes available (<c>channels_enabled</c>, SPEC-003 §17.7) and whether the channel runs
/// in polling mode (SPEC-003 §17.4, CA-171/CA-172).
/// </summary>
/// <remarks>
/// Both facts live in one declaration because they come from one source — the channel's global
/// options at the core level — and are stated at one moment: the channel's <c>Add*</c> call, before
/// it decides whether it is enabled at all. Both are read through delegates rather than captured as
/// values: the underlying options are watched (<c>IOptionsMonitor</c>), and a value captured at
/// registration would freeze a deployment-side edit out of the answer.
/// </remarks>
public sealed class CoreChannelDeclaration
{
    /// <summary>
    /// Reads whether the channel is enabled at the core level.
    /// </summary>
    private readonly Func<bool> _isEnabled;

    /// <summary>
    /// Reads whether the channel runs in polling mode at the core level.
    /// </summary>
    private readonly Func<bool> _usesPolling;

    /// <summary>
    /// Declares the core-level facts of a channel.
    /// </summary>
    /// <param name="channelType">Channel type the declaration speaks for.</param>
    /// <param name="isEnabled">Reads the channel's core-level <c>Enabled</c> flag.</param>
    /// <param name="usesPolling">
    /// Reads whether the channel pulls updates itself (polling) instead of receiving a webhook;
    /// <c>null</c> — the channel has no polling transport at all.
    /// </param>
    public CoreChannelDeclaration(string channelType, Func<bool> isEnabled, Func<bool>? usesPolling = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelType);
        ArgumentNullException.ThrowIfNull(isEnabled);

        ChannelType = channelType;
        _isEnabled = isEnabled;
        _usesPolling = usesPolling ?? (static () => false);
    }

    /// <summary>
    /// Channel type the declaration speaks for.
    /// </summary>
    public string ChannelType { get; }

    /// <summary>
    /// Whether the channel is enabled at the core level right now.
    /// </summary>
    public bool IsEnabled => _isEnabled();

    /// <summary>
    /// Whether the channel runs in polling mode at the core level right now.
    /// </summary>
    public bool UsesPolling => _usesPolling();
}
