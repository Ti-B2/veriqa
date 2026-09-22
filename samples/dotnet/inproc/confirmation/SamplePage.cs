// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Sample.DotNet.Inproc.Confirmation;

/// <summary>
/// The one page of the sample: a form, the QR of the created confirmation, and the outcome.
/// It talks only to the backend half of this application, never to Veriqa directly.
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
          <title>Veriqa sample — server-to-server confirmation</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 32rem; margin: 2rem auto; padding: 0 1rem; }
            label { display: block; margin: .5rem 0; }
            input { width: 100%; padding: .4rem; box-sizing: border-box; }
            img { display: block; margin: 1rem 0; max-width: 100%; }
            pre { background: #f4f4f4; padding: .75rem; overflow-x: auto; }
          </style>
        </head>
        <body>
          <h1>Approve a payment</h1>
          <form id="form">
            <label>Amount <input name="amount" value="42.00 EUR" maxlength="32" required /></label>
            <label>Payee <input name="payee" value="ACME Ltd" maxlength="64" /></label>
            <button type="submit">Ask for approval</button>
          </form>
          <section id="entry" hidden>
            <p>Scan with your phone, or <a id="link" target="_blank" rel="noopener">open the link</a>:</p>
            <img id="qr" alt="Confirmation QR code" />
          </section>
          <p id="status"></p>
          <pre id="details" hidden></pre>
          <script src="/sample.js"></script>
        </body>
        </html>
        """;

    /// <summary>The page script: create the confirmation, show its QR, poll the outcome.</summary>
    public const string Script = """
        const form = document.getElementById('form');
        const status = document.getElementById('status');
        const details = document.getElementById('details');

        form.addEventListener('submit', async (event) => {
          event.preventDefault();
          details.hidden = true;
          const data = Object.fromEntries(new FormData(form));
          const response = await fetch('/payments/approval', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
          });
          const body = await response.json();
          if (!response.ok) { show('Refused', body); return; }

          document.getElementById('qr').src = body.qr;
          document.getElementById('link').href = body.url;
          document.getElementById('entry').hidden = false;
          status.textContent = 'Waiting for the confirmation…';
          poll(body.transactionId);
        });

        async function poll(transactionId) {
          const response = await fetch('/payments/approval/' + encodeURIComponent(transactionId));
          const body = await response.json();
          if (!response.ok) { show('Refused', body); return; }
          if (body.outcome === 'pending') { setTimeout(() => poll(transactionId), 2000); return; }
          document.getElementById('entry').hidden = true;
          show('Outcome: ' + body.outcome, body);
        }

        function show(text, body) {
          status.textContent = text;
          details.textContent = JSON.stringify(body, null, 2);
          details.hidden = false;
        }
        """;
}
