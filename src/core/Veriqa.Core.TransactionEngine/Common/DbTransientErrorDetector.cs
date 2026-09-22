// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Common;

/// <summary>
/// Shared detector of transient database connection errors for schema initialization services
/// (migrations at startup). Provider-agnostic: provider exceptions (Npgsql, MySQL)
/// inherit DbException and override IsTransient — no separate branches are needed.
/// </summary>
public static class DbTransientErrorDetector
{
    /// <summary>
    /// Determines whether an exception is a transient database connection error.
    /// Retrying non-transient errors (access rights, wrong schema) makes no sense.
    /// </summary>
    /// <param name="ex">Exception.</param>
    /// <returns>true if the error is transient and a retry makes sense.</returns>
    public static bool IsTransient(Exception ex)
    {
        // Check the whole exception chain:
        // SocketException — network failure (the database is not up yet),
        // TimeoutException — connection timeout,
        // DbException.IsTransient — the provider's own diagnostics (including Npgsql/MySQL)
        return ex is System.Net.Sockets.SocketException
            || ex is TimeoutException
            || ex is System.Data.Common.DbException dbException && dbException.IsTransient
            || (ex.InnerException is not null && IsTransient(ex.InnerException));
    }
}
