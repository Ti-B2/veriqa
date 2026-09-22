# Veriqa.Core.TransactionEngine

The lifecycle of an authentication / confirmation transaction: the state machine, the transaction
service, the pluggable transaction store and in-process event publishing. The store shipped inside
this package is the in-memory one; the EF Core and Redis stores and the RabbitMQ publisher live in
satellite packages (`Veriqa.Core.TransactionEngine.EntityFrameworkCore`,
`Veriqa.Core.TransactionEngine.Redis`, `Veriqa.Core.TransactionEngine.RabbitMq`), so a deployment
never receives an ORM or a broker client it does not use.

The finalization sequence — confirm → resolve identity → complete, compensating a failed
completion — lives here too, in `ChannelTransactionFinalizer.ConfirmAndCompleteAsync`
(`Veriqa.Core.TransactionEngine.Services`). It drives the state machine and the identity
resolution and touches no channel, so every path that finalizes a sign-in (the channel webhook,
the core confirmation page, any future one) reaches it without depending on a channel assembly.

## Writing your own transaction store

`ITransactionStore` is a published contract: an implementation over SQL Server, Mongo, DynamoDB or
anything else lives in your own assembly and needs no access to Veriqa internals.

### Register your store before `AddVeriqaTransactionEngine`

The engine wraps the store it composed with its own state-transition guard — the check that keeps a
forbidden state out of the storage no matter who produced the transaction. It can only wrap what is
already in the collection, so the registration has to come first:

```csharp
// Either register it yourself, above the engine…
builder.Services.AddSingleton<ITransactionStore, MyStore>();
builder.Services.AddVeriqaTransactionEngine(builder.Configuration);

// …or hand it to the engine builder, which is the same thing in one step.
builder.Services.AddVeriqaTransactionEngine(builder.Configuration, engine => engine.UseInMemoryStore());
```

A store registered **after** the engine wins the resolution and would carry no guard. That is not
left to be discovered later: the engine refuses to start and the message names both the cause and
the fix.

### Rehydration — snapshot in, snapshot out

A transaction does not expose public setters for its lifecycle fields (`State`, `StateReasonCode`,
`UpdatedAt`, the three outcome snapshots, `ConcurrencyToken`): moving a transaction is the business
of the state machine alone. A store does not move a transaction — it restores one that already
moved — so it gets a path of its own, and only that one:

```csharp
// Writing: take the whole transaction as one object.
var snapshot = transaction.ToSnapshot();

// Reading: restore it with every field exactly as it was stored.
var restored = Transaction.Restore(new TransactionSnapshot
{
    Id = snapshot.Id,
    Type = snapshot.Type,
    State = snapshot.State,               // required — not the default state
    CreatedAt = snapshot.CreatedAt,
    UpdatedAt = snapshot.UpdatedAt,       // required
    ExpiresAt = snapshot.ExpiresAt,
    AllowedChannelTypes = snapshot.AllowedChannelTypes,
    ConcurrencyToken = snapshot.ConcurrencyToken,   // required — never a fresh one
    // …every remaining field
});
```

`State`, `UpdatedAt` and `ConcurrencyToken` are `required` on `TransactionSnapshot` even though
`Transaction` gives them defaults. That is deliberate: a store that forgets them would return a
transaction sitting in its default state with a freshly generated token — the state machine and
optimistic concurrency would both break, silently, with nothing to observe. `required` turns that
into a compilation error.

Do not construct a `Transaction` with an object initializer inside a store, and do not reflect over
its properties. `Restore` freezes `AllowedChannelTypes` for you, so any read-only set will do.

**Obligation when the model grows.** `TransactionSnapshot` is the full field perimeter of a
transaction. A field added to `Transaction` must be added here as well, in the same change —
otherwise stores keep compiling and quietly stop carrying it.

### Obligations of AddAsync and UpdateAsync

- `AddAsync` **must throw** `DuplicateIdempotencyKeyException` when a transaction with the same
  (`IdempotencyScope`, `IdempotencyKey`) pair is already stored. Idempotency in the engine rests on
  that exception; returning the existing transaction instead, or overwriting it, breaks idempotency
  with no failure to observe. The uniqueness check must be atomic with the insert — a unique index
  or a conditional write, not a read followed by a write.
- `UpdateAsync` **must compare** the stored concurrency token with `expectedConcurrencyToken` and
  return `false` on a mismatch instead of writing. The comparison and the write must be one atomic
  operation (a conditional update, a `WHERE token = …` clause, a compare-and-swap).
