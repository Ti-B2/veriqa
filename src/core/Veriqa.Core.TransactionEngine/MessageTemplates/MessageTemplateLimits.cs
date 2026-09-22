// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Numeric limits shared by the typed placeholder schema (SPEC-036 §7.2, TPL-012).
/// The single source of the substituted-value length ceiling for the whole core: the confirmation
/// prompt renderer's field cap references this constant so there is never a second "128" (TPL-110).
/// </summary>
public static class MessageTemplateLimits
{
    /// <summary>
    /// Maximum length of a single substituted slot value (protection against channel-message bloat).
    /// A per-slot <c>MaxLength</c> may be smaller, but never larger (TPL-012).
    /// </summary>
    public const int MaxSlotValueLength = 128;

    /// <summary>
    /// Maximum aggregate byte size of a caller-values set, within the snapshot limit
    /// (SPEC-001 §3.3, 128 KB; TPL-025). Exceeding it fails the transaction with
    /// <c>slot_values_too_large</c> (D4).
    /// </summary>
    public const int MaxCallerValuesBytes = 16 * 1024;
}
