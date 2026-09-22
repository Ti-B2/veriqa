// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Marker interface: the store manages removal of terminal transactions natively
/// (e.g., Redis TTL).
/// TransactionCleanupService skips CleanupTerminalTransactionsAsync for such stores,
/// but still runs ExpireTransactionsAsync to publish TransactionExpiredEvent.
/// </summary>
/// <remarks>
/// Public because the statement belongs to the store, and a store may live outside the Veriqa
/// assemblies: a third-party implementation over a backend with its own TTL (DynamoDB, Mongo,
/// Cosmos) has no other way to say so, and without the marker the cleanup service would delete
/// records the backend has already taken care of.
/// </remarks>
public interface ISupportsNativeExpiry { }