- `UpdateAsync` **must not write** a state the stored transaction cannot reach.
  `TransactionStateMachine.CanTransition` — public for exactly this — decides, over the state
  currently stored and the state the passed transaction carries. A state equal to the stored one is
  not a transition, so a write that changes only other fields goes through. The refusal is an
  `InvalidStateTransitionException`, never a `false`: `false` means a lost optimistic lock or a
  transaction that is no longer stored, and a caller answers it by re-reading and retrying — which
  for a forbidden transition never terminates. Throw it **only when the concurrency token still
  matches**, that is, when the write would otherwise have been applied: a caller working off a stale
  copy has already lost the optimistic lock, and the transition its copy appears to make is an
  artefact of that copy — it keeps getting `false`, and its retry terminates, because the re-read
  copy carries the state actually stored.
- `ISupportsNativeExpiry` is a marker for a store whose backend removes terminal records itself
  (a TTL). Implement it and the cleanup service stops deleting those records; it still scans for
  expired transactions in order to publish `TransactionExpiredEvent`.

## Request context of a transaction: tenant, application, locale, time zone, ui_config

Five dimensions travel with a transaction, carried by one of two containers — `OidcContext` for a
transaction started by the OIDC sign-in flow, `TransactionRequestContext` for a server-to-server
one. The two are never populated together: their writers are different.

Read them **only** through `TransactionContextExtensions` — `GetTenantId()`, `GetApplicationId()`,
`GetUiLocale()`, `GetUiTimeZone()`, `GetUiConfigCode()`. Each applies the same precedence: the request-context
container, then the OIDC context, then `null`. Reaching into a container directly is how a reader
ends up seeing one kind of transaction and not the other.

Both containers are serialized whole by the store, so the tenant needs no column and no schema
change of its own.

### Where the tenant comes from

The tenant is resolved from `client_id` by the `IClientTenantResolver` port — once per request, at
every entry point of the auth server that knows the client but has no transaction to read the tenant
off (no ambient tenant scope is open on those HTTP paths; only the channel paths open one):

- the sign-in (`/connect/authorize`) stores the answer on `OidcContext.TenantId` of the transaction it
  creates;
- server-to-server creation of a confirmation transaction (`POST /api/transaction/confirmation`)
  stores it on the request context of the transaction it creates (`TransactionRequestContext.TenantId`);
- the `form_post` response page, issued after the transaction is gone, asks again for its branding —
  the mapping is deterministic, so the answer is the one the sign-in stored.

Every later reader of a transaction takes the tenant off the transaction (`GetTenantId`), not from the
resolver. **The engine's own implementation always answers
`null`** — the default implicit tenant of an installation that never heard of tenants, and the right
answer for a host using the engine without the auth server. Where the auth server is present, the
answer comes from the calling client's own configuration entry
(`Veriqa:OpenIddict:Clients[].TenantId`): `AddVeriqaOpenIddict` displaces the engine's null answer
through `ReplaceShippedClientTenantResolver<TResolver>()` — the seam a component wired AFTER the
engine uses to take over the slot, because `TryAdd` alone would leave the null standing.
Multi-tenant hosts register their own:

```csharp
builder.Services.AddSingleton<IClientTenantResolver, MyClientTenantResolver>();
```

Your registration wins over both the engine's answer and the auth server's, whatever the call
order, as long as it is last-wins — `AddSingleton` or `Replace`. `TryAdd` wins only while the slot
is still free, that is, before either the engine or the auth server has registered one. A resolver
that throws fails none of the requests above — sign-in, confirmation creation or the `form_post`
page: the tenant is treated as unstated, the request resolves past the tenant level, and the failure
is logged. Cancellation of the request is not swallowed.

The tenant is a convenience for resolving tenant-level configuration keys — **not an access
boundary**. Isolation is provided by `client_id`, which sits above the tenant; no store method is
narrowed by tenant and none refuses a transaction over a tenant mismatch.

### Keys that carry the tenant

Two keys include the tenant so that tenants do not share a budget or a record:

- the per-user rate-limiter key — `<tenant>:<channel_type>:<channel_user_id>`;
- the channel-identity key — the triple (tenant, `ChannelType`, `ChannelUserId`).

An unstated tenant is spelled one fixed way in both (`TenantKey.Default`), so `null`
and an empty string never produce two keys for one default tenant.

Both keys read the tenant off the same field — `ChannelIdentitySnapshot.TenantId`, written by the
adapter — so the counter and the record of one user always belong to the same tenant. A channel
routed per tenant (Telegram, Max, WhatsApp) takes that value from the ambient scope the host opens
for the inbound request; Email has no tenant routing at all — neither its pages nor its inbound
webhook carry a `{tenant}` segment — so it takes the value from the transaction being confirmed.

**Upgrade note.** These key shapes changed: rate-limiter counters and `ChannelIdentity` records
written before the upgrade are not found afterwards and are created anew. Both are short-lived
working data, so nothing is lost — but a per-user rate-limit window in flight across the upgrade
starts over.

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
