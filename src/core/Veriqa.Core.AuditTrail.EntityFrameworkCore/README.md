# Veriqa.Core.AuditTrail.EntityFrameworkCore

EF Core sink for the Veriqa audit trail — a satellite package. The journal itself (the event-bus
receiver, the append-only store and the retention sweep) ships in `Veriqa.Core.AuditTrail`; this
package moves the records into a relational database without the journal taking a dependency on an
ORM.

The database provider and the connection string belong to the host. The migrations live in their own
package per provider — for PostgreSQL that is `Veriqa.Core.AuditTrail.Migrations.PostgreSql`.

## Install

```bash
dotnet add package Veriqa.Core.AuditTrail.EntityFrameworkCore
```

## Wiring

```csharp
services.AddVeriqaAuditTrail(audit =>
    audit.UseEfCoreSink(ef => ef.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsAssembly("Veriqa.Core.AuditTrail.Migrations.PostgreSql"))));
```

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
