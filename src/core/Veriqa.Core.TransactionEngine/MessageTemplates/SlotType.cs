// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Type of a typed placeholder slot (SPEC-036 §4.1/§4.2, TPL-011). The type is the contract for
/// BOTH input validation and rendering — it is not decoration. The v1 set is fixed; extending it is
/// a spec revision.
/// </summary>
public enum SlotType
{
    /// <summary>
    /// Open free-form text with a mandatory hard length limit, NFC normalization and a control-character
    /// ban (TPL-012). Rendered as text.
    /// </summary>
    String = 0,

    /// <summary>
    /// A value strictly from the declared discrete set (TPL-013). A mismatch is a security event.
    /// </summary>
    Enum = 1,

    /// <summary>
    /// A numeric value (integer or decimal) within the declared bounds (TPL-014). Rendered per the
    /// recipient's locale.
    /// </summary>
    Number = 2,

    /// <summary>
    /// A monetary amount plus an ISO 4217 currency (TPL-015). <b>Forward-declared in v1</b>: the type is
    /// reserved in the contract, but has no identified consumer, so rendering is not implemented and a
    /// <c>money</c> slot in configuration v1 is rejected (SPEC-036 §5, TASK-079 D3).
    /// </summary>
    Money = 3,

    /// <summary>
    /// A moment in time transported as ISO 8601 with an offset; the internal model is
    /// <see cref="System.DateTimeOffset"/> (TPL-016). Rendered per the recipient's locale.
    /// </summary>
    Datetime = 4,

    /// <summary>
    /// The name/identifier of a domain object (tool, server, document): same constraints as
    /// <see cref="String"/>, plus the render invariant "strictly text" — never a link, markup or deep
    /// link, regardless of whether the value looks like a URL (TPL-017).
    /// </summary>
    EntityRef = 5
}
