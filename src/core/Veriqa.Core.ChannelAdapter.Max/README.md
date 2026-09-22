# Veriqa.Core.ChannelAdapter.Max

MAX channel adapter for Veriqa — a satellite package. The user confirms a desktop sign-in in a MAX
bot, and the adapter turns that confirmation into the identity the core issues tokens for.

MAX is deliberately not part of the `Veriqa.Core.BaseChannels` metapackage: it is installed with an
explicit `dotnet add package` by the deployments that need it.

## Install

```bash
dotnet add package Veriqa.Core.ChannelAdapter.Max
```

The channel contour (`Veriqa.Core.ChannelAdapter`) arrives with this package — it is not installed
separately.

## Wiring

```csharp
authServer.AddChannelAdapters(adapters => adapters.AddMax());
```

The adapter activates only when its configuration section `Veriqa:Channels:Max` is enabled
(`"Enabled": true`); otherwise only its options and their validator are registered.

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
