# Veriqa.Core.ChannelAdapter.WhatsApp

WhatsApp channel adapter for Veriqa — a satellite package. The user confirms a desktop sign-in in
WhatsApp over the Meta Cloud API, and the adapter turns that confirmation into the identity the core
issues tokens for.

It is one channel out of several: a deployment installs the channels it actually uses, and only the
Meta Cloud API client travels with this one.

## Install

```bash
dotnet add package Veriqa.Core.ChannelAdapter.WhatsApp
```

The channel contour (`Veriqa.Core.ChannelAdapter`) arrives with this package — it is not installed
separately.

## Wiring

```csharp
authServer.AddChannelAdapters(adapters => adapters.AddWhatsApp());
```

The adapter activates only when its configuration section `Veriqa:Channels:WhatsApp` is enabled
(`"Enabled": true`); otherwise only its options and their validator are registered.

## License

Mozilla Public License 2.0 — see the `LICENSE` file in the repository root; the package carries
the `MPL-2.0` license expression in its metadata.

---
Veriqa™ — © Dmitrii Erusov. Sources and mirrors: https://veriqa.app · devs@veriqa.app
