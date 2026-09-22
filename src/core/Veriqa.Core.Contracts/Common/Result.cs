// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Veriqa.Core.TransactionEngine.Common;

/// <summary>
/// Result of a business-logic operation that has no return value: either success or an error.
/// Mirrors <see cref="Result{T}"/> — same factories, same error model.
/// </summary>
/// <remarks>
/// An operation that merely reports success answers with this type rather than with
/// <c>Result&lt;bool&gt;</c>: the latter offers three states where only two are meaningful, and the
/// boolean nobody reads invites callers to invent a meaning for it. Conversions to and from
/// <see cref="Result{T}"/> are deliberately absent — a value either exists or it does not, and a
/// silent bridge between the two would erase that distinction.
/// </remarks>
public sealed class Result
{
    /// <summary>
    /// Message of the exception thrown when the error of a successful result is read.
    /// Deliberately free of any operation data: a result travels across trust boundaries.
    /// </summary>
    private const string ErrorUnavailableMessage = "Error is not available on a successful result.";

    /// <summary>
    /// Error of a failed result; null for a successful one.
    /// </summary>
    private readonly TransactionError? _error;

    /// <summary>
    /// Creates a result.
    /// </summary>
    private Result(TransactionError? error, bool isSuccess)
    {
        _error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>
    /// Whether the result is successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Whether the result is a failure.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Error of a failed result. Reading it on a successful result is a contract violation
    /// by the caller — check <see cref="IsFailure"/> first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public TransactionError Error =>
        _error ?? throw new InvalidOperationException(ErrorUnavailableMessage);

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>Successful result.</returns>
    public static Result Success() => new(null, true);

    /// <summary>
    /// Creates a failed result with an error.
    /// </summary>
    /// <param name="error">Error.</param>
    /// <returns>Failed result.</returns>
    public static Result Failure(TransactionError error) => new(error, false);

    /// <summary>
    /// Creates a failed result with a code, a message and the kind of failure reported.
    /// </summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error description.</param>
    /// <param name="category">Kind of failure the error reports; a product decision by default.</param>
    /// <returns>Failed result.</returns>
    public static Result Failure(
        string code,
        string message,
        TransactionErrorCategory category = TransactionErrorCategory.BusinessRule) =>
        new(new TransactionError(code, message, category), false);
}

/// <summary>
/// Result of a business-logic operation. Contains either a success value or an error.
/// Exceptions are not used for business errors — only the Result pattern.
/// </summary>
/// <typeparam name="T">Value type on success.</typeparam>
/// <remarks>
/// A <c>Match</c> overload folding both branches into one expression is deliberately absent, and its
/// absence is a decision rather than an omission: <see cref="TryGetValue"/> already spares the caller
/// the flag-check-then-property-read pair, and because this type is sealed with a public success
/// state, <c>Match</c> can be supplied as an extension method by anyone — including a consumer, in
/// their own assembly — without a change here. Adding it later is therefore additive and costs no
/// major version, so it waits for a caller that needs it.
/// </remarks>
public sealed class Result<T>
{
    /// <summary>
    /// Message of the exception thrown when the value of a failed result is read.
    /// Deliberately free of any operation data: a result travels across trust boundaries and the
    /// value it carries may be sensitive.
    /// </summary>
    private const string ValueUnavailableMessage = "Value is not available on a failed result.";

    /// <summary>
    /// Message of the exception thrown when the error of a successful result is read.
    /// </summary>
    private const string ErrorUnavailableMessage = "Error is not available on a successful result.";

    /// <summary>
    /// Value of a successful result; default for a failed one.
    /// </summary>
    private readonly T? _value;

    /// <summary>
    /// Error of a failed result; null for a successful one.
    /// </summary>
    private readonly TransactionError? _error;

    /// <summary>
    /// Creates a result.
    /// </summary>
    private Result(T? value, TransactionError? error, bool isSuccess)
    {
        _value = value;
        _error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>
    /// Whether the result is successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Whether the result is a failure.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Value of a successful result; never null unless <typeparamref name="T"/> is itself a
    /// nullable type — an operation declaring <c>Result&lt;T?&gt;</c> may return null as a
    /// successful outcome. Reading it on a failed result is a contract violation by the
    /// caller — check <see cref="IsSuccess"/> or call <see cref="TryGetValue"/> first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value =>
        IsSuccess ? _value! : throw new InvalidOperationException(ValueUnavailableMessage);

    /// <summary>
    /// Error of a failed result. Reading it on a successful result is a contract violation
    /// by the caller — check <see cref="IsFailure"/> first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public TransactionError Error =>
        _error ?? throw new InvalidOperationException(ErrorUnavailableMessage);

    /// <summary>
    /// Returns the value when the result is a success — one call instead of a flag check
    /// followed by a property read.
    /// </summary>
    /// <param name="value">Value of a successful result; the default of <typeparamref name="T"/> otherwise.</param>
    /// <returns>true if the result is a success.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">Result value.</param>
    /// <returns>Successful result.</returns>
    public static Result<T> Success(T value) => new(value, null, true);

    /// <summary>
    /// Creates a failed result with an error.
    /// </summary>
    /// <param name="error">Error.</param>
    /// <returns>Failed result.</returns>
    public static Result<T> Failure(TransactionError error) => new(default, error, false);

    /// <summary>
    /// Creates a failed result with a code, a message and the kind of failure reported.
    /// </summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error description.</param>
    /// <param name="category">Kind of failure the error reports; a product decision by default.</param>
    /// <returns>Failed result.</returns>
    public static Result<T> Failure(
        string code,
        string message,
        TransactionErrorCategory category = TransactionErrorCategory.BusinessRule) =>
        new(default, new TransactionError(code, message, category), false);
}
