// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Reasons the confirmation page of the core refused a request for a transaction it FOUND
/// (SPEC-039 E48). The codes travel on the channel audit event and land in the integrator's journal
/// as the reason of the refusal; the answer shown to the visitor is one and the same for all of them
/// (SPEC-039 C15), so the journal is the only place the reasons are told apart.
/// </summary>
/// <remarks>
/// A refusal on an identifier that names no transaction carries no code: the journal entry is keyed
/// by the transaction, and there is none.
/// </remarks>
public static class ConfirmationPageAuditCodes
{
    /// <summary>
    /// The transaction was found, is a confirmation this page was addressed for, and its lifetime has
    /// run out — the question was opened, or an answer was submitted, after the TTL. Both sides of the
    /// cleanup pass carry this code: the transaction still Pending with its expiry behind it, and the
    /// one the cleanup has already moved into the Expired state.
    /// </summary>
    public const string Expired = "confirmation_page_expired";

    /// <summary>
    /// The transaction was found, but may not be answered on this page: it is not a confirmation, it
    /// carries no channel identity to sign an answer with, or it is over for a reason other than its
    /// time — a decision on it is already recorded.
    /// </summary>
    public const string NotAnswerable = "confirmation_page_not_answerable";
}
