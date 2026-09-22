// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.TransactionEngine.Common;

/// <summary>
/// What kind of failure an error reports: a decision of the product, or a dependency that did not
/// answer.
/// </summary>
/// <remarks>
/// The distinction exists because it changes what a caller does — retry the same request, or fix
/// it — and until now it could only be read by comparing the error code against a list of strings,
/// which every caller had to keep in sync by hand. The set is deliberately minimal: everything
/// finer than "who is at fault" is already said by <see cref="TransactionError.Code"/>, and a
/// category per code would be a second dictionary of the same thing.
/// </remarks>
public enum TransactionErrorCategory
{
    /// <summary>
    /// The operation was refused by a rule of the product: an invalid request, a state that
    /// forbids the transition, a limit, a conflict of two callers. Repeating the same request
    /// unchanged yields the same answer. The default: an error that says nothing about its origin
    /// is a decision of the product, never an outage.
    /// </summary>
    BusinessRule = 0,

    /// <summary>
    /// A dependency of the operation failed — the transaction store above all. The request itself
    /// may be perfectly valid, and repeating it later may well succeed.
    /// </summary>
    Infrastructure = 1
}
