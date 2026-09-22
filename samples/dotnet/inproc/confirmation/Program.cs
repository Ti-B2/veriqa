// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

// Runnable sample: a server-to-server confirmation. A relying-party backend asks a person to approve
// an action ("Approve a payment of 42.00 EUR to ACME?") in a trusted channel, with no sign-in and no
// browser redirect:
//
//   1. client_credentials token          POST /connect/token
//   2. create the confirmation           POST /api/transaction/confirmation  -> channel_entry.qr
//   3. poll the outcome                  GET  /api/transaction/{id}/result
//   4. (optional) who confirmed          POST /connect/token, grant urn:veriqa:params:oauth:grant-type:confirmation
//
// To keep the sample one process, the same application is both the Veriqa issuer (inproc) and the
// relying-party backend that calls it over HTTP. The backend half talks to Veriqa only through the
// public HTTP API, so pointing ConfirmationSample:Authority at a standalone Veriqa works unchanged.

using System.Collections.Concurrent;
using System.Text.Json;
using Veriqa.Core.AuthServer.DependencyInjection;
using Veriqa.Core.ChannelAdapter.DependencyInjection;
using Veriqa.Sample.DotNet.Inproc.Confirmation;

var builder = WebApplication.CreateBuilder(args);

// The Veriqa issuer. What makes the confirmation possible is configuration, not code: the client
// entry payments-backend in appsettings.json allows the Client Credentials grant and DECLARES the
// action type approve-payment in its own MessageTemplates (contract + templates).
builder.Services.AddVeriqaAuthServer(builder.Configuration, builder.Environment, authServer =>
{
    authServer.UseInMemoryOpenIddictStore();
    authServer.ConfigureTransactionEngine(te => te.UseInMemoryStore());
    authServer.AddChannelAdapters(adapters => adapters.AddTelegram());
});

// The relying-party backend half.
var sampleOptions = builder.Configuration.GetSection(ConfirmationSampleOptions.SectionName)
    .Get<ConfirmationSampleOptions>() ?? new ConfirmationSampleOptions();
builder.Services.AddSingleton(sampleOptions);
// The access token belongs to the client, not to a call: held here for the whole process, it survives
// the short-lived client the typed HttpClient hands out per call.
builder.Services.AddSingleton<VeriqaAccessTokenCache>();
builder.Services.AddHttpClient<VeriqaConfirmationClient>(http => http.BaseAddress = new Uri(sampleOptions.Authority));

// A transaction is redeemed for an id_token once, so the claims are kept for the page to read again.
var exchangedClaims = new ConcurrentDictionary<string, JsonElement>(StringComparer.Ordinal);

var app = builder.Build();

app.UseVeriqaAuthServer();
app.MapVeriqaAuthServer();

app.MapGet("/", () => Results.Content(SamplePage.Html, "text/html; charset=utf-8"));
app.MapGet(SamplePage.ScriptPath, () => Results.Content(SamplePage.Script, "text/javascript; charset=utf-8"));

app.MapPost("/payments/approval", async (
    PaymentApprovalForm form,
    VeriqaConfirmationClient veriqa,
    CancellationToken cancellationToken) =>
{
    try
    {
        var created = await veriqa.CreateAsync(
            new Dictionary<string, string>
            {
                [PaymentApprovalForm.AmountSlot] = form.Amount,
                [PaymentApprovalForm.PayeeSlot] = form.Payee
            },
            cancellationToken);

        // channel_entry.qr is a PNG data URI: the page shows it as an image for the phone to scan.
        return Results.Ok(new
        {
            transactionId = created.TransactionId,
            url = created.ChannelEntry.Url,
            qr = created.ChannelEntry.Qr,
            validUntil = created.ChannelEntry.ValidUntil
        });
    }
    catch (VeriqaCallException refused)
    {
        return Results.Content(refused.Body, "application/json", statusCode: refused.StatusCode);
    }
});

app.MapGet("/payments/approval/{transactionId}", async (
    string transactionId,
    VeriqaConfirmationClient veriqa,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await veriqa.GetResultAsync(transactionId, cancellationToken);

        if (result.Outcome != VeriqaConfirmationClient.ConfirmedOutcome)
        {
            return Results.Ok(new { outcome = result.Outcome });
        }

        if (!exchangedClaims.TryGetValue(transactionId, out var claims))
        {
            claims = await veriqa.ExchangeForIdTokenClaimsAsync(transactionId, cancellationToken);
            exchangedClaims[transactionId] = claims;
        }

        return Results.Ok(new { outcome = result.Outcome, claims });
    }
    catch (VeriqaCallException refused)
    {
        return Results.Content(refused.Body, "application/json", statusCode: refused.StatusCode);
    }
});

app.Run();

/// <summary>What the page posts: the values the backend puts into the caller slots.</summary>
internal sealed record PaymentApprovalForm(string Amount, string Payee)
{
    /// <summary>Caller slot of approve-payment the amount goes into (Contract:Slots in appsettings.json).</summary>
    public const string AmountSlot = "amount";

    /// <summary>Caller slot of approve-payment the payee goes into.</summary>
    public const string PayeeSlot = "payee";
}
