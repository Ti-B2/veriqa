// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Kinds of the channel entry the confirmation creation answer carries (SPEC-039 C20). The set is
/// closed: an entry either addresses one channel directly or hands the user the page where the
/// channel is still to be chosen.
/// </summary>
internal static class ChannelEntryKinds
{
    /// <summary>
    /// The entry is the deep link of a single channel — the user lands in that channel.
    /// </summary>
    public const string DeepLink = "deep_link";

    /// <summary>
    /// The entry is the URL of the core's own entry page, where the channel is chosen.
    /// </summary>
    public const string PageUrl = "page_url";
}
