// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// String constants of transaction types.
/// The type is set at creation and cannot be changed during the lifecycle.
/// The set of types is extended by adding new constants.
/// </summary>
public static class TransactionTypes
{
    /// <summary>
    /// Primary sign-in through a trusted channel.
    /// </summary>
    public const string Login = "login";

    /// <summary>
    /// Confirmation of an action without a separate sign-in.
    /// </summary>
    public const string Confirmation = "confirmation";

    /// <summary>
    /// All valid transaction types (an immutable set with O(1) lookup).
    /// </summary>
    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Login,
        Confirmation
    }.ToFrozenSet();

    /// <summary>
    /// Checks whether the given type is valid.
    /// </summary>
    /// <param name="type">Transaction type.</param>
    /// <returns>true if the type is valid.</returns>
    public static bool IsValid(string? type) =>
        type is not null && All.Contains(type);
}
