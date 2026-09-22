// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Telegram.Services;

/// <summary>
/// Provider of Telegram bot information.
/// Supplies the cached bot username.
/// </summary>
internal interface IBotInfoProvider
{
    /// <summary>
    /// Returns the username of the Telegram bot.
    /// The result is cached after the first call.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bot username without the @ symbol.</returns>
    Task<string> GetBotUsernameAsync(CancellationToken cancellationToken = default);
}
