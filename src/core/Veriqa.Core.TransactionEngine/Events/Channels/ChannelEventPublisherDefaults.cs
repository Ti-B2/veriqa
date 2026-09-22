// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Events.Channels;

/// <summary>
/// Default constants for the event publisher based on System.Threading.Channels.
/// </summary>
internal static class ChannelEventPublisherDefaults
{
    /// <summary>
    /// Default event queue capacity.
    /// </summary>
    internal const int DefaultCapacity = 1000;
}
