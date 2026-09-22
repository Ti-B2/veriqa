// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.AspNetCore.SignalR;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.AuthServer.Hubs;

/// <summary>
/// SignalR hub for real-time notifications about authentication transaction status (SPEC-007 §5.2).
/// The client connects and subscribes to a group keyed by the external session_id.
/// Access model (SPEC-007 UI-095): the session_id is a bearer capability. It travels in the QR code,
/// the deep link and the page URL, so whoever holds the sign-in entry may subscribe — the hub does not
/// authenticate the connection and rejects only an unparsable identifier or a transaction that does not
/// exist. The boundary is held by what the hub sends, not by who listens: its messages carry the
/// transaction's process state only, never the channel user's identity.
/// </summary>
public sealed class AuthTransactionHub : Hub
{
    /// <summary>
    /// Transaction store used to validate the subscription.
    /// </summary>
    private readonly ITransactionStore _transactionStore;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<AuthTransactionHub> _logger;

    /// <summary>
    /// Clock the transaction deadline is read by.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes the authentication hub.
    /// </summary>
    /// <param name="transactionStore">Transaction store.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">
    /// Clock the transaction deadline is read by — the same one the engine measures the TTL with, so
    /// that the replayed status and the transaction's own expiry never disagree. Supplied by the
    /// container, which every wiring of this hub has: the store it takes above comes from the
    /// Transaction Engine, and the engine registers the clock alongside it.
    /// </param>
    public AuthTransactionHub(
        ITransactionStore transactionStore,
        ILogger<AuthTransactionHub> logger,
        TimeProvider timeProvider)
    {
        _transactionStore = transactionStore;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Subscribes the client to updates for a specific transaction (SPEC-007 UI-028).
    /// Validates the session_id format, verifies that the transaction exists in the store, and
    /// replays the transaction's current status to the caller so that a subscriber is never left
    /// waiting for a broadcast that has already gone out (SPEC-007 UI-087).
    /// </summary>
    /// <param name="sessionId">External session identifier (session_id).</param>
    /// <returns>Group subscription task.</returns>
    public async Task JoinTransactionAsync(string sessionId)
    {
        // The method validates the session_id, adds the connection to the group and replays the
        // transaction's current status to it

        // Validate the session_id → TransactionId format
        var parsedId = SessionIdMapper.ToTransactionId(sessionId);
        if (parsedId is null)
        {
            _logger.LogWarning(
                "Client {ConnectionId} attempted to subscribe to an invalid sessionId: {SessionId}",
                Context.ConnectionId,
                sessionId);
            throw new HubException("Invalid session identifier");
        }

        // The only admission check is existence: the session_id itself is the capability (SPEC-007 UI-095)
        var transaction = await _transactionStore.GetByIdAsync(parsedId.Value, Context.ConnectionAborted);
        if (transaction is null)
        {
            _logger.LogWarning(
                "Client {ConnectionId} attempted to subscribe to a non-existent session {SessionId}",
                Context.ConnectionId,
                sessionId);
            throw new HubException("Session not found");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId, Context.ConnectionAborted);

        // A broadcast is addressed to the group AS IT STOOD at the moment it was sent, and it is
        // never repeated. So a status that changed before this connection was in the group reached
        // nobody — the page opened after the user had already answered, or reconnected and
        // resubscribed after the socket dropped — and waiting for it would last until the TTL ran
        // out, which is the one ending SPEC-007 UI-087 forbids. The current status is therefore
        // replayed to the caller alone, over the same method and the same model a broadcast uses:
        // the client needs no second path, and a status it already handled is idempotent on it.
        //
        // Read again, rather than reusing the snapshot taken for the admission check above: only a
        // snapshot taken AFTER the connection joined the group leaves no gap. A change between the
        // two reads is carried by the broadcast the group now receives; a change before the second
        // read is in the snapshot itself. The two can therefore overlap — and an overlap costs one
        // repeated message, while the gap between them would cost the status altogether. The second
        // read is a read per subscription, not per status change. A transaction no longer in the
        // store by that second read — swept between the two — leaves the admission snapshot as the
        // last thing known about it, and the deadline on that snapshot is what the caller hears.
        var current = await _transactionStore.GetByIdAsync(parsedId.Value, Context.ConnectionAborted)
            ?? transaction;

        var currentStatus = TransactionStatusMessageFactory.FromTransaction(
            current,
            _timeProvider.GetUtcNow());
        if (currentStatus is not null)
        {
            await Clients.Caller.SendAsync(
                SignalRConstants.OnStatusChangedMethod,
                currentStatus,
                Context.ConnectionAborted);
        }

        _logger.LogInformation(
            "Client {ConnectionId} subscribed to session {SessionId}, replayed status: {Status}",
            Context.ConnectionId,
            sessionId,
            currentStatus?.Status);
    }

    /// <summary>
    /// Unsubscribes the client from transaction updates (SPEC-007 UI-029).
    /// </summary>
    /// <param name="sessionId">External session identifier (session_id).</param>
    /// <returns>Group unsubscription task.</returns>
    public async Task LeaveTransactionAsync(string sessionId)
    {
        // The method removes the connection from the group keyed by session_id
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId, Context.ConnectionAborted);
        _logger.LogInformation(
            "Client {ConnectionId} unsubscribed from session {SessionId}",
            Context.ConnectionId,
            sessionId);
    }
}
