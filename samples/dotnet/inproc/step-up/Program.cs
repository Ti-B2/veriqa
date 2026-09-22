// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

// Runnable sample: step-up confirmation of a destructive action. The user is already working in the
// application; deleting a project is irreversible, so the backend asks the OWNER of the account to
// confirm it in their trusted channel, and deletes only when the person who confirmed is that owner:
//
//   1. create the confirmation with expected_identities   POST /api/transaction/confirmation
//   2. poll the outcome and the match                      GET  /api/transaction/{id}/result
//   3. delete only on outcome = confirmed AND matched_type = telegram_user_id
//
// A real application knows the owner's channel identity from account linking. To keep the sample
// self-contained, the account is bound to the first person who confirms a deletion: that confirmation
// is exchanged for an id_token and its channel_user_id becomes the owner. Every later deletion expects
// that owner, and a confirmation by anyone else deletes nothing.
//
// To keep the sample one process, the same application is both the Veriqa issuer (inproc) and the
// relying-party backend that calls it over HTTP, exactly as in the confirmation sample.

using Veriqa.Core.AuthServer.DependencyInjection;
using Veriqa.Core.ChannelAdapter.DependencyInjection;
using Veriqa.Sample.DotNet.Inproc.StepUp;

var builder = WebApplication.CreateBuilder(args);

// The Veriqa issuer. The client entry projects-backend in appsettings.json declares the action type
// delete-project and the comparable identity type telegram_user_id — without that declaration an
// expected_identities member is refused with candidate_type_undeclared.
builder.Services.AddVeriqaAuthServer(builder.Configuration, builder.Environment, authServer =>
{
    authServer.UseInMemoryOpenIddictStore();
    authServer.ConfigureTransactionEngine(te => te.UseInMemoryStore());
    authServer.AddChannelAdapters(adapters => adapters.AddTelegram());
});

// The relying-party backend half.
var sampleOptions = builder.Configuration.GetSection(SampleOptions.SectionName)
    .Get<SampleOptions>() ?? new SampleOptions();
builder.Services.AddSingleton(sampleOptions);
builder.Services.AddSingleton<ProjectWorkspace>();
// The access token belongs to the client, not to a call: held here for the whole process, it survives
// the short-lived client the typed HttpClient hands out per call.
builder.Services.AddSingleton<VeriqaAccessTokenCache>();
builder.Services.AddHttpClient<VeriqaConfirmationClient>(http => http.BaseAddress = new Uri(sampleOptions.Authority));

var app = builder.Build();

app.UseVeriqaAuthServer();
app.MapVeriqaAuthServer();

app.MapGet("/", () => Results.Content(SamplePage.Html, "text/html; charset=utf-8"));
app.MapGet(SamplePage.ScriptPath, () => Results.Content(SamplePage.Script, "text/javascript; charset=utf-8"));

app.MapGet("/workspace", (ProjectWorkspace workspace) => Results.Ok(workspace.Snapshot()));

app.MapPost("/workspace/owner/reset", (ProjectWorkspace workspace) =>
{
    workspace.ResetOwner();
    return Results.Ok(workspace.Snapshot());
});

app.MapPost("/projects/{projectId}/deletion", async (
    string projectId,
    ProjectWorkspace workspace,
    VeriqaConfirmationClient veriqa,
    CancellationToken cancellationToken) =>
{
    var project = workspace.Find(projectId);
    if (project is null)
    {
        return Results.NotFound();
    }

    // The step-up itself: once an owner is known, the confirmation is addressed to that person.
    var owner = workspace.Owner;
    var expected = owner is null
        ? null
        : new Dictionary<string, string> { [ProjectWorkspace.OwnerIdentityType] = owner };

    try
    {
        var created = await veriqa.CreateAsync(
            new Dictionary<string, string>
            {
                ["project"] = project.Name,
                ["documents"] = project.Documents.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            expected,
            cancellationToken);

        // The transaction is remembered against the project on the server: the page never tells the
        // backend which project a confirmed transaction deletes.
        workspace.StartDeletion(created.TransactionId, project.Id);

        return Results.Ok(new
        {
            transactionId = created.TransactionId,
            url = created.ChannelEntry.Url,
            qr = created.ChannelEntry.Qr,
            expectsOwner = owner is not null
        });
    }
    catch (VeriqaCallException refused)
    {
        return Results.Content(refused.Body, "application/json", statusCode: refused.StatusCode);
    }
});

app.MapGet("/deletions/{transactionId}", async (
    string transactionId,
    ProjectWorkspace workspace,
    VeriqaConfirmationClient veriqa,
    CancellationToken cancellationToken) =>
{
    var deletion = workspace.FindDeletion(transactionId);
    if (deletion is null)
    {
        return Results.NotFound();
    }

    if (deletion.Verdict is not null)
    {
        return Results.Ok(deletion);
    }

    try
    {
        var result = await veriqa.GetResultAsync(transactionId, cancellationToken);

        if (result.Outcome == "pending")
        {
            return Results.Ok(deletion);
        }

        if (result.Outcome != VeriqaConfirmationClient.ConfirmedOutcome)
        {
            return Results.Ok(workspace.Conclude(transactionId, result.Outcome, DeletionVerdict.NotDeleted));
        }

        if (workspace.Owner is null)
        {
            // No owner yet: the first person to confirm becomes the owner of the account.
            var claims = await veriqa.ExchangeForIdTokenClaimsAsync(transactionId, cancellationToken);
            workspace.BindOwner(claims.GetProperty("channel_user_id").GetString()!);

            return Results.Ok(workspace.Conclude(transactionId, result.Outcome, DeletionVerdict.DeletedOwnerBound));
        }

        // The decision a step-up exists for: confirmed is not enough, it has to be the expected person.
        var verdict = result.MatchedType == ProjectWorkspace.OwnerIdentityType
            ? DeletionVerdict.Deleted
            : DeletionVerdict.ConfirmedByAnotherPerson;

        return Results.Ok(workspace.Conclude(transactionId, result.Outcome, verdict));
    }
    catch (VeriqaCallException refused)
    {
        return Results.Content(refused.Body, "application/json", statusCode: refused.StatusCode);
    }
});

app.Run();
