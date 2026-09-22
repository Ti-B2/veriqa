# Veriqa.Core.AuthServer.Migrations.PostgreSql

PostgreSQL migrations for the OpenIddict store of the Veriqa auth server — clients, scopes,
authorizations and tokens. The package contains no API of its own: it is the migrations assembly a
host names by string when it configures the OpenIddict `DbContext`, plus the design-time factory
`dotnet ef` needs.

A host that takes the core from NuGet cannot create the OpenIddict schema without it. Migrations are
per provider by EF Core design, so a different database gets a different package.

## Install

```bash
dotnet add package Veriqa.Core.AuthServer.Migrations.PostgreSql
```

## Wiring

```csharp
authServer.UseOpenIddictDatabase(ef => ef.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly("Veriqa.Core.AuthServer.Migrations.PostgreSql")));
```

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
