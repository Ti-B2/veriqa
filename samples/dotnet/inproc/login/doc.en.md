### Prerequisites

- .NET SDK 10.0+
- `dotnet add package Veriqa.Core.AuthServer`
- Channels: `dotnet add package Veriqa.Core.BaseChannels` (Telegram, WhatsApp, Email) and
  `dotnet add package Veriqa.Core.ChannelAdapter.Max`; if you need a single channel, install just
  its package (`Veriqa.Core.ChannelAdapter.Telegram`, …)
- (optional) a demo bot token for a real messenger sign-in

### Run steps

1. Paste the integration fragment into `Program.cs`.
2. `dotnet run --project samples/dotnet/inproc/login`
3. Open `/` and click "Run sign-in".

### About this combination

login — standard user sign-in through a trusted channel. The inproc mode runs the OpenIddict
issuer inside the app process with ephemeral keys. Channels: `telegram`, `whatsapp`,
`max`, `email` — all 4 are registered and toggled individually via configuration
(`Veriqa:Channels:<Channel>:Enabled`). All are disabled by default, so `dotnet run` starts without
external tokens/SMTP; set the credentials of the channel you need for a real sign-in.

:::note Real OIDC sign-in: the code and guide apply to the run, no mock.
