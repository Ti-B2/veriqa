// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.Hubs;

/// <summary>
/// Builds the status messages of the auth hub (SPEC-007 §5.3) — one home for two rules that were
/// previously written wherever they were needed: what a stored transaction tells the page about
/// itself, and where the browser is sent once the transaction has an outcome.
/// </summary>
/// <remarks>
/// The transaction is read the way the polling surface reads it: the deadline first — the domain
/// pair "is the TTL still what ends this one" plus "has it passed", the same pair
/// <c>ITransactionService.GetTransactionAsync</c> asks — and then <c>State</c>,
/// <c>StateReasonCode</c> and <c>IsAwaitingWebConfirmation()</c>, named with the same constants the
/// broadcast uses. Both transports of SPEC-007 UI-087 therefore end the page on the same fact about
/// the same transaction. What they do NOT share is how the answer travels: an expired transaction is
/// a 404 on the polling surface (SPEC-007 UI-094) and the <c>Expired</c> status of the message model
/// (§5.3) here — one page ending, reached over two wires.
/// </remarks>
internal static class TransactionStatusMessageFactory
{
    /// <summary>
    /// Projects the current state of a stored transaction onto a status message.
    /// </summary>
    /// <remarks>
    /// A transaction that has nothing to tell yet — created, or pending with no channel data —
    /// returns null: waiting is what the page is already showing, and a message saying so would
    /// only repeat it.
    /// </remarks>
    /// <param name="transaction">Transaction read from the store.</param>
    /// <param name="now">Current point in time, by the clock the TTL is measured with.</param>
    /// <returns>Status message, or null when the current state carries no client-visible status.</returns>
    public static TransactionStatusMessage? FromTransaction(Transaction transaction, DateTimeOffset now)
    {
        // The method maps the transaction's own state onto the message the page already knows how
        // to handle.

        var sessionId = SessionIdMapper.ToSessionId(transaction.Id);

        // Asked before anything the stored state says, because the store may not have caught up with
        // it yet: a transaction whose fate the deadline still decides is over the moment the deadline
        // passes, and the sweep that writes that down runs later. Read from the state alone, such a
        // transaction is a live one — and a Pending one already carrying channel data would send the
        // browser to the callback, which knows the TTL and answers with the generic "session expired"
        // page instead of the ending this page shows inline (SPEC-007 UI-094). Confirmed is untouched
        // by this question by construction: its deadline decides nothing (SPEC-001 §4.4 item 4), and
        // IsSubjectToExpiry() is what says so.
        if (transaction.IsSubjectToExpiry() && transaction.IsExpired(now))
        {
            return new TransactionStatusMessage
            {
                SessionId = sessionId,
                Status = TransactionStatusNames.Expired
            };
        }

        // Asked before the state itself: "the core is waiting for the answer on its own page" is
        // not a state-machine state — the transaction stays Pending — so the state alone would
        // report this one as ordinary waiting (SPEC-007 UI-087).
        if (transaction.IsAwaitingWebConfirmation())
        {
            return new TransactionStatusMessage
            {
                SessionId = sessionId,
                Status = TransactionStatusNames.AwaitingWebConfirmation,
                RedirectUrl = BuildCallbackUrl(sessionId)
            };
        }

        return transaction.State switch
        {
            TransactionState.Confirmed => new TransactionStatusMessage
            {
                SessionId = sessionId,
                Status = TransactionStatusNames.Confirmed
            },

            TransactionState.Completed => new TransactionStatusMessage
            {
                SessionId = sessionId,
                Status = TransactionStatusNames.Completed,
                RedirectUrl = BuildCallbackUrl(sessionId)
            },

            TransactionState.Expired => new TransactionStatusMessage
            {
                SessionId = sessionId,
                Status = TransactionStatusNames.Expired
            },

            TransactionState.Failed => new TransactionStatusMessage
            {
                SessionId = sessionId,
                Status = TransactionStatusNames.Failed,
                ErrorCode = transaction.StateReasonCode
            },

            // Created, and Pending with no channel data: nothing has happened for the page to hear.
            _ => null
        };
    }

    /// <summary>
    /// Builds the callback URL for redirection after successful authentication.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Callback URL.</returns>
    public static string BuildCallbackUrl(string sessionId)
    {
        // The method builds the URL for the browser redirect
        return $"{OidcEndpoints.AuthorizeCallback}?{OidcConstants.SessionIdParameterName}={Uri.EscapeDataString(sessionId)}";
    }
}
