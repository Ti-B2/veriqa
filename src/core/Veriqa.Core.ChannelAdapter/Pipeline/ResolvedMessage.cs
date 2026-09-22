// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.MessageTemplates;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// A message ready to render: the two settings it is made of, resolved for the current ownership
/// context and brought together (SPEC-036 §4.6, TPL-116).
/// <para>
/// The pair is assembled at the point of resolution and not inside the renderer, because rendering is
/// synchronous and deterministic while resolution is neither. It is a pair rather than one value on
/// purpose: the contract and the ladder are resolved apart and may well come from different levels —
/// a deployment that rewrites the wording of a message states no slots at all, and the contract it
/// gets is the one the product ships.
/// </para>
/// </summary>
/// <param name="Kind">Message kind identifier — what a diagnostic names the message by.</param>
/// <param name="Contract">Slots the message declares: the allowlist of the substitution.</param>
/// <param name="Templates">Variants of the text, fullest → minimal, as the answering step stated them.
/// A variant is a text or a structure the renderer of a channel understands (SPEC-036 §4.3).</param>
/// <param name="ContractLevel">Level that stated the CONTRACT — the second fact about the same
/// answer, next to the contract itself (SPEC-036 TPL-112/TPL-113). A consumer deciding whether a
/// message kind is one a deployment DECLARED, rather than one the product ships for its own purposes,
/// asks it here instead of resolving the same key a second time: the right to name a kind is read off
/// the contract, because the template ladder also comes from the <c>ui_config</c> level, which a
/// request parameter chooses and a contract never reaches (TPL-108). Null — the level is unknown to
/// the point that built this value (nothing outside the resolution can produce one).</param>
public sealed record ResolvedMessage(
    string Kind,
    MessageContract Contract,
    IReadOnlyList<MessageTemplateVariant> Templates,
    ConfigLevel? ContractLevel = null);
