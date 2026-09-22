// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// Transaction status polling endpoint (GET /api/transaction/{id}/status).
/// Provides a fallback for clients without WebSocket support (SPEC-007 §5.4).
/// Returns a safe public subset of the transaction state:
/// state, channel_type, ttl_seconds, reason_code, awaiting_web_confirmation.
/// </summary>
public static class TransactionStatusEndpoint
{
    /// <summary>
    /// Full endpoint path.
    /// </summary>
    private const string RoutePath = OidcEndpoints.TransactionStatusBase + "/{id}/status";

    /// <summary>
    /// Registers the transaction status polling endpoint with rate limiting.
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <param name="rateLimitPolicyName">Rate limiting policy name (null — no limiting).</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapTransactionStatusEndpoint(
        this IEndpointRouteBuilder endpoints,
        string? rateLimitPolicyName = null)
    {
        var builder = endpoints.MapGet(RoutePath, HandleStatusAsync);

        if (!string.IsNullOrEmpty(rateLimitPolicyName))
        {
            builder.RequireRateLimiting(rateLimitPolicyName);
        }

        return endpoints;
    }

    /// <summary>
    /// Handles the transaction status request by its external identifier (session_id).
    /// </summary>
    private static async Task<IResult> HandleStatusAsync(
        string id,
        ITransactionService transactionService,
        TimeProvider timeProvider,
        HttpContext httpContext)
    {
        // The method parses the id as a session_id, fetches the transaction, and returns its public status.

        // 1. Map session_id → TransactionId
        var transactionId = SessionIdMapper.ToTransactionId(id);

        if (transactionId is null)
        {
            return Results.Problem(
                detail: "Invalid transaction identifier.",
                statusCode: StatusCodes.Status400BadRequest,
                title: OidcErrorCodes.OidcRequestInvalid);
        }

        // 2. Fetch the transaction from the store
        var txResult = await transactionService.GetTransactionAsync(
            transactionId.Value,
            httpContext.RequestAborted);

        if (txResult.IsFailure)
        {
            return Results.Problem(
                detail: "Transaction not found or expired.",
                statusCode: StatusCodes.Status404NotFound,
                title: OidcErrorCodes.TransactionNotFound);
        }

        var transaction = txResult.Value;

        // 3. Build the public response
        var response = BuildStatusResponse(transaction, timeProvider.GetUtcNow());

        return Results.Ok(response);
    }

    /// <summary>
    /// Builds the public representation of the transaction status.
    /// Does not expose sensitive data (claims, snapshot contents).
    /// </summary>
    /// <param name="transaction">Transaction.</param>
    /// <param name="now">Current time (UTC), supplied by the caller's time provider.</param>
    /// <returns>Anonymous object with public status fields.</returns>
    private static object BuildStatusResponse(Transaction transaction, DateTimeOffset now)
    {
        // The method builds a safe public status without exposing internal data.

        var ttlSeconds = transaction.IsTerminal()
            ? 0
            : Math.Max(0, (transaction.ExpiresAt - now).TotalSeconds);

        // The channel type appears as soon as the channel data reaches the transaction — that is at
        // the confirmation for the in-channel surfaces, and already before it when the core takes the
        // question onto its own page.
        var channelType = transaction.ChannelIdentitySnapshot?.ChannelType;

        return new
        {
            state = transaction.State.ToString(),
            channel_type = channelType,
            ttl_seconds = Math.Round(ttlSeconds, 1),
            reason_code = transaction.StateReasonCode,

            // The polling fallback needs the same signal SignalR delivers as a status: the question
            // is now on the core page, so the browser must go to the callback. It is a separate
            // field rather than a synthetic value of "state", which stays the name of a state-machine
            // state.
            awaiting_web_confirmation = transaction.IsAwaitingWebConfirmation()
        };
    }
}
