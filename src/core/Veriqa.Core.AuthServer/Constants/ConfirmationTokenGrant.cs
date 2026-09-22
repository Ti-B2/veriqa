// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Protocol names of the confirmation token grant (SPEC-039 C51): the extension grant through which
/// the client that created a confirmation transaction exchanges it, once, for the identity token of
/// the confirming party.
/// </summary>
public static class ConfirmationTokenGrant
{
    /// <summary>
    /// Grant type — an absolute URI, as RFC 6749 §4.5 requires of an extension grant.
    /// </summary>
    public const string GrantType = "urn:veriqa:params:oauth:grant-type:confirmation";

    /// <summary>
    /// Name of the token request parameter carrying the public transaction identifier returned by the
    /// server-to-server creation entry.
    /// </summary>
    public const string TransactionIdParameter = "transaction_id";
}
