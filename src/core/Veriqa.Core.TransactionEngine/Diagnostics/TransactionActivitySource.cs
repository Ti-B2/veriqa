// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;

using Veriqa.Core.TransactionEngine.Constants;

namespace Veriqa.Core.TransactionEngine.Diagnostics;

/// <summary>
/// The single activity source of the transaction engine. The component owns it and subscribes to
/// nothing: a host adds it to its OpenTelemetry provider by the very same constant.
/// </summary>
/// <remarks>
/// Static, unlike the meter next to it, and for a reason that is not stylistic: an
/// <see cref="ActivitySource"/> is not a factory-created instrument. Its subscription is global —
/// an <see cref="ActivityListener"/> matches sources by name, not by object — so a test observes
/// spans of this very source without substituting anything, and a second instance would only split
/// one source name across two objects.
/// Status discipline, one rule for every span started here: the span is marked
/// <see cref="ActivityStatusCode.Error"/> for a failure that ENDS inside the method owning it — a
/// refused <c>Result</c>, and an exception that method catches. An exception left to escape is
/// deliberately not marked: it ends the operation above, where the host's request instrumentation
/// marks the enclosing span, and a second mark would add no fact. An Unset span of this source
/// therefore reads as "no failure ended here", never as "the operation succeeded".
/// </remarks>
internal static class TransactionActivitySource
{
    /// <summary>
    /// Activity source the engine's spans are started from.
    /// </summary>
    internal static readonly ActivitySource Source = new(TransactionTelemetry.ActivitySourceName);
}
