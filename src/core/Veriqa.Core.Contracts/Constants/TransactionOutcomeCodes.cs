// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Well-known terminal outcomes of a transaction as shown to the user (SPEC-003 §6.2).
/// <c>Confirmed</c>/<c>Declined</c> mean the user's will carried through to a terminal state;
/// any branch where the core shows the user an error is <c>Failed</c>.
/// </summary>
public static class TransactionOutcomeCodes
{
    /// <summary>
    /// The user confirmed and the transaction completed.
    /// </summary>
    public const string Confirmed = "confirmed";

    /// <summary>
    /// The user declined and the decline was recorded.
    /// </summary>
    public const string Declined = "declined";

    /// <summary>
    /// The transaction expired by TTL.
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// The transaction ended in an error shown to the user.
    /// </summary>
    public const string Failed = "failed";
}
