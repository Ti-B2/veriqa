// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Events.Channels;

/// <summary>
/// Settings of the event publisher based on System.Threading.Channels.
/// </summary>
public sealed class ChannelEventPublisherOptions
{
    /// <summary>
    /// Event queue capacity (bounded channel capacity).
    /// </summary>
    public int Capacity { get; set; } = ChannelEventPublisherDefaults.DefaultCapacity;
}
