// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using Veriqa.Core.ChannelAdapter.Constants;

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// Reason why a confirmation prompt carries no initiator details (SPEC-017 §7.1).
/// An extensible value rather than a closed enum: a new reason does not break already compiled adapters.
/// </summary>
/// <param name="Value">Reason code (see <see cref="ContextSuppressionReasons"/>).</param>
public readonly record struct ContextSuppressionReason(string Value)
{
    /// <summary>
    /// Reason code. Never empty for a value built through the constructor;
    /// <c>default(ContextSuppressionReason)</c> carries <see langword="null"/> and denotes an unknown reason.
    /// </summary>
    public string Value { get; } = Validate(Value);

    /// <summary>
    /// The initiator context snapshot was never collected for the transaction.
    /// </summary>
    /// <remarks>
    /// Expression-bodied on purpose: a static auto-property without an initializer would be
    /// <c>default</c>, and every well-known reason would compare equal to every other one.
    /// </remarks>
    public static ContextSuppressionReason InitiatorSnapshotUnavailable
        => new(ContextSuppressionReasons.InitiatorSnapshotUnavailable);

    /// <summary>
    /// Displaying the initiator context is switched off by the application configuration (ICC-081).
    /// </summary>
    /// <remarks>Expression-bodied for the same reason as <see cref="InitiatorSnapshotUnavailable"/>.</remarks>
    public static ContextSuppressionReason DisplayDisabledByConfiguration
        => new(ContextSuppressionReasons.DisplayDisabledByConfiguration);

    /// <summary>
    /// No message declares the subject of a confirmation transaction (SPEC-039 E28): the question is
    /// not asked and the prompt carrying this reason is never sent to a channel.
    /// </summary>
    /// <remarks>Expression-bodied for the same reason as <see cref="InitiatorSnapshotUnavailable"/>.</remarks>
    public static ContextSuppressionReason ConfirmationTemplateUnavailable
        => new(ContextSuppressionReasons.ConfirmationTemplateUnavailable);

    /// <summary>
    /// Rejects an empty code so that a constructed reason can never collide with <c>default</c>.
    /// </summary>
    /// <param name="value">Reason code.</param>
    /// <returns>The validated code.</returns>
    private static string Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
