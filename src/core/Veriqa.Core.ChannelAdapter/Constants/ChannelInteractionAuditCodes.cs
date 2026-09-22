// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Reasons the channel side of the interaction surface refused to serve an event for a transaction
/// it FOUND (SPEC-039 E48). The codes travel on the channel audit event and land in the integrator's
/// journal as the reason of the refusal; what the sender is answered with does not depend on them
/// (SPEC-039 C15), so the journal is the only place the reasons are told apart.
/// </summary>
/// <remarks>
/// The twin of the page-side dictionary of the same surface: one class of occurrence, two entry
/// points, and a code that names the entry point it came from — a refusal in a channel is not a
/// refusal of the confirmation page and must not borrow its name.
/// An event on an identifier that names no transaction carries no code at all: the journal entry is
/// keyed by the transaction, and there is none — otherwise a stranger with random identifiers would
/// be writing the journal.
/// </remarks>
public static class ChannelInteractionAuditCodes
{
    /// <summary>
    /// The transaction was found and its sign-in window has closed — in either of the two forms the
    /// same fact takes on the two sides of the cleanup pass: the state the pass has already written
    /// down, and the deadline behind a transaction still awaiting an answer (read by the pipeline
    /// itself, or the one the engine refused a write against). Written for a sign-in link followed
    /// and for a confirm or decline button pressed alike.
    /// </summary>
    public const string LinkExpired = "channel_link_expired";

    /// <summary>
    /// The transaction was found, but it no longer accepts the event: a decision on it is already
    /// recorded, or another writer took it at the same moment. A press repeating the very decision
    /// already recorded is not such a refusal and is not written down.
    /// </summary>
    public const string LinkNotAnswerable = "channel_link_not_answerable";

    /// <summary>
    /// The transaction was found and refused the event on the sender's account: the channel is
    /// outside its allow-list, or the sender is a bot. A refusal of ours — the store, the identity
    /// resolver — is written down under this code too, since neither is disclosed to the sender; the
    /// exact code of the engine travels in the event's details and tells the two apart in the journal.
    /// </summary>
    public const string SenderNotAdmitted = "channel_sender_not_admitted";
}
