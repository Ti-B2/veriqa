// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// A rendered message in its parts (SPEC-036 §4.3). Channels whose message has one part read the body
/// alone (<see cref="MessageTextRenderer.Render"/>); a mail reads both, because the variant that states
/// its body states its subject beside it.
/// </summary>
/// <param name="Subject">Subject of the message, or null when the chosen variant states none.</param>
/// <param name="Body">Body of the message — the part every channel has.</param>
/// <param name="ReferencedSlots">Slots the CHOSEN variant references. A channel whose transport has to
/// carry something a text points at — the attachment a mail body references by Content-Id — reads it
/// off here: which of the forms a ladder offers was actually rendered is known to the render alone, and
/// asking the text of the rendered string instead would mean parsing the markup back.</param>
public sealed record RenderedMessageText(
    string? Subject,
    string Body,
    IReadOnlySet<string> ReferencedSlots);
