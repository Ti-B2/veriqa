// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.DataProtection;

using Veriqa.Core.TransactionEngine.Identity;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Reversible protection of the relying party's expectations over ASP.NET Core Data Protection
/// (SPEC-039 C23). The platform's own facility, not cryptography of ours (<c>core-rules §3</c>): the
/// key ring — Redis or the file system — is configured by the host, and key rotation is supported by
/// Data Protection itself, values written under an earlier key staying readable while that key is in
/// the ring.
/// </summary>
/// <remarks>
/// The implementation lives here rather than in the engine because the key ring belongs to the host:
/// the engine states the port and compares the values, the composition that has a key ring supplies
/// the means. No value ever reaches a log — a failure is reported as the fact alone.
/// </remarks>
internal sealed class DataProtectionIdentityValueProtector : IIdentityValueProtector
{
    /// <summary>
    /// Purpose string of this protector: it isolates these values from every other use of Data
    /// Protection in the process, so a payload of another purpose cannot be read as one of ours.
    /// </summary>
    private const string ProtectorPurpose = "Veriqa.Core.IdentityMatch.Expectations.v1";

    /// <summary>
    /// Protector of the fixed purpose above.
    /// </summary>
    private readonly IDataProtector _protector;

    /// <summary>
    /// Logger. Never carries a value — only the fact that one could not be restored.
    /// </summary>
    private readonly ILogger<DataProtectionIdentityValueProtector> _logger;

    /// <summary>
    /// Creates the protector from the Data Protection provider of the host.
    /// </summary>
    /// <param name="dataProtectionProvider">Data Protection provider (the key ring is the host's).</param>
    /// <param name="logger">Logger.</param>
    public DataProtectionIdentityValueProtector(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DataProtectionIdentityValueProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string Protect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return _protector.Protect(value);
    }

    /// <inheritdoc />
    public bool TryUnprotect(string protectedValue, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        try
        {
            value = _protector.Unprotect(protectedValue);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A key ring rotated without the previous keys, or a value written by another purpose.
            // The exception carries no value of ours, and the message deliberately names none either.
            _logger.LogWarning(
                exception,
                "A stored identity-match expectation could not be restored.");

            value = null;

            return false;
        }
    }
}
