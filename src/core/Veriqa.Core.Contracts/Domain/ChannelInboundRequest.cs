// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// Channel-neutral envelope of an inbound platform request (SPEC-003 §6.2, CA-030).
/// Carries everything an adapter needs to validate a signature and to parse its own wire format,
/// and nothing that would drag a web framework into the MIT contracts package.
/// </summary>
/// <remarks>
/// A class, not a record: <see cref="ReadOnlyMemory{T}"/> makes record value equality misleading,
/// and equality of an envelope is of no use to anyone.
/// </remarks>
public sealed class ChannelInboundRequest
{
    /// <summary>
    /// HTTP method in upper case ("GET"/"POST") — tells a verification handshake from an event.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Raw request body byte for byte: a signature is computed over exactly these bytes.
    /// Decoding into a string is the adapter's business.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// Request headers. Names compare case-insensitively (<c>OrdinalIgnoreCase</c>); a multi-valued
    /// header is joined into one value with ", " — an adapter that needs strict single-valuedness
    /// must take that into account.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// Query string parameters. Names compare case-insensitively (<c>OrdinalIgnoreCase</c>);
    /// repeated parameters are joined the same way as headers.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Query { get; init; }
}
