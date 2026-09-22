// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Veriqa.Core.TransactionEngine.Identity;

/// <summary>
/// Reversible protection of the identity values a relying party states as its expectations
/// (SPEC-039 C23). The engine compares them, but the means of protection comes from the host, so the
/// contract is declared here and implemented by the composition that owns a key ring.
/// </summary>
/// <remarks>
/// The implementation is a standard platform facility (ASP.NET Core Data Protection) and never
/// hand-written cryptography (<c>core-rules §3</c>). What the engine stores is only the result of
/// <see cref="Protect"/>: from the store's point of view the value is an opaque string, and it never
/// reaches a log above <c>Debug</c>, an event or any surface.
/// </remarks>
public interface IIdentityValueProtector
{
    /// <summary>
    /// Turns an already normalized identity value into the protected representation that is stored.
    /// </summary>
    /// <param name="value">Normalized value; lives in memory only and is never logged.</param>
    /// <returns>Protected representation to write into the comparison container.</returns>
    string Protect(string value);

    /// <summary>
    /// Restores a stored value at the moment of the comparison.
    /// </summary>
    /// <remarks>
    /// A refusal is expected and is not an error of the operation: a key ring rotated without access
    /// to the previous keys leaves a value that can no longer be read, and the answer to that is
    /// "nothing matched" rather than a failed confirmation (SPEC-039 E43). The question carries no
    /// message of its own, which is why it is a <c>Try</c> and not a <c>Result</c>: there is nothing
    /// to tell the caller beyond yes or no, and the reason belongs in the implementation's own log.
    /// </remarks>
    /// <param name="protectedValue">Value as it was stored.</param>
    /// <param name="value">The restored value.</param>
    /// <returns><see langword="true"/> when the value was restored.</returns>
    bool TryUnprotect(string protectedValue, [NotNullWhen(true)] out string? value);
}
