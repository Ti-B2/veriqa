// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Veriqa.Sample.DotNet.Showcase.StreamingTv;

/// <summary>One title of the catalogue.</summary>
/// <param name="Id">Identifier used in the URLs.</param>
/// <param name="Title">Name shown on the card and in the confirmation.</param>
/// <param name="Genre">Genre shown on the card.</param>
/// <param name="Year">Year of release.</param>
/// <param name="Price">Price, already formatted for display.</param>
/// <param name="Currency">Currency of <paramref name="Price"/>.</param>
public sealed record CatalogTitle(string Id, string Title, string Genre, int Year, string Price, string Currency);

/// <summary>A title as one viewer sees it: the catalogue entry plus whether they own it.</summary>
/// <param name="Id">Identifier used in the URLs.</param>
/// <param name="Title">Name shown on the card.</param>
/// <param name="Genre">Genre shown on the card.</param>
/// <param name="Year">Year of release.</param>
/// <param name="Price">Price, already formatted for display.</param>
/// <param name="Currency">Currency of <paramref name="Price"/>.</param>
/// <param name="Owned">True when this viewer has already bought it.</param>
public sealed record CatalogTitleView(
    string Id,
    string Title,
    string Genre,
    int Year,
    string Price,
    string Currency,
    bool Owned);

/// <summary>What the backend decided about a purchase once its confirmation ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PurchaseState>))]
public enum PurchaseState
{
    /// <summary>The confirmation has not ended yet.</summary>
    [JsonStringEnumMemberName("pending")]
    Pending,

    /// <summary>Confirmed by the viewer who started it: the title is theirs.</summary>
    [JsonStringEnumMemberName("purchased")]
    Purchased,

    /// <summary>Declined, expired or failed: nothing was bought.</summary>
    [JsonStringEnumMemberName("refused")]
    Refused,

    /// <summary>Confirmed, but not by the viewer who started it: nothing was bought.</summary>
    [JsonStringEnumMemberName("mismatched")]
    Mismatched
}

/// <summary>A purchase waiting for its confirmation, or already decided.</summary>
/// <param name="TransactionId">The confirmation transaction.</param>
/// <param name="TitleId">The title it buys.</param>
/// <param name="State">What the backend did; <see cref="PurchaseState.Pending"/> until the confirmation ends.</param>
/// <param name="Outcome">Outcome of the confirmation: <c>pending</c> until it ends.</param>
public sealed record PurchaseView(string TransactionId, string TitleId, PurchaseState State, string Outcome);

/// <summary>
/// The in-memory state of the sample: the catalogue, who owns what, who has already been welcomed,
/// and the purchases in flight. In a real service this is the database; here a restart of the
/// process forgets everything, which is exactly what a sample should do.
/// <para>
/// Everything is keyed by the <c>sub</c> claim — the viewer Veriqa signed in — so two people
/// running the sample at once do not see each other's purchases.
/// </para>
/// </summary>
public sealed class CatalogStore
{
    /// <summary>Outcome of a confirmation that has not ended yet.</summary>
    public const string PendingOutcome = "pending";

    private readonly Lock _gate = new();

    private readonly List<CatalogTitle> _titles =
    [
        new("deep-field", "Deep Field", "Sci-Fi", 2024, "4.99", "EUR"),
        new("northern-lines", "Northern Lines", "Drama", 2023, "3.49", "EUR"),
        new("the-quiet-harbour", "The Quiet Harbour", "Documentary", 2025, "2.99", "EUR"),
        new("salt-and-iron", "Salt and Iron", "Thriller", 2022, "3.99", "EUR"),
        new("paper-satellites", "Paper Satellites", "Animation", 2024, "4.49", "EUR"),
        new("last-train-west", "Last Train West", "Western", 2021, "2.49", "EUR"),
        new("glass-orchard", "Glass Orchard", "Fantasy", 2025, "5.49", "EUR"),
        new("the-long-signal", "The Long Signal", "Sci-Fi", 2023, "3.99", "EUR")
    ];

    private readonly Dictionary<string, HashSet<string>> _ownedBySubject = new(StringComparer.Ordinal);
    private readonly HashSet<string> _welcomedSubjects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Purchase> _purchases = new(StringComparer.Ordinal);

    /// <summary>Whether this viewer has already seen the welcome screen.</summary>
    /// <param name="subject">The <c>sub</c> claim of the viewer.</param>
    public bool WelcomeSeen(string subject)
    {
        lock (_gate)
        {
            return _welcomedSubjects.Contains(subject);
        }
    }

