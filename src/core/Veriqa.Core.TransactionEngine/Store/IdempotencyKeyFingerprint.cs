// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Utility for safe logging of the idempotency key.
/// Returns a short stable fingerprint (SHA-256 → first 8 bytes → hex),
/// so diagnostics is possible without exposing caller-controlled data,
/// which may contain a correlation ID or other sensitive information.
/// </summary>
/// <remarks>
/// Called by the stores whenever a transaction carries the idempotency pair. In this release no
/// shipped entry point fills that pair in — the browser authorization endpoint does not, because a
/// repeated authorization request is a new sign-in rather than a retry — so the fingerprint appears
/// in logs only for a host that drives the engine directly. The entry point where a repeat IS a
/// repeat arrives with the s2s creation of a confirmation transaction (SPEC-039).
/// </remarks>
internal static class IdempotencyKeyFingerprint
{
    /// <summary>
    /// Produces a stable fingerprint for (scope, key) suitable for logs.
    /// Format: <c>sha256:xxxxxxxxxxxxxxxx</c> (16 hex characters = 8 prefix bytes).
    /// </summary>
    /// <param name="scope">Idempotency scope.</param>
    /// <param name="key">Idempotency key.</param>
    /// <returns>Fingerprint string.</returns>
    public static string Compute(string? scope, string? key)
    {
        if (scope is null && key is null)
        {
            return "sha256:empty";
        }

        // Length-prefixed serialization — eliminates collisions between ("a", "bc") and ("ab", "c").
        var scopeBytes = Encoding.UTF8.GetBytes(scope ?? string.Empty);
        var keyBytes = Encoding.UTF8.GetBytes(key ?? string.Empty);
        var buffer = new byte[8 + scopeBytes.Length + keyBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), scopeBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), keyBytes.Length);
        scopeBytes.CopyTo(buffer, 8);
        keyBytes.CopyTo(buffer, 8 + scopeBytes.Length);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer, hash);
        return "sha256:" + Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
