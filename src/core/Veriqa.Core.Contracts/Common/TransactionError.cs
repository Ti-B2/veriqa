// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.TransactionEngine.Common;

/// <summary>
/// Structured error with a code, a description and the kind of failure it reports.
/// </summary>
/// <remarks>
/// The category is the answer to "is this us or the request", available without comparing the code
/// against a list of strings. It defaults to <see cref="TransactionErrorCategory.BusinessRule"/>,
/// so a producer that says nothing about the origin of an error reports a decision of the product —
/// never an outage it did not observe.
/// </remarks>
/// <param name="Code">Error code (a string constant).</param>
/// <param name="Message">Human-readable error description (a Natural Key: English base text — the base language after the TASK-057 flip).</param>
/// <param name="Category">Kind of failure the error reports.</param>
public sealed record TransactionError(
    string Code,
    string Message,
    TransactionErrorCategory Category = TransactionErrorCategory.BusinessRule);
