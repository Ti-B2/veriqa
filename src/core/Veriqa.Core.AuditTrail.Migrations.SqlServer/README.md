# Veriqa.Core.AuditTrail.Migrations.SqlServer

SQL Server migrations for the audit journal schema of Veriqa. The package contains no API of its
own: it is the migrations assembly a host names by string when it configures the EF Core audit sink,
plus the design-time factory `dotnet ef` needs.

A host that takes the audit trail from NuGet cannot create the journal schema on SQL Server without
it. Migrations are per provider by EF Core design, so a different database gets a different package.

## Install

```bash
dotnet add package Veriqa.Core.AuditTrail.Migrations.SqlServer
```

## Wiring

```csharp
services.AddVeriqaAuditTrail(audit =>
    audit.UseEfCoreSink(ef => ef.UseSqlServer(
        connectionString,
        sqlServer =>
        {
            sqlServer.MigrationsAssembly("Veriqa.Core.AuditTrail.Migrations.SqlServer");
            sqlServer.MigrationsHistoryTable(AuditMigrationDefaults.MigrationsHistoryTable);
        })));
```

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
