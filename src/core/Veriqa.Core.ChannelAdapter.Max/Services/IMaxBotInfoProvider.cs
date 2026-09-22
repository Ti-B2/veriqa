// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Max.Services;

/// <summary>
/// MAX bot info provider.
/// Caches the bot username after the first request.
/// </summary>
internal interface IMaxBotInfoProvider
{
    /// <summary>
    /// Returns the MAX bot username.
    /// On the first call requests the info via the MAX Bot API and caches the result.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bot username.</returns>
    Task<string> GetBotUsernameAsync(CancellationToken cancellationToken = default);
}
