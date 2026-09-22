/*
 * Helio — the support section.
 *
 * An ordinary page in an ordinary browser, so the television limits of the neighbouring sample do
 * not apply here on their own merits. The syntax level is still ES2018, because the root
 * eslint.config.mjs pins it for the whole of samples/dotnet/showcase: the two samples share their
 * plumbing, and a script copied from here into streaming-tv must not stop parsing on a television.
 */

(function () {
  'use strict';

  var API_SESSION = '/api/session';
  var API_TICKETS = '/api/tickets';
  var API_RESOLUTION = '/api/tickets/{id}/resolution';
  var API_RESOLUTION_RESULT = '/api/tickets/resolutions/';

  var ACTION_RESOLVE = 'resolve';
  var ACTION_DELETE = 'delete';

  /* How often the page asks the backend whether the confirmation has ended. */
  var POLL_INTERVAL_MS = 2000;

  /* How many polls in a row may fail before the window gives up and offers a retry. A poll that
     answers resets the count, so a brief loss of network recovers by itself. */
  var MAX_CONSECUTIVE_POLL_FAILURES = 30;

  var CHANNEL_NAMES = {
    telegram: 'Telegram',
    whatsapp: 'WhatsApp',
    email: 'Email'
  };

  var STATUS_LABELS = {
    open: 'Open',
    waiting: 'Waiting for you',
    resolved: 'Resolved'
  };

  var state = {
    poll: null
  };

  /* ---------------------------------------------------------------- plumbing */

  function byId(id) {
    return document.getElementById(id);
  }

  function request(url, options) {
    var settings = options || {};
    settings.credentials = 'same-origin';

    return fetch(url, settings).then(function (response) {
      if (response.status === 204) {
        return null;
      }

      return response.text().then(function (body) {
        var parsed;
        try {
          parsed = body ? JSON.parse(body) : null;
        } catch (error) {
          parsed = null;
        }

        if (!response.ok) {
          var failure = new Error('HTTP ' + response.status);
          failure.status = response.status;
          failure.body = parsed;
          throw failure;
        }

        return parsed;
      });
    });
  }

  function postJson(url, payload) {
    return request(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
  }

  function clear(node) {
    while (node.firstChild) {
      node.removeChild(node.firstChild);
    }
  }

  function initialsOf(displayName) {
    var parts = String(displayName || '').split(/\s+/).filter(function (part) {
      return part.length > 0;
    });

    if (parts.length === 0) {
      return '?';
    }

    if (parts.length === 1) {
      return parts[0].charAt(0).toUpperCase();
    }

    return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase();
  }

  /* Draws the avatar, or the person's initials when the channel gave no picture. A Telegram account
     with a hidden profile photo is the ordinary case, not an error. */
  function renderAvatar(container, profile) {
    clear(container);

    if (profile.avatar) {
      var image = document.createElement('img');
      image.src = profile.avatar;
      image.alt = '';
      container.appendChild(image);
      return;
    }

    var initials = document.createElement('div');
    initials.className = 'initials';
    initials.textContent = initialsOf(profile.displayName);
    container.appendChild(initials);
  }

  /* The card of the customer, one line per field the channel actually gave. A field that came back
     null is left out entirely rather than drawn as an empty line. */
  function renderProfileFields(container, profile) {
    clear(container);

    var name = document.createElement('p');
    name.className = 'field-name';
    name.textContent = profile.displayName;
    container.appendChild(name);

    var fullName = [profile.givenName, profile.familyName].filter(Boolean).join(' · ');
    var lines = [
      { text: fullName, className: 'field' },
      { text: profile.channelType, className: 'field field-channel' },
      { text: profile.username ? '@' + profile.username : null, className: 'field' },
      { text: profile.channelUserIdMasked ? 'ID ' + profile.channelUserIdMasked : null, className: 'field' },
      { text: profile.phoneMasked, className: 'field' },
      { text: profile.email, className: 'field' }
    ];

    lines.forEach(function (line) {
      if (!line.text) {
        return;
      }

      var node = document.createElement('p');
      node.className = line.className;
      node.textContent = line.text;
      container.appendChild(node);
    });
  }

  function formatDate(value) {
    var moment = new Date(value);

    return isNaN(moment.getTime()) ? '' : moment.toISOString().slice(0, 10);
  }

  /* ---------------------------------------------------------------- infographic */

  /* Three steps of a ticket, drawn rather than downloaded: nothing under wwwroot comes from outside
     this origin, so there is no image file and no web font to fetch. No xmlns attribute is needed —
     assigning to innerHTML runs the HTML parser, which puts <svg> into the SVG namespace itself. */
  function renderInfographic(container) {
    var steps = [
      { x: 40, label: 'You write', caption: 'in any channel' },
      { x: 250, label: 'We answer', caption: 'in the same place' },
      { x: 460, label: 'You confirm', caption: 'closing the ticket' }
    ];

    var parts = ['<svg viewBox="0 0 640 150" role="img" aria-label="How a ticket travels">'];

    steps.forEach(function (step, index) {
      if (index > 0) {
        parts.push('<line x1="' + (step.x - 60) + '" y1="55" x2="' + (step.x - 20) + '" y2="55" '
          + 'stroke="currentColor" stroke-width="2" opacity="0.25"/>');
      }

      parts.push('<circle cx="' + (step.x + 45) + '" cy="55" r="26" fill="none" '
        + 'stroke="currentColor" stroke-width="2" opacity="0.45"/>');
      parts.push('<text x="' + (step.x + 45) + '" y="63" text-anchor="middle" '
        + 'font-size="22" font-weight="700" fill="currentColor">' + (index + 1) + '</text>');
      parts.push('<text x="' + (step.x + 45) + '" y="105" text-anchor="middle" '
        + 'font-size="16" font-weight="600" fill="currentColor">' + step.label + '</text>');
      parts.push('<text x="' + (step.x + 45) + '" y="127" text-anchor="middle" '
        + 'font-size="13" fill="currentColor" opacity="0.6">' + step.caption + '</text>');
    });

    parts.push('</svg>');
    container.innerHTML = parts.join('');
  }

  /* ---------------------------------------------------------------- tickets */

  function actionButton(label, className, onClick) {
    var button = document.createElement('button');
    button.className = 'button ' + className;
    button.type = 'button';
    button.textContent = label;
    button.addEventListener('click', onClick);

    return button;
  }

  function renderTickets(tickets) {
    var list = byId('tickets');
    clear(list);

    byId('tickets-empty').hidden = tickets.length > 0;

    tickets.forEach(function (ticket) {
      var item = document.createElement('li');
      item.className = 'ticket';

      var head = document.createElement('div');
      head.className = 'ticket-head';

      var id = document.createElement('span');
      id.className = 'ticket-id';
      id.textContent = '#' + ticket.id + ' · ' + formatDate(ticket.createdAt);
      head.appendChild(id);

      var status = document.createElement('span');
      status.className = 'ticket-status status-' + ticket.status;
      status.textContent = STATUS_LABELS[ticket.status] || ticket.status;
      head.appendChild(status);

      item.appendChild(head);

      var subject = document.createElement('p');
      subject.className = 'ticket-subject';
      subject.textContent = ticket.subject;
      item.appendChild(subject);

      var message = document.createElement('p');
      message.className = 'ticket-message';
      message.textContent = ticket.lastMessage;
      item.appendChild(message);

      var actions = document.createElement('div');
      actions.className = 'ticket-actions';

      if (ticket.status !== 'resolved') {
        actions.appendChild(actionButton('Resolved', 'button-ghost', function () {
          askConfirmation(ticket, ACTION_RESOLVE);
        }));
      }

      actions.appendChild(actionButton('Delete', 'button-ghost button-danger', function () {
        askConfirmation(ticket, ACTION_DELETE);
      }));

      item.appendChild(actions);
      list.appendChild(item);
    });
  }

  function loadTickets() {
    return request(API_TICKETS).then(function (answer) {
      renderTickets(answer.tickets);
    });
  }

  function loadSession() {
    return request(API_SESSION).then(function (session) {
      if (!session.authenticated) {
        renderInfographic(byId('infographic'));
        byId('section-public').hidden = false;
        byId('section-account').hidden = true;
        byId('header-right').hidden = true;

        return null;
      }

      renderAvatar(byId('header-avatar'), session.profile);
      byId('header-name').textContent = session.profile.displayName;
      byId('header-right').hidden = false;

      renderAvatar(byId('card-avatar'), session.profile);
      renderProfileFields(byId('card-fields'), session.profile);

      byId('section-public').hidden = true;
      byId('section-account').hidden = false;

      return loadTickets();
    });
  }

  /* ---------------------------------------------------------------- confirmation in the page */

  /* The first of the two questions, and the cheap one: it costs nothing and prevents a misclick.
     The second question — the one that actually decides — is asked in the customer's channel. */
  function askConfirmation(ticket, action) {
    var deleting = action === ACTION_DELETE;

    byId('confirm-title').textContent = deleting
      ? 'Delete ticket #' + ticket.id + '?'
      : 'Mark ticket #' + ticket.id + ' as resolved?';
    byId('confirm-text').textContent = deleting
      ? 'The history of “' + ticket.subject + '” is removed for good. We will ask you to confirm this in your messenger.'
      : '“' + ticket.subject + '” will be closed. We will ask you to confirm this in your messenger.';
    byId('confirm-accept').textContent = deleting ? 'Delete' : 'Mark resolved';

    byId('confirm').hidden = false;

    byId('confirm-accept').onclick = function () {
      byId('confirm').hidden = true;
      startResolution(ticket, action);
    };
  }

  byId('confirm-cancel').addEventListener('click', function () {
    byId('confirm').hidden = true;
  });

  /* ---------------------------------------------------------------- step-up */

  function overlayButton(label, className, onClick) {
    var button = document.createElement('button');
    button.className = 'button ' + className;
    button.type = 'button';
    button.textContent = label;
    button.addEventListener('click', onClick);

    return button;
  }

  function setStatus(text, tone) {
    var status = byId('stepup-status');
    status.className = 'stepup-status' + (tone ? ' is-' + tone : '');
    status.textContent = text;
  }

  /* Closing the window stops the polling. The transaction itself lives out its time inside Veriqa —
     asking again simply creates a new one. */
  function closeStepUp() {
    if (state.poll) {
      window.clearTimeout(state.poll);
      state.poll = null;
    }

    byId('stepup').hidden = true;
  }

  /* The same rule as Veriqa's own sign-in page: a touch screen alone is not a phone (a touch-screen
     laptop still needs the QR), so the media query and the User-Agent must agree. A tablet counts as
     a desktop here — an uncertain device keeps the QR, which another device can always scan. */
  function isPhone() {
    var query = window.matchMedia;
    if (!query || (!query('(pointer: coarse)').matches && !query('(hover: none)').matches)) {
      return false;
    }

    var uaData = navigator.userAgentData;

    return uaData && typeof uaData.mobile === 'boolean'
      ? uaData.mobile
      : /Mobi|iPhone|iPod/i.test(navigator.userAgent || '');
  }

  function channelName(channelType) {
    return CHANNEL_NAMES[channelType] || 'your messenger';
  }

  /* On a phone the person confirms on the very device, so the way in is a button that opens the
     messenger; anywhere else it is the QR, scanned by the phone. */
  function renderChannelEntry(started) {
    var qr = byId('stepup-qr');
    clear(qr);

    if (started.url && isPhone()) {
      qr.hidden = true;

      var open = document.createElement('a');
      open.className = 'button button-primary';
      open.href = started.url;
      open.textContent = 'Approve in ' + channelName(started.channelType);

      var actions = byId('stepup-actions');
      actions.insertBefore(open, actions.firstChild);

      setStatus('Open ' + channelName(started.channelType) + ' and confirm there…', null);
      return;
    }

    qr.hidden = false;

    if (started.qr) {
      var image = document.createElement('img');
      image.src = started.qr;
      image.alt = 'QR code opening the confirmation';
      qr.appendChild(image);
    } else {
      var empty = document.createElement('div');
      empty.className = 'stepup-qr-empty';
      empty.textContent = 'No code to show — try again';
      qr.appendChild(empty);
    }

    setStatus('Scan the code and confirm in ' + channelName(started.channelType) + '…', null);
  }

  function startResolution(ticket, action) {
    byId('stepup-title').textContent = action === ACTION_DELETE
      ? 'Deleting ticket #' + ticket.id
      : 'Closing ticket #' + ticket.id;
    clear(byId('stepup-qr'));
    setStatus('Creating the confirmation…', null);

    var actions = byId('stepup-actions');
    clear(actions);
    actions.appendChild(overlayButton('Cancel', 'button-ghost', closeStepUp));

    byId('stepup').hidden = false;

    postJson(API_RESOLUTION.replace('{id}', encodeURIComponent(ticket.id)), { action: action })
      .then(function (started) {
        renderChannelEntry(started);
        pollResolution(started.transactionId, ticket, action, 0);
      })
      .catch(function (error) {
        if (error.status === 409) {
          setStatus('This ticket is already resolved.', 'warn');
        } else if (error.status === 404) {
          setStatus('This ticket is no longer in your list.', 'warn');
        } else {
          setStatus('The confirmation could not be created.', 'bad');
        }

        var failedActions = byId('stepup-actions');
        clear(failedActions);
        failedActions.appendChild(overlayButton('Close', 'button-ghost', function () {
          closeStepUp();
          loadTickets();
        }));
      });
  }

  function finishResolution(resolution, ticket, action) {
    var actions = byId('stepup-actions');
    clear(actions);

    if (resolution.state === 'applied') {
      setStatus(action === ACTION_DELETE
        ? 'Confirmed. The ticket is deleted.'
        : 'Confirmed. The ticket is closed.', 'ok');
    } else if (resolution.state === 'mismatched') {
      setStatus('Confirmed by another account — the ticket was left as it was.', 'bad');
    } else {
      setStatus('Not confirmed (' + resolution.outcome + '). The ticket was left as it was.', 'warn');
      actions.appendChild(overlayButton('Try again', 'button-ghost', function () {
        startResolution(ticket, action);
      }));
    }

    actions.appendChild(overlayButton('Close', 'button-primary', function () {
      closeStepUp();
      loadTickets();
    }));
  }

  function pollResolution(transactionId, ticket, action, failures) {
    state.poll = window.setTimeout(function () {
      request(API_RESOLUTION_RESULT + encodeURIComponent(transactionId))
        .then(function (resolution) {
          if (resolution.state === 'pending') {
            pollResolution(transactionId, ticket, action, 0);
            return;
          }

          state.poll = null;
          finishResolution(resolution, ticket, action);
        })
        .catch(function () {
          if (failures + 1 >= MAX_CONSECUTIVE_POLL_FAILURES) {
            state.poll = null;
            setStatus('No answer about the confirmation — check the connection.', 'bad');

            var actions = byId('stepup-actions');
            clear(actions);
            actions.appendChild(overlayButton('Try again', 'button-ghost', function () {
              startResolution(ticket, action);
            }));
            actions.appendChild(overlayButton('Close', 'button-primary', function () {
              closeStepUp();
              loadTickets();
            }));

            return;
          }

          pollResolution(transactionId, ticket, action, failures + 1);
        });
    }, POLL_INTERVAL_MS);
  }

  /* ---------------------------------------------------------------- start */

  loadSession();
})();
