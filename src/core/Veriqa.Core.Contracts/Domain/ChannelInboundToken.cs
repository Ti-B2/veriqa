// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// Opaque token of an inbound channel interaction (SPEC-003 §6.3).
/// The core carries it back to the adapter in <c>ReportOutcomeAsync</c> untouched: it never parses,
/// interprets or logs the payload — what is inside is the adapter's decision (coordinates, keys,
/// anything that saves a platform call).
/// </summary>
/// <param name="Value">Adapter-defined payload.</param>
public readonly record struct ChannelInboundToken(string Value)
{
    /// <summary>
    /// Adapter-defined payload. Never empty for a value built through the constructor, so
    /// "there is a token" and "there is no token" stay distinguishable.
    /// </summary>
    public string Value { get; } = Validate(Value);

    /// <summary>
    /// Rejects an empty payload so that a constructed token can never collide with <c>default</c>.
    /// </summary>
    /// <param name="value">Adapter-defined payload.</param>
    /// <returns>The validated payload.</returns>
    private static string Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