    /// <summary>Remembers that this viewer has seen the welcome screen; later sign-ins go straight to the catalogue.</summary>
    /// <param name="subject">The <c>sub</c> claim of the viewer.</param>
    public void MarkWelcomeSeen(string subject)
    {
        lock (_gate)
        {
            _welcomedSubjects.Add(subject);
        }
    }

    /// <summary>The catalogue as this viewer sees it, with their own titles marked as owned.</summary>
    /// <param name="subject">The <c>sub</c> claim of the viewer.</param>
    public IReadOnlyList<CatalogTitleView> CatalogueFor(string subject)
    {
        lock (_gate)
        {
            var owned = _ownedBySubject.GetValueOrDefault(subject);

            return [.. _titles.Select(title => new CatalogTitleView(
                title.Id,
                title.Title,
                title.Genre,
                title.Year,
                title.Price,
                title.Currency,
                owned?.Contains(title.Id) is true))];
        }
    }

    /// <summary>A title by id, or null.</summary>
    /// <param name="titleId">Identifier of the title.</param>
    public CatalogTitle? Find(string titleId)
    {
        lock (_gate)
        {
            return _titles.Find(title => title.Id == titleId);
        }
    }

    /// <summary>Whether this viewer already owns the title.</summary>
    /// <param name="subject">The <c>sub</c> claim of the viewer.</param>
    /// <param name="titleId">Identifier of the title.</param>
    public bool Owns(string subject, string titleId)
    {
        lock (_gate)
        {
            return _ownedBySubject.GetValueOrDefault(subject)?.Contains(titleId) is true;
        }
    }

    /// <summary>
    /// Remembers which title a confirmation transaction buys, and for whom. The page never tells the
    /// backend what a confirmed transaction paid for — the binding is made here, on the server.
    /// </summary>
    /// <param name="transactionId">The confirmation transaction.</param>
    /// <param name="titleId">The title it buys.</param>
    /// <param name="subject">The <c>sub</c> claim of the viewer who started it.</param>
    public void StartPurchase(string transactionId, string titleId, string subject)
    {
        lock (_gate)
        {
            _purchases[transactionId] = new Purchase(transactionId, titleId, subject, PendingOutcome, null);
        }
    }

    /// <summary>
    /// A purchase of THIS viewer by its transaction, or null — a transaction started by someone else
    /// is answered the same way as one that never existed.
    /// </summary>
    /// <param name="transactionId">The confirmation transaction.</param>
    /// <param name="subject">The <c>sub</c> claim of the viewer asking.</param>
    public PurchaseView? FindPurchase(string transactionId, string subject)
    {
        lock (_gate)
        {
            var purchase = _purchases.GetValueOrDefault(transactionId);

            return purchase is null || !string.Equals(purchase.Subject, subject, StringComparison.Ordinal)
                ? null
                : View(purchase);
        }
    }

    /// <summary>
    /// Records the verdict of a purchase once, and hands the title over when the verdict says so. A
    /// purchase already decided keeps its first verdict, so polling it again costs Veriqa nothing.
    /// </summary>
    /// <param name="transactionId">The confirmation transaction.</param>
    /// <param name="outcome">Outcome Veriqa reported.</param>
    /// <param name="state">What the backend decided to do about it.</param>
    /// <returns>The purchase as the page sees it.</returns>
    public PurchaseView Conclude(string transactionId, string outcome, PurchaseState state)
    {
        lock (_gate)
        {
            var purchase = _purchases[transactionId];
            if (purchase.State is not null)
            {
                return View(purchase);
            }

            if (state is PurchaseState.Purchased)
            {
                if (!_ownedBySubject.TryGetValue(purchase.Subject, out var owned))
                {
                    owned = new HashSet<string>(StringComparer.Ordinal);
                    _ownedBySubject[purchase.Subject] = owned;
                }

                owned.Add(purchase.TitleId);
            }

            purchase = purchase with { Outcome = outcome, State = state };
            _purchases[transactionId] = purchase;

            return View(purchase);
        }
    }

    /// <summary>The wire shape of a purchase: a verdict not yet reached reads as pending.</summary>
    private static PurchaseView View(Purchase purchase) =>
        new(purchase.TransactionId, purchase.TitleId, purchase.State ?? PurchaseState.Pending, purchase.Outcome);

    /// <summary>
    /// A purchase as the store keeps it. <c>Subject</c> stays here and never reaches the page, and a
    /// null <c>State</c> is what "still waiting" looks like inside the store.
    /// </summary>
    private sealed record Purchase(
        string TransactionId,
        string TitleId,
        string Subject,
        string Outcome,
        PurchaseState? State);
}
