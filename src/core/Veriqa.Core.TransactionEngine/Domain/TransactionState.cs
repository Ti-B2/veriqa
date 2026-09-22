// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Transaction state machine states.
/// Defines the lifecycle: Created → Pending → Confirmed → Completed / Expired / Failed.
/// </summary>
public enum TransactionState
{
    /// <summary>
    /// Transaction created but not yet handed to the user.
    /// </summary>
    Created,

    /// <summary>
    /// Transaction is active, waiting for the user's action in the channel.
    /// </summary>
    Pending,

    /// <summary>
    /// The user confirmed the action in the channel. Awaiting finalization.
    /// </summary>
    Confirmed,

    /// <summary>
    /// Transaction completed successfully. Resolved identity obtained.
    /// </summary>
    Completed,

    /// <summary>
    /// Transaction expired by TTL.
    /// </summary>
    Expired,

    /// <summary>
    /// Transaction ended with an error.
    /// </summary>
    Failed
}
