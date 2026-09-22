// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Everything the server tells the client script of the sign-in window: the values of THIS request
/// (texts, URLs, the remainder of the transaction) and the application constants the page compares
/// against (status names, hub methods, attribute names). It is the whole seam between the two — the
/// script file itself carries no server value, which is what lets it be a real .js instead of a
/// string literal inside the renderer.
/// <para>
/// The object travels into the page as a JSON data block (<see cref="AuthPageScript.ConfigElementId"/>)
/// and is read once when the script starts. Property names of the JSON are the names of these
/// properties in camelCase — the naming policy is stated in <see cref="AuthPageScript"/>, so a
/// renamed property here renames the field the script reads, and the compiler does not catch it: the
/// pair of names is checked by reading the two files together.
/// </para>
/// </summary>
internal sealed record AuthPageScriptConfig
{
    /// <summary>External session identifier the page tracks.</summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Application path base (the issuer under a sub-path of the origin, e.g. <c>/demo</c>). Every
    /// root-relative URL of the page gets it applied by the script's own helper, in one place.
    /// </summary>
    public required string BasePath { get; init; }

    /// <summary>
    /// Milliseconds left of the transaction's lifetime when the page was built. The countdown adds it
    /// to the browser's own clock reading, so no clock has to be shared with the server.
    /// </summary>
    public required long RemainingMs { get; init; }

    /// <summary>Interval of the status fallback polling, in milliseconds (SPEC-007 §5.4).</summary>
    public required int PollIntervalMs { get; init; }

    /// <summary>
    /// Whether the page continues on the browser callback of the sign-in path. <see langword="false"/>
    /// — the transaction has no such callback and its outcome is shown in place.
    /// </summary>
    public required bool NavigatesAway { get; init; }

    /// <summary>Root-relative URL of the transaction status surface polled by the fallback.</summary>
    public required string StatusUrl { get; init; }

    /// <summary>
    /// Root-relative callback the page navigates to on a terminal outcome; empty — the page has no
    /// callback of its own.
    /// </summary>
    public required string CallbackUrl { get; init; }

    /// <summary>
    /// Root-relative URL of the core page asking the confirming question — the single destination of a
    /// page without a callback (SPEC-039 E29); empty on the sign-in path.
    /// </summary>
    public required string ConfirmUrl { get; init; }

    /// <summary>Root-relative URL the email form posts the magic-link request to.</summary>
    public required string EmailStartUrl { get; init; }

    /// <summary>
    /// Channel type of the email adapter — the one channel whose intermediate statuses the page shows
    /// (SPEC-016 §10.1).
    /// </summary>
    public required string EmailChannelType { get; init; }

    /// <summary>
    /// Name of the root attribute carrying the state of the page. The script writes it and the
    /// stylesheet decides what each state shows, so both sides keep one source for the name.
    /// </summary>
    public required string PageStateAttribute { get; init; }

    /// <summary>Value of <see cref="PageStateAttribute"/> for a page whose transaction ran out of time.</summary>
    public required string ExpiredPageState { get; init; }

    /// <summary>
    /// Reason code of a transaction the user declined — the one code the terminal line is branched on,
    /// because a refusal and a fault both arrive as a failed transaction.
    /// </summary>
    public required string DeclinedReasonCode { get; init; }

    /// <summary>Lifecycle status names the page compares the incoming status against.</summary>
    public required AuthPageScriptStatuses Statuses { get; init; }

    /// <summary>Localized texts the page shows, already resolved for the language of the request.</summary>
    public required AuthPageScriptTexts Texts { get; init; }

    /// <summary>Addresses of the SignalR hub the page subscribes to.</summary>
    public required AuthPageScriptHub Hub { get; init; }

    /// <summary>
    /// Intermediate email statuses and the line each of them puts on the page: the status key as the
    /// adapter reports it, the localized text as the value. A status missing from the map leaves the
    /// status line alone.
    /// </summary>
    public required IReadOnlyDictionary<string, string> EmailStatusTexts { get; init; }
}

/// <summary>
/// Lifecycle status names as the server spells them, handed to the page so that no status string is
/// hardcoded on the client.
/// </summary>
/// <param name="Confirmed">The user confirmed on the channel; the outcome is being processed.</param>
/// <param name="AwaitingWebConfirmation">The core asks the confirming question on a page of its own.</param>
/// <param name="Completed">Terminal success.</param>
/// <param name="Expired">The transaction ran out of time.</param>
/// <param name="Failed">Terminal failure — a refusal by the user or a fault, told apart by the reason.</param>
internal sealed record AuthPageScriptStatuses(
    string Confirmed,
    string AwaitingWebConfirmation,
    string Completed,
    string Expired,
    string Failed);

/// <summary>
/// The page's texts, already localized and, where the caller worded a terminal outcome of its own,
/// already taken from the caller rather than from the renderer's sign-in wording.
/// </summary>
/// <param name="Confirmed">Line shown while a confirmed transaction is being processed.</param>
/// <param name="Success">Terminal line of a completed transaction.</param>
/// <param name="Expired">Terminal line of an expired transaction.</param>
/// <param name="Error">Terminal line of a failure that is not the user's refusal.</param>
/// <param name="Declined">Terminal line of a transaction the user declined.</param>
/// <param name="EmailSent">Confirmation that the letter was sent; carries the <c>{0}</c> slot of the address.</param>
/// <param name="EmailError">Line shown when the letter could not be sent.</param>
internal sealed record AuthPageScriptTexts(
    string Confirmed,
    string Success,
    string Expired,
    string Error,
    string Declined,
    string EmailSent,
    string EmailError);

/// <summary>
/// Addresses of the SignalR hub of the window: where to connect and what the methods are called.
/// Application constants, handed over for the same reason the status names are.
/// </summary>
/// <param name="Path">Root-relative path of the hub.</param>
/// <param name="StatusMethod">Hub method announcing a lifecycle status change.</param>
/// <param name="ChannelStatusMethod">Hub method announcing a channel-specific intermediate status.</param>
/// <param name="JoinMethod">Hub method the page calls to join the transaction's group.</param>
internal sealed record AuthPageScriptHub(
    string Path,
    string StatusMethod,
    string ChannelStatusMethod,
    string JoinMethod);
