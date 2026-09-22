// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

// Runnable sample: sign-in (login) with Veriqa embedded under .NET Aspire orchestration
// (aspire). This file is the AppHost — the entry point of the whole sample:
// `dotnet run` here starts the store, the application and the Aspire dashboard together.
//
// What differs from the plain in-process sample is NOT the Veriqa wiring (it is the same
// AddVeriqaAuthServer call, see app/Program.cs) but who owns the infrastructure: the AppHost
// declares the transaction store as a resource and hands its address to the application through
// service discovery, so the application carries no endpoint of the store in its configuration.
//
// The fragment between the `region:snippet` / `endregion:snippet` markers is the single source
// of truth for the code snippet on the demo site: the demo-stand extractor cuts out
// the body by this marker. Change the orchestration ONLY here — the snippet updates itself.

var builder = DistributedApplication.CreateBuilder(args);

// region:snippet
// Resource names of the orchestration. The store name is also the name of the connection string
// Aspire injects into the application, so the same literal lives in app/Program.cs — keep both
// in sync (anti-magic-string).
const string TransactionStoreResourceName = "transactions";
const string AppResourceName = "veriqa-app";

// Transaction store as an orchestrated resource: Aspire starts Redis and publishes its
// connection string under the resource name. WithDataVolume keeps pending transactions across
// container restarts. The store is NOT published outside — it is reachable only inside the
// Aspire network.
var transactions = builder
    .AddRedis(TransactionStoreResourceName)
    .WithDataVolume();

// The application that embeds Veriqa. WithReference injects
// ConnectionStrings__transactions — this is the whole delta against the in-process mode:
// the address of the store is resolved by service discovery, not configured by hand.
builder
    .AddProject<Projects.Veriqa_Sample_DotNet_Aspire_Login_App>(AppResourceName)
    .WithReference(transactions)
    .WaitFor(transactions);
// endregion:snippet

builder.Build().Run();
