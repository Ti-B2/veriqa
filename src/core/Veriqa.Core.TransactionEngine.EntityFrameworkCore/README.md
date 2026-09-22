# Veriqa.Core.TransactionEngine.EntityFrameworkCore

EF Core transaction store for the Veriqa Transaction Engine — a satellite package. The core engine
ships with an in-memory store; installing this one moves authentication transactions into a
relational database without the core taking a dependency on an ORM.

The database provider (Npgsql and friends) and the connection string belong to the host: this
package takes the `DbContextOptionsBuilder` you configure and nothing else. The migrations live in
their own package per provider — for PostgreSQL that is
`Veriqa.Core.TransactionEngine.Migrations.PostgreSql`.

## Install

```bash
dotnet add package Veriqa.Core.TransactionEngine.EntityFrameworkCore
```

## Wiring

```csharp
authServer.ConfigureTransactionEngine(te =>
    te.UseEfCoreStore(ef => ef.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsAssembly("Veriqa.Core.TransactionEngine.Migrations.PostgreSql"))));
```

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
