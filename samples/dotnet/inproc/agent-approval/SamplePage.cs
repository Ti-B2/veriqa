// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Sample.DotNet.Inproc.AgentApproval;

/// <summary>
/// The one page of the sample: a refund ticket, the log of the agent handling it, and the QR of the
/// approval the agent is waiting for. It talks only to the backend half of this application.
/// </summary>
internal static class SamplePage
{
    /// <summary>Path the page script is served from.</summary>
    public const string ScriptPath = "/sample.js";

    /// <summary>
    /// The page markup. The script is a separate file rather than inline: the Veriqa security headers
    /// set a Content-Security-Policy that admits scripts from this origin only.
    /// </summary>
    public const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Veriqa sample — approving an agent's action</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 40rem; margin: 2rem auto; padding: 0 1rem; }
            label { display: block; margin: .5rem 0; }
            input { width: 100%; padding: .4rem; box-sizing: border-box; }
            img { display: block; margin: 1rem 0; max-width: 100%; }
            ol { padding-left: 1.2rem; font-family: ui-monospace, monospace; font-size: .9rem; }
            .approval { color: #b3261e; }
            .muted { color: #555; }
          </style>
        </head>
        <body>
          <h1>Support agent</h1>
          <p class="muted">Tools: lookup_order · refund_payment (needs a person's approval) · send_customer_email</p>
          <form id="form">
            <label>Order <input name="order" value="A-1042" maxlength="32" required /></label>
            <label>Amount <input name="amount" value="89.00 EUR" maxlength="32" required /></label>
            <label>Customer wrote <input name="reason" value="The headphones arrived broken" maxlength="120" required /></label>
            <button type="submit">Hand the ticket to the agent</button>
          </form>
          <section id="entry" hidden>
            <p id="prompt"></p>
            <p>Scan with your phone, or <a id="link" target="_blank" rel="noopener">open the link</a>:</p>
            <img id="qr" alt="Approval QR code" />
          </section>
          <ol id="log"></ol>
          <p id="status"></p>
          <script src="/sample.js"></script>
        </body>
        </html>
        """;

    /// <summary>The page script: start a run and follow its log until it completes.</summary>
    public const string Script = """
        const form = document.getElementById('form');
        const status = document.getElementById('status');

        form.addEventListener('submit', async (event) => {
          event.preventDefault();
          const data = Object.fromEntries(new FormData(form));
          const response = await fetch('/agent/runs', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
          });
          const body = await response.json();
          status.textContent = 'The agent is working…';
          follow(body.id);
        });

        async function follow(runId) {
          const run = await (await fetch('/agent/runs/' + encodeURIComponent(runId))).json();

          document.getElementById('log').replaceChildren(...run.log.map(entry => {
            const item = document.createElement('li');
            item.textContent = entry.at + '  ' + entry.kind.padEnd(8) + ' ' + entry.text;
            if (entry.kind === 'approval') { item.className = 'approval'; }
            return item;
          }));

          const entry = document.getElementById('entry');
          if (run.pending) {
            document.getElementById('prompt').textContent = 'The agent wants to call ' + run.pending.tool + '. Approve or decline in Telegram:';
            document.getElementById('qr').src = run.pending.qr;
            document.getElementById('link').href = run.pending.url;
            entry.hidden = false;
          } else {
            entry.hidden = true;
          }

          if (run.completion) { status.textContent = run.completion; return; }
          setTimeout(() => follow(runId), 1500);
        }
        """;
}
