// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Sample.DotNet.Inproc.StepUp;

/// <summary>
/// The one page of the sample: the projects, the QR of a deletion waiting for confirmation, and the
/// verdict. It talks only to the backend half of this application, never to Veriqa directly.
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
          <title>Veriqa sample — step-up before deleting</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 34rem; margin: 2rem auto; padding: 0 1rem; }
            li { display: flex; justify-content: space-between; align-items: center; margin: .4rem 0; }
            img { display: block; margin: 1rem 0; max-width: 100%; }
            pre { background: #f4f4f4; padding: .75rem; overflow-x: auto; }
            .muted { color: #555; }
          </style>
        </head>
        <body>
          <h1>Projects</h1>
          <p id="owner" class="muted"></p>
          <ul id="projects"></ul>
          <section id="entry" hidden>
            <p id="prompt"></p>
            <p>Scan with your phone, or <a id="link" target="_blank" rel="noopener">open the link</a>:</p>
            <img id="qr" alt="Confirmation QR code" />
          </section>
          <p id="status"></p>
          <pre id="details" hidden></pre>
          <button id="reset" type="button">Forget the owner</button>
          <script src="/sample.js"></script>
        </body>
        </html>
        """;

    /// <summary>The page script: list the projects, ask for a deletion, poll its verdict.</summary>
    public const string Script = """
        const status = document.getElementById('status');
        const details = document.getElementById('details');

        const verdicts = {
          Deleted: 'Deleted — confirmed by the owner.',
          DeletedOwnerBound: 'Deleted — and the person who confirmed is now the owner of this account.',
          ConfirmedByAnotherPerson: 'Not deleted — confirmed, but not by the owner.',
          NotDeleted: 'Not deleted.'
        };

        async function load() {
          const workspace = await (await fetch('/workspace')).json();
          document.getElementById('owner').textContent = workspace.owner
            ? 'Owner: Telegram user ' + workspace.owner + '. Deletions have to be confirmed by this person.'
            : 'No owner yet: the first person to confirm a deletion becomes the owner.';
          const list = document.getElementById('projects');
          list.replaceChildren(...workspace.projects.map(project => {
            const item = document.createElement('li');
            item.textContent = project.name + ' · ' + project.documents + ' documents';
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = 'Delete';
            button.addEventListener('click', () => requestDeletion(project));
            item.append(button);
            return item;
          }));
        }

        async function requestDeletion(project) {
          details.hidden = true;
          const response = await fetch('/projects/' + encodeURIComponent(project.id) + '/deletion', { method: 'POST' });
          const body = await response.json();
          if (!response.ok) { show('Refused', body); return; }

          document.getElementById('prompt').textContent = body.expectsOwner
            ? 'Deleting "' + project.name + '" needs the owner\'s confirmation.'
            : 'Deleting "' + project.name + '" needs a confirmation.';
          document.getElementById('qr').src = body.qr;
          document.getElementById('link').href = body.url;
          document.getElementById('entry').hidden = false;
          status.textContent = 'Waiting for the confirmation…';
          poll(body.transactionId);
        }

        async function poll(transactionId) {
          const response = await fetch('/deletions/' + encodeURIComponent(transactionId));
          const body = await response.json();
          if (!response.ok) { show('Refused', body); return; }
          if (body.verdict === null) { setTimeout(() => poll(transactionId), 2000); return; }
          document.getElementById('entry').hidden = true;
          show(verdicts[body.verdict] + ' (outcome: ' + body.outcome + ')', body);
          load();
        }

        function show(text, body) {
          status.textContent = text;
          details.textContent = JSON.stringify(body, null, 2);
          details.hidden = false;
        }

        document.getElementById('reset').addEventListener('click', async () => {
          await fetch('/workspace/owner/reset', { method: 'POST' });
          load();
        });

        load();
        """;
}
