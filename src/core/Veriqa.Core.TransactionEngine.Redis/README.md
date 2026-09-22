# Veriqa.Core.TransactionEngine.Redis

Redis transaction store for the Veriqa Transaction Engine — a satellite package. It keeps
authentication transactions in Redis, so several replicas of a host see one transaction and a
sign-in started on one of them completes on another. Nothing but this package pulls a Redis client
into the dependency graph of a deployment that does not use it.

Transactions are short-lived by nature, which is what makes a key-value store with an expiry a
natural home for them; a deployment that needs them in a relational database installs the EF Core
satellite instead.

## Install

```bash
dotnet add package Veriqa.Core.TransactionEngine.Redis
```

## Wiring

```csharp
authServer.ConfigureTransactionEngine(te =>
    te.UseRedisStore(redis => redis.Configuration = "localhost:6379"));
```

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
