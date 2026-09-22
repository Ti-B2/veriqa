// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

// Runnable sample: human-in-the-loop approval of an AI agent's action. A support agent handles a
// refund ticket with three tools known in advance — lookup_order, refund_payment, send_customer_email.
// Reading and writing to the customer run on their own; refund_payment moves money, so every call of it
// waits for a person to approve it in their trusted channel, with the amount and the order in front of
// them:
//
//   agent -> refund_payment(...) -> ApprovalGate -> POST /api/transaction/confirmation (action approve-refund)
//                                                 -> GET  /api/transaction/{id}/result until it ends
//         <- executed on confirmed, "not executed" on declined / expired
//
// The approval is enforced where tools are invoked, not in the prompt: whatever the model — or, in this
// sample, the fixed plan standing in for it — decides to call, a tool with an approval policy does not
// run without a confirmed transaction. The person sees the slot values of a declared action type, never
// text the agent wrote.
//
// To keep the sample one process, the same application is both the Veriqa issuer (inproc) and the
// backend that runs the agent and calls Veriqa over HTTP, exactly as in the confirmation sample.

using System.Collections.Concurrent;
using Veriqa.Core.AuthServer.DependencyInjection;
using Veriqa.Core.ChannelAdapter.DependencyInjection;
using Veriqa.Sample.DotNet.Inproc.AgentApproval;

var builder = WebApplication.CreateBuilder(args);

// The Veriqa issuer. The client entry support-agent in appsettings.json declares the action type
// approve-refund: the question the person is asked, with its slots.
builder.Services.AddVeriqaAuthServer(builder.Configuration, builder.Environment, authServer =>
{
    authServer.UseInMemoryOpenIddictStore();
    authServer.ConfigureTransactionEngine(te => te.UseInMemoryStore());
    authServer.AddChannelAdapters(adapters => adapters.AddTelegram());
});

// The backend half: the agent, its tools and the approval gate.
var sampleOptions = builder.Configuration.GetSection(SampleOptions.SectionName)
    .Get<SampleOptions>() ?? new SampleOptions();
builder.Services.AddSingleton(sampleOptions);
// The access token belongs to the client, not to a call: held here for the whole process, it survives
// the short-lived client the typed HttpClient hands out per call.
builder.Services.AddSingleton<VeriqaAccessTokenCache>();
builder.Services.AddHttpClient<VeriqaConfirmationClient>(http => http.BaseAddress = new Uri(sampleOptions.Authority));
builder.Services.AddSingleton<SupportTools>();
builder.Services.AddTransient<ApprovalGate>();
builder.Services.AddTransient<SupportAgent>();

var runs = new ConcurrentDictionary<string, AgentRun>(StringComparer.Ordinal);

var app = builder.Build();

app.UseVeriqaAuthServer();
app.MapVeriqaAuthServer();

app.MapGet("/", () => Results.Content(SamplePage.Html, "text/html; charset=utf-8"));
app.MapGet(SamplePage.ScriptPath, () => Results.Content(SamplePage.Script, "text/javascript; charset=utf-8"));

app.MapPost("/agent/runs", (
    RefundRequest request,
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime) =>
{
    var run = new AgentRun();
    runs[run.Id] = run;

    // The agent works in the background, as an agent does; the page reads its log while it waits.
    _ = Task.Run(async () =>
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var agent = scope.ServiceProvider.GetRequiredService<SupportAgent>();
            await agent.RunAsync(run, request, lifetime.ApplicationStopping);
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            run.Complete("Stopped: the application is shutting down.");
        }
        catch (Exception failure)
        {
            // Nobody awaits this task, so a failure the agent does not report itself — a timeout of the
            // HTTP client, an answer of an unexpected shape — is logged and ends the run here; otherwise
            // it would reach no log and leave the page polling a run that stopped.
            app.Logger.LogError(failure, "Agent run {RunId} failed.", run.Id);
            run.Complete($"Stopped: the run failed ({failure.GetType().Name}); see the application log.");
        }
    });

    return Results.Ok(new { id = run.Id });
});

app.MapGet("/agent/runs/{runId}", (string runId) =>
    runs.TryGetValue(runId, out var run) ? Results.Ok(run.Snapshot()) : Results.NotFound());

app.Run();
