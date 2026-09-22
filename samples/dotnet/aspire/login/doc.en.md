### Prerequisites

- .NET SDK 10.0+
- A running Docker daemon (the transaction store is a container started by Aspire)
- `dotnet add package Veriqa.Core.AuthServer` and `Veriqa.Core.TransactionEngine.Redis`
- Channels: `dotnet add package Veriqa.Core.BaseChannels` (Telegram, WhatsApp, Email) and
  `dotnet add package Veriqa.Core.ChannelAdapter.Max`; if you need a single channel, install just
  its package (`Veriqa.Core.ChannelAdapter.Telegram`, …)
- (optional) a demo bot token for a real messenger sign-in

### Run steps

1. Paste the orchestration fragment into the AppHost `Program.cs`.
2. `dotnet run --project samples/dotnet/aspire/login`
3. Open the application from the Aspire dashboard and click "Run sign-in".

### About this combination

login — standard user sign-in through a trusted channel. The aspire mode differs
from inproc not in how Veriqa is plugged in — that is the same `AddVeriqaAuthServer` call — but
in who owns the infrastructure: the transaction store is declared as an AppHost resource and its
address reaches the application through service discovery (`ConnectionStrings:transactions`)
rather than configuration. Channels: `telegram`, `whatsapp`, `max`, `email` — all 4 are registered and
toggled individually (`Veriqa:Channels:<Channel>:Enabled`), all disabled by default.

:::note Running the application without its AppHost is not supported: with no store connection
string the startup fails explicitly — a guard against silently falling back to another store.
