// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Limits that belong to the channel-adapter contract as a whole, not to an individual channel.
/// A single adapter never restates or narrows a value declared here: a per-channel copy is free to
/// drift from the contract, and the drift is invisible until a snapshot is rejected in production.
/// Channel constant classes alias these members instead of carrying their own literal.
/// </summary>
public static class ChannelAdapterLimits
{
    /// <summary>
    /// Maximum raw metadata size in bytes, identical for every channel (SPEC-003 CA-005). Part of the
    /// overall 128 KB snapshot budget (SPEC-001 §3.3), which is why one channel cannot raise it alone.
    /// </summary>
    public const int MaxRawMetadataSize = 4096;
}
