# Veriqa.Core.BaseChannels

Metapackage of the base Veriqa channel set: it carries no library of its own, only the dependencies
that make up the usual starting set — the channel contour plus the Telegram, WhatsApp and Email
channels. One `dotnet add package` instead of four, and no API of its own appears at the consumer.

MAX is deliberately left out: it is installed separately with
`dotnet add package Veriqa.Core.ChannelAdapter.Max`. A deployment that wants a single channel takes
that channel's package instead of this one; installing both the metapackage and a separate channel
is fine — NuGet resolves it to one copy.

## Install

```bash
dotnet add package Veriqa.Core.BaseChannels
```

## Wiring

The channels arrive as their own packages, so the registration is theirs:

```csharp
authServer.AddChannelAdapters(adapters =>
{
    adapters.AddTelegram();
    adapters.AddWhatsApp();
    adapters.AddEmail();
});
```

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
