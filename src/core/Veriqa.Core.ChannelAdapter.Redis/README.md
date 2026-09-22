# Veriqa.Core.ChannelAdapter.Redis

Redis-backed channel stores for Veriqa — a satellite package. It replaces the in-process defaults of
three channel stores with Redis ones: the coordinates of a sent prompt, Email action tokens and
Email push correlations. With them in Redis, a magic link opened against another replica of the host
still resolves, and a prompt sent from one replica is still editable from another.

Redis stays out of the channel contour's dependency graph: a deployment that runs one replica never
receives a Redis client.

## Install

```bash
dotnet add package Veriqa.Core.ChannelAdapter.Redis
```

## Wiring

```csharp
authServer.AddChannelAdapters(adapters =>
{
    adapters.AddEmail();
    adapters.UseRedisChannelStores(redis => redis.Configuration = "localhost:6379");
});
```

The three ports are registered unconditionally, so the call works before or after `AddEmail()`.

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
