// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0
//
// Client script of the sign-in window of the "Core" contour (rendered by
// DefaultAuthPageRenderer): the SignalR subscription, the polling fallback, the countdown,
// the channel tabs, the tooltip and the email form.
//
// The file ships as an embedded resource of Veriqa.Core.AuthServer and is INLINED into the
// generated document inside a <script> tag carrying the page's CSP nonce — it is never served
// over a URL, because the page has to stay self-contained and no second request may stand
// between the user and the window.
//
// Every comment of this file reaches the end user's browser verbatim, so the norms its code
// implements are named in AuthPageScript (C#), not here.
//
// Nothing of the server is spelled into this file. Every value the request decides — texts,
// URLs, status names, timings — arrives in the JSON data block the renderer emits directly
// above it (<script type="application/json" id="veriqa-auth-config">), which is what keeps
// this a real .js file: a linter, an editor and a reviewer read it as code instead of as a
// C# string literal.
(function() {
    "use strict";

    // The one seam with the server (see the header): the page's configuration, read once.
    // The block is emitted by the same renderer that inlines this file, so its absence is not
    // a state the page can be in.
    // The id below reads the block emitted by the constant AuthPageScript.ConfigElementId: the
    // reader and the emitter are what bind, and nothing checks that they agree — not the compiler,
    // not the component tests, which read the markup only. Rename them together; the name as it
    // appears in comments and documents (the header above included) binds nothing and follows by
    // a grep for the old value.
    var config = JSON.parse(document.getElementById("veriqa-auth-config").textContent);
    var sessionId = config.sessionId;

    // Application path base (issuer under a sub-path of the origin, e.g. /demo).
    // All root-relative page URLs (fetch, SignalR, redirect from
    // server messages) are normalized via withBase — the server sends them
    // without the prefix (built from application constants).
    var BASE_PATH = config.basePath;
    function withBase(url) {
        if (!BASE_PATH || !url) return url;
        // Only root-relative paths ("/x", but not protocol-relative "//x")
        return (url.charAt(0) === "/" && url.charAt(1) !== "/") ? BASE_PATH + url : url;
    }

    var statusEl = document.getElementById("veriqa-status");
    var spinnerEl = document.getElementById("veriqa-spinner");
    var countdownEl = document.getElementById("veriqa-countdown");

    // The moment the countdown runs to, expressed on THIS browser's clock: the
    // server's remainder added to the browser's own reading of "now". A clock
    // that is minutes off cancels out of the difference, so only its drift over
    // the lifetime of one transaction is left, and that is negligible.
    var deadline = Date.now() + config.remainingMs;
    var msgConfirmed = config.texts.confirmed;
    var msgSuccess = config.texts.success;
    var msgExpired = config.texts.expired;
    var msgError = config.texts.error;
    var msgDeclined = config.texts.declined;

    // The one reason code the terminal line is branched on: a refusal by the user
    // and a fault both arrive as Failed, and only the reason tells them apart
    // (no hardcoding on the client — the code is the server's constant).
    var REASON_DECLINED = config.declinedReasonCode;

    // Statuses from TransactionStatusNames (no hardcoding on the client)
    var STATUS_CONFIRMED = config.statuses.confirmed;
    var STATUS_AWAITING_WEB = config.statuses.awaitingWebConfirmation;
    var STATUS_COMPLETED = config.statuses.completed;
    var STATUS_EXPIRED = config.statuses.expired;
    var STATUS_FAILED = config.statuses.failed;

    // Email-specific intermediate statuses (no hardcoding)
    var CHANNEL_EMAIL = config.emailChannelType;
    var EMAIL_STATUS_TEXTS = config.emailStatusTexts;

    var isTerminal = false;

    // The page leaves for the callback exactly once. The status that sends it
    // there is NOT terminal (the transaction stays pending until the user
    // answers on the next page), so isTerminal cannot serve as that guard: a
    // later poll tick or SignalR message would otherwise reassign
    // window.location while the browser is already navigating.
    var isNavigating = false;

    // Channel switching (tabs) — via addEventListener, without inline onclick
    function switchChannel(channel) {
        document.querySelectorAll('.veriqa-panel').forEach(function(p) { p.style.display = 'none'; });
        document.querySelectorAll('.veriqa-tab').forEach(function(t) {
            t.classList.remove('active');
            t.setAttribute('aria-selected', 'false');
            t.setAttribute('tabindex', '-1');
        });
        var panel = document.getElementById('veriqa-panel-' + channel);
        if (panel) panel.style.display = 'block';
        var tab = document.querySelector('.veriqa-tab[data-channel="' + channel + '"]');
        if (tab) {
            tab.classList.add('active');
            tab.setAttribute('aria-selected', 'true');
            tab.setAttribute('tabindex', '0');
        }
    }

    // Binding handlers to the channel tabs: click plus roving arrow-key
    // navigation (WAI-ARIA Tabs pattern, automatic activation — selection follows
    // focus). ArrowRight/ArrowLeft move focus and selection to the adjacent tab in
    // DOM order with wrap-around; the switch itself reuses switchChannel (no
    // duplicated logic). Behavioral parity with the Site-contour preview (bindArrowNav).
    var channelTabs = document.querySelectorAll('.veriqa-tab');
    channelTabs.forEach(function(tab, index) {
        tab.addEventListener('click', function() {
            var channel = this.getAttribute('data-channel');
            if (channel) switchChannel(channel);
        });
        tab.addEventListener('keydown', function(e) {
            var forward = e.key === 'ArrowRight';
            var backward = e.key === 'ArrowLeft';
            if (!forward && !backward) return;
            e.preventDefault();
            var next = channelTabs[(index + (forward ? 1 : -1) + channelTabs.length) % channelTabs.length];
            var channel = next.getAttribute('data-channel');
            if (channel) switchChannel(channel);
            next.focus();
        });
    });

    // Tooltip of a clipped third-party label: it opens on hover
    // AND on keyboard focus and closes on Escape — the native title attribute
    // did neither, and on a touch surface it never appeared at all. The full
    // label stays in the DOM, so the accessible name is complete without the
    // bubble; the bubble repeats it visually and is hidden from assistive tech
    // (aria-hidden) so the name is not announced twice.

    // Keyboard focus, told apart from focus a mouse click left behind:
    // :focus-visible is exactly that distinction and is already the focus
    // indicator of the contour in CSS. Every target browser supports it, but
    // matches() throws on a selector an older one does not know, and the safe
    // answer there is "not keyboard" - the bubble then closes on pointer leave
    // instead of hanging over the page.
    function hasKeyboardFocus(el) {
        try { return !!el && el.matches(':focus-visible'); } catch (e) { return false; }
    }

    document.querySelectorAll('.veriqa-tooltip-host').forEach(function(host) {
        var bubble = host.querySelector('.veriqa-tooltip');
        var triggers = host.querySelectorAll('[data-veriqa-tooltip]');
        if (!bubble || triggers.length === 0) return;
        // One host serves the whole tab strip, so the bubble has an owner:
        // the trigger whose label it currently shows. "Something inside the
        // host is focused" is a different question and must not be mistaken
        // for it.
        var shownFor = null;
        function hideTooltip() { bubble.hidden = true; shownFor = null; }
        triggers.forEach(function(trigger) {
            function showTooltip() {
                bubble.textContent = trigger.getAttribute('data-veriqa-tooltip');
                bubble.hidden = false;
                shownFor = trigger;
            }
            trigger.addEventListener('mouseenter', showTooltip);
            trigger.addEventListener('focus', showTooltip);
            // Losing focus dismisses the bubble only while it still belongs to
            // this trigger: the pointer may have handed it over to another one.
            trigger.addEventListener('blur', function() {
                if (shownFor === trigger) hideTooltip();
            });
        });
        // The pointer may travel from the trigger onto the bubble itself
        // (WCAG 1.4.13, "hoverable"), so dismissal is bound to the host as a
        // whole. The bubble outlives the pointer only while its OWN trigger
        // holds the KEYBOARD focus. Plain "is the active element" is not that
        // test: a click gives the clicked tab the focus too, and the bubble
        // then stayed hanging over the QR code, swallowing clicks until Escape.
        host.addEventListener('mouseleave', function() {
            if (shownFor !== document.activeElement || !hasKeyboardFocus(shownFor)) hideTooltip();
        });
    });

    // Escape dismisses any open tooltip, including one opened by hover
    // (WCAG 1.4.13, "dismissible")
    document.addEventListener('keydown', function(e) {
        if (e.key !== 'Escape') return;
        document.querySelectorAll('.veriqa-tooltip').forEach(function(openBubble) {
            openBubble.hidden = true;
        });
    });

    // Email form: sending the magic link via AJAX
    var emailSubmitBtn = document.getElementById("veriqa-email-submit");
    if (emailSubmitBtn) {
        emailSubmitBtn.addEventListener('click', function() {
            // A terminal page sends nothing. The form is hidden by the state
            // attribute, but hiding is a rule of the stylesheet and this is the
            // handler's own guard: a click that still reaches it — a keyboard
            // activation racing the status, an integrator's custom CSS — must
            // not start a transaction the server has already closed.
            if (isTerminal) return;

            var emailInput = document.getElementById("veriqa-email-input");
            var msgEl = document.getElementById("veriqa-email-msg");
            var emailVal = emailInput ? emailInput.value.trim() : '';
            var sid = this.getAttribute('data-session-id') || '';

            if (!emailVal) return;

            emailSubmitBtn.disabled = true;
            msgEl.style.display = 'none';
            msgEl.className = 'veriqa-email-msg';

            fetch(withBase(config.emailStartUrl), {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email: emailVal, session_id: sid })
            })
            .then(function(resp) {
                if (!resp.ok) {
                    var ct = resp.headers.get('content-type') || '';
                    if (ct.indexOf('application/json') !== -1) {
                        return resp.json().then(function(d) { return { _err: true, data: d }; });
                    }
                    return resp.text().then(function(t) { return { _err: true, data: { error: t || config.texts.emailError } }; });
                }
                return resp.json();
            })
            .then(function(data) {
                if (data._err) { data = data.data; }
                if (data.success) {
                    msgEl.textContent = config.texts.emailSent
                        .replace('{0}', data.maskedEmail || emailVal);
                    msgEl.className = 'veriqa-email-msg';
                    msgEl.style.display = 'block';
                    emailSubmitBtn.disabled = true;
                    if (emailInput) emailInput.disabled = true;
                } else {
                    msgEl.textContent = data.error || config.texts.emailError;
                    msgEl.className = 'veriqa-email-msg error';
                    msgEl.style.display = 'block';
                    emailSubmitBtn.disabled = false;
                }
            })
            .catch(function() {
                msgEl.textContent = config.texts.emailError;
                msgEl.className = 'veriqa-email-msg error';
                msgEl.style.display = 'block';
                emailSubmitBtn.disabled = false;
            });
        });
    }

    // Toggling the Push/Pull sections of the Email panel
    var pushSection = document.getElementById("veriqa-push-section");
    var pullSection = document.getElementById("veriqa-pull-section");
    var showPullBtn = document.getElementById("veriqa-show-pull");
    var showPushBtn = document.getElementById("veriqa-show-push");
    if (showPullBtn && pushSection && pullSection) {
        showPullBtn.addEventListener('click', function() {
            pushSection.style.display = 'none';
            pullSection.style.display = 'block';
        });
    }
    if (showPushBtn && pushSection && pullSection) {
        showPushBtn.addEventListener('click', function() {
            pullSection.style.display = 'none';
            pushSection.style.display = 'block';
        });
    }

    // Fallback-polling configuration. The polled URL is fetched
    // here, so it is based here; the callback is a navigation target and stays
    // root-relative — navigateTo bases it. withBase is not idempotent: basing
    // twice would send the browser to /demo/demo/... and a 404.
    var STATUS_URL = withBase(config.statusUrl);
    var CALLBACK_URL = config.callbackUrl;
    var POLL_INTERVAL_MS = config.pollIntervalMs;

    // Whether this page continues on the browser callback of the sign-in path.
    // False for a transaction that has no such callback: its outcome is shown here.
    var NAVIGATES_AWAY = config.navigatesAway;

    // The destination of a page without that callback — the core page asking the
    // confirming question. Empty when there is none.
    var CONFIRM_URL = config.confirmUrl;

    // Countdown. It is started HERE, below the polling
    // configuration, and not where it is declared: a page whose remainder is
    // already zero verifies with the status URL on its very first tick, and a
    // start placed above these declarations would read them unassigned.
    //
    // The deadline is a MOMENT, so what is left of it is kept in milliseconds.
    // The figure on screen is whole seconds and stays so, but the decision to
    // ask the server is not: a remainder counted in whole seconds is zero for
    // the whole last second, and a tick almost never lands on the deadline
    // exactly (the first remainder is a fraction, and every timeout drifts
    // late). A page deciding by the figure would ask about an expiry that has
    // not happened, be told "alive, 0.4 s left", replan to the same instant and
    // ask again — a burst of status requests at the speed of the round trip, at
    // the end of every ordinary transaction.
    function updateCountdown() {
        if (isTerminal) return;
        var left = deadline - Date.now();
        var diff = Math.max(0, Math.floor(left / 1000));
        var min = Math.floor(diff / 60);
        var sec = diff % 60;
        countdownEl.textContent = (min > 0 ? min + ":" : "") + (sec < 10 ? "0" : "") + sec;
        // The warning is a property of the CURRENT remainder, so it is written
        // in both directions: a deadline replanned back above the threshold by
        // the server's own remainder must lose the error colouring it took.
        countdownEl.className = diff <= 30
            ? "veriqa-countdown warning"
            : "veriqa-countdown";
        // Zero on the clock is a QUESTION for the server, not an answer: the
        // countdown is rendered first, so the page shows the zero it reached
        // while the check is in flight instead of blanking or lying.
        if (left <= 0) {
            verifyExpiry();
            return;
        }
        // Never past the deadline, and never more than a second from the next
        // figure to draw — whichever of the two comes first.
        setTimeout(updateCountdown, Math.min(1000, left));
    }

    // Whether a verification is already in flight. Without it the poll loop and
    // the countdown could each start one, and two answers would reschedule the
    // same deadline twice.
    var verifyPending = false;

    // The answer that is NOT one: nothing was said about the transaction — the
    // surface refused (a rate limit, a fault of its own or of a proxy in front
    // of it), or what came back cannot be read as its answer. A marker rather
    // than a value, told apart by identity, so that no shape a real response
    // could take is mistaken for it.
    var VERIFY_UNAVAILABLE = {};

    // The check the countdown makes when it runs out. Expiry is a fact of the
    // SERVER, and until this call it is only a fact of the local clock — which is
    // precisely what used to end a live transaction early and, worse, stop the
    // page from listening for the confirmation that was still coming.
    //
    // The order of the questions is normative: the STATE is read before the
    // remaining TTL. A transaction can be terminal with no time left for reasons
    // that are not expiry at all, so a page reading the remainder first would
    // announce the wrong ending. Only two answers end the page: the transaction
    // is gone (404), or the server itself names the state Expired.
    //
    // Past the deadline the surface gives two different answers, and the
    // difference is the one this page lives on. A transaction the deadline still
    // decides — nobody has confirmed it yet — is answered 404 until the sweep
    // records the expiry, and the page ends on that. A transaction already
    // confirmed is not: its deadline ends nothing (what it waits on now is
    // finalization, a wait the TTL does not govern and whose length is the
    // server's business, not the page's), the surface reports the state it is
    // in, and the page lands in the branch below for "alive with nothing left"
    // and keeps asking until the outcome is named — which is precisely the
    // sign-in it is waiting for.
    function verifyExpiry() {
        if (isTerminal || isNavigating || verifyPending) return;
        verifyPending = true;
        fetch(STATUS_URL, { headers: { "Accept": "application/json" } })
            .then(function(resp) {
                // Gone — the one answer that IS the expiry.
                if (resp.status === 404) return null;
                // Any other refusal says nothing ABOUT THE TRANSACTION: a rate
                // limit or a fault means the check did not happen, not that the
                // sign-in ended. Ending here would hand the shared bucket of the
                // status surface a verdict over live pages — and the page's own
                // confirmation, still on its way, would then be dropped by the
                // terminal flag. The check is repeated instead.
                if (!resp.ok) return VERIFY_UNAVAILABLE;
                // Nor does a 200 whose body is not the answer: an intercepting
                // proxy — the very one named above — answers OK with a page of
                // its own. The transport is alive and the check simply did not
                // happen, so it is repeated rather than ended on.
                return resp.json().catch(function() { return VERIFY_UNAVAILABLE; });
            })
            .then(function(data) {
                verifyPending = false;
                if (isTerminal || isNavigating) return;
                if (data === VERIFY_UNAVAILABLE) {
                    setTimeout(verifyExpiry, POLL_INTERVAL_MS);
                    return;
                }
                if (!data || !data.state) { handleExpired(); return; }
                if (data.state === STATUS_EXPIRED) { handleExpired(); return; }
                // Terminal for another reason — the outcome is shown by the one
                // handler that shows outcomes, expiry wording included nowhere.
                if (data.state === STATUS_COMPLETED || data.state === STATUS_FAILED) {
                    handleStatus(data.state, CALLBACK_URL, data.reason_code);
                    return;
                }
                // Alive, and the server says how much longer: the local deadline
                // was early (a browser clock running fast, a suspended tab), so
                // it is replanned from the server's own remainder.
                if (data.ttl_seconds > 0) {
                    deadline = Date.now() + data.ttl_seconds * 1000;
                    updateCountdown();
                    return;
                }
                // Alive with nothing left to report. Two arrivals here, and
                // neither is an ending: the deadline has not passed on the
                // server and the remainder is merely under the tenth of a
                // second the response rounds to, or it has passed on a
                // transaction it does not end — a confirmation awaiting
                // finalization. Either way the page keeps the zero on screen,
                // leaves the status line alone and asks again — naming the end
                // is the server's part.
                setTimeout(verifyExpiry, POLL_INTERVAL_MS);
            })
            .catch(function() {
                // An unreachable server, not a refusing one: there is no
                // transport left to ask again over, so the page ends as expired
                // without the server's word.
                verifyPending = false;
                handleExpired();
            });
    }
    updateCountdown();

    function handleExpired() {
        isTerminal = true;
        // The state of the page, written where every path into expiry passes:
        // the verification above, the SignalR status and the 404 of the poll
        // loop. The stylesheet drops every way IN from the page on this one
        // attribute and shows the way OUT — one decision, one place.
        document.documentElement.setAttribute(
            config.pageStateAttribute, config.expiredPageState);
        statusEl.textContent = msgExpired;
        statusEl.className = "veriqa-status error";
        countdownEl.style.display = "none";
        stopWaitingIfStaying();
    }

    // Where a status may take this page. In the mode with the callback — wherever
    // the status brought it. Without it — the confirming question and NOTHING
    // else: a target arriving in a message is ignored, which closes both delivery
    // paths of a target (the field of a status message and the deterministic
    // callback of the polling fallback) with one decision.
    function targetFor(status, redirectUrl) {
        if (NAVIGATES_AWAY) return redirectUrl;
        return status === STATUS_AWAITING_WEB ? CONFIRM_URL : "";
    }

    // The only place that takes the browser off this page. Guarded, so the first
    // caller wins and every later tick/message is a no-op; a page with no target
    // for this status stops here as well.
    // Answers whether the navigation was TAKEN, so that a caller can tell the user
    // "hold on, we are moving you" only when the page really is about to move.
    function navigateTo(url, delayMs) {
        if (isNavigating || !url) return false;
        isNavigating = true;
        setTimeout(function() {
            // Every navigation target reaching this point is root-relative —
            // a URL from a SignalR message (built by the background handler
            // without HttpContext) and the deterministic callback used by
            // polling alike — so the path base is applied here, once.
            window.location.href = withBase(url);
        }, delayMs);
        return true;
    }

    // A page that never leaves shows its terminal state as the last screen, so
    // nothing on it may keep promising movement: the spinner turned on by an
    // earlier, non-terminal status is put out here. On a page that does navigate
    // the terminal screen lives only for the moment before the callback takes
    // over, and its behaviour is left exactly as it is.
    function stopWaitingIfStaying() {
        if (!NAVIGATES_AWAY) { spinnerEl.style.display = "none"; }
    }

    // Applying the transaction lifecycle status to the UI.
    // Shared function: called both from the SignalR notification and from fallback
    // polling. The reason code is the third argument because a Failed status alone
    // does not say whether the user refused; both delivery paths already carry it.
    function handleStatus(status, redirectUrl, reasonCode) {
        if (isTerminal) return;

        if (status === STATUS_CONFIRMED) {
            statusEl.textContent = msgConfirmed;
            statusEl.className = "veriqa-status confirmed";
            spinnerEl.style.display = "block";
        } else if (status === STATUS_AWAITING_WEB) {
            // The core took the confirming question onto its own page: leave for
            // it right away. The transaction is not terminal — the answer is
            // still ahead — so the countdown and the state are left alone; the
            // navigation guard is what stops the polling loop from repeating this.
            // The status line is not rewritten either: the user has confirmed
            // nothing yet, and the page they are about to see asks that question.
            // The spinner follows the navigation: a page that stays put keeps
            // waiting for the outcome instead of announcing a move it will not
            // make, and the status tracking goes on — the answer is given
            // elsewhere and comes back here.
            spinnerEl.style.display = navigateTo(targetFor(status, redirectUrl), 0) ? "block" : "none";
        } else if (status === STATUS_COMPLETED) {
            isTerminal = true;
            statusEl.textContent = msgSuccess;
            statusEl.className = "veriqa-status success";
            countdownEl.style.display = "none";
            // Terminal and final: the spinner covers the pause before the
            // navigation and has nothing to cover without one — on a page that
            // stays, the outcome is the last thing shown, and it is shown still.
            spinnerEl.style.display = navigateTo(targetFor(status, redirectUrl), 500) ? "block" : "none";
        } else if (status === STATUS_EXPIRED) {
            handleExpired();
        } else if (status === STATUS_FAILED) {
            isTerminal = true;
            // A refusal is told from a fault by the reason and by nothing else: a
            // failure that names no reason is not the user's refusal to state.
            statusEl.textContent = (reasonCode === REASON_DECLINED) ? msgDeclined : msgError;
            statusEl.className = "veriqa-status error";
            countdownEl.style.display = "none";
            stopWaitingIfStaying();
        }
    }

    // Status fallback polling: activated when SignalR is
    // unavailable — the library failed to load, negotiate/start did not succeed,
    // or the connection dropped for good. Polls the REST endpoint
    // without JS dependencies; this reliability tier is below the SignalR transport.
    //
    // The loop that is running RIGHT NOW, held by identity — and the only
    // answer this page has to "is anything still tracking the transaction".
    // A flag raised at the start and never lowered answers a different
    // question — "was polling ever started" — and the two answers part company
    // the moment the loop ends itself, which it does whenever the browser is
    // leaving this page (see pollStatus below). A page brought back from the
    // back/forward cache would then be refused its restart by a flag describing
    // a loop that is no longer running, and would sit out the TTL with a single
    // read behind it — the ending no transport may leave the user with.
    // An identity and not a counter, because a restart may happen while the
    // previous loop's read is still in flight: the continuation of that read
    // belongs to a loop which is no longer the live one, and it must plan no
    // tick of its own — or the page would poll twice over for the rest of its
    // life. Compared by reference, so no two loops can ever be taken for one.
    var livePollLoop = null;
    function startPolling() {
        if (livePollLoop || isTerminal || isNavigating) return;
        livePollLoop = {};
        pollStatus(livePollLoop);
    }
    // ONE read of the status surface, applied to the page. It has two callers,
    // and they differ only in what happens AFTER the answer: the loop below
    // plans the next read, the reconciliation further down plans nothing. What
    // the read itself is — which URL, that a 404 is the expiry, and which of
    // the two response fields carries the status — stays written once.
    function readStatusOnce() {
        if (isTerminal || isNavigating) return Promise.resolve();
        return fetch(STATUS_URL, { headers: { "Accept": "application/json" } })
            .then(function(resp) {
                // 404 — transaction not found/expired: treat as expired
                if (resp.status === 404) { handleExpired(); return null; }
                if (!resp.ok) { return null; }
                return resp.json();
            })
            .then(function(data) {
                // The polling endpoint has no redirectUrl — on Completed we use
                // the deterministic callback built on the server from session_id.
                // "The core is waiting for the answer on its page" is not a
                // state-machine state, so it arrives as its own field rather
                // than inside "state".
                if (data && data.awaiting_web_confirmation) {
                    handleStatus(STATUS_AWAITING_WEB, CALLBACK_URL, data.reason_code);
                } else if (data && data.state) {
                    handleStatus(data.state, CALLBACK_URL, data.reason_code);
                }
            });
    }
    // The loop. It carries the identity it was started with and checks it at
    // every point where it could go on: a loop that is no longer the live one
    // stops without touching anything, and the loop that IS the live one
    // releases the identity wherever it decides not to continue. Stopping and
    // saying so are the same act here — that is the whole of the invariant this
    // page needs, because the page that comes back has to be able to ask
    // whether anything is still tracking it and be told the truth.
    function pollStatus(loop) {
        if (loop !== livePollLoop) return;
        if (isTerminal || isNavigating) { livePollLoop = null; return; }
        // Both outcomes plan the next tick: a network error or a rate limit says
        // nothing about the transaction, so it must not terminate the loop.
        function planNext() {
            if (loop !== livePollLoop) return;
            if (isTerminal || isNavigating) { livePollLoop = null; return; }
            setTimeout(function() { pollStatus(loop); }, POLL_INTERVAL_MS);
        }
        readStatusOnce().then(planNext, planNext);
    }

    // Coming back to the page. A phone that froze the tab while the user answered
    // in the messenger can hand it back with a socket that died silently: neither
    // onreconnected nor onclose fires, so nothing resubscribes, and the status
    // broadcast meanwhile was addressed to a group this page had already left.
    // Asking on return is what ends that wait — the server cannot know the
    // connection is gone, and the browser never said so (no transport may leave
    // the user waiting out the TTL).
    // Registered for both transports and above the SignalR branch below, which
    // returns: the read is the same one the fallback loop makes, and a frozen tab
    // throttles that loop's timers just as thoroughly.
    var reconcilePending = false;
    function reconcileStatus() {
        if (isTerminal || isNavigating || reconcilePending) return;
        reconcilePending = true;
        function done() { reconcilePending = false; }
        readStatusOnce().then(done, done);
    }

    // The transport, not only the status. One read tells the page where the
    // transaction stands right now; it says nothing about how the NEXT change
    // would reach it. A transport that delivers nothing at all — a connection
    // the client itself reports as disconnected, or a fallback loop that has
    // stopped — would leave the page with one answer and no way to hear the
    // following one.
    // What comes back is the highest tier still available, which is not always
    // the tier the page was on: the checks below are the conditions under which
    // a tier has to be raised, asked in order. A page that never had a hub gets
    // the fallback loop back; a socket the client reports as Disconnected is
    // revived instead, and the page drops to polling only if that revival
    // fails — so a page that had already degraded through onclose or a failed
    // start is offered the socket again before the loop. Only on return, and
    // only when the page is not being tracked: no periodic probing is
    // introduced, a running fallback loop is left to its schedule, and a
    // connection that is connected, connecting or reconnecting is left to its
    // own machinery. The silent death — the socket the client still believes is
    // open — is not detectable here at all; it is covered by the read above
    // and, beyond it, by SignalR's own keep-alive timeout, which starts running
    // again with the thawed tab and ends in reconnect or in onclose, both of
    // which this page already handles.
    function resumeTransport() {
        if (isTerminal || isNavigating) return;
        // Nothing to revive: the fallback loop is running and already owns the
        // tracking. This is also what keeps a live transport from being polled
        // alongside — reaching the tiers below means the page is NOT being
        // tracked at this moment.
        if (livePollLoop) return;
        // No hub was ever built (the library failed to load), so the fallback
        // loop is the only tier this page has — and it is not running, or the
        // line above would have returned. This is the page restored onto a loop
        // that stopped while the browser was leaving; it gets its tracking back
        // here, and it is the same tier it was on, not a new one.
        if (!connection) { startPolling(); return; }
        var hubStates = window.signalR.HubConnectionState;
        if (!hubStates || connection.state !== hubStates.Disconnected) return;
        connection.start()
            .then(function() {
                // Back on the wire and back in the group: the subscription is
                // what makes the connection useful, and the hub answers it with
                // the transaction's current status.
                return connection.invoke(config.hub.joinMethod, sessionId);
            })
            .catch(function() {
                // The hub is not coming back for this page — the tier below it
                // keeps the transaction tracked.
                startPolling();
            });
    }

    document.addEventListener("visibilitychange", function() {
        if (document.visibilityState !== "visible") return;
        reconcileStatus();
        resumeTransport();
    });
    // Restored from the back/forward cache: the page did not reload, so nothing
    // else on it runs again — the listeners are the state it woke up with, and
    // so is the navigation latch left standing by the very navigation that put
    // this page into the cache.
    window.addEventListener("pageshow", function(event) {
        if (!event.persisted) return;
        // The navigation latch counts navigations within ONE life of the page: it
        // exists so that a repeat message or poll tick cannot reassign
        // window.location WHILE the browser is already leaving. A restore from
        // the back/forward cache is past that point — the navigation it guarded
        // completed, the user undid it, and the page is on screen again with
        // nothing in flight: a life of its own, in which no navigation has been
        // started yet. Left standing, the latch does not merely forbid a second
        // navigation: it silences the reconciliation below, the expiry check and
        // the polling fallback alike, and the restored page would sit on a frozen
        // countdown until the TTL ran out — the one ending no transport may lead to.
        isNavigating = false;
        reconcileStatus();
        resumeTransport();
    });

    // SignalR connection. If the client library failed
    // to load → immediately and transparently degrade to REST polling
    // (the page keeps tracking the transaction instead of going silent).
    if (!window.signalR || !window.signalR.HubConnectionBuilder) {
        startPolling();
        return;
    }

    var connection = new window.signalR.HubConnectionBuilder()
        .withUrl(withBase(config.hub.path))
        .withAutomaticReconnect()
        .build();

    connection.on(config.hub.statusMethod, function(message) {
        handleStatus(message.status, message.redirectUrl, message.errorCode);
    });

    // Email-specific intermediate statuses.
    // An additive channel: does not affect the lifecycle handling above.
    connection.on(config.hub.channelStatusMethod, function(message) {
        if (isTerminal) return;
        if (!message || message.channelType !== CHANNEL_EMAIL) return;
        var text = EMAIL_STATUS_TEXTS[message.channelStatus];
        if (text) {
            statusEl.textContent = text;
            statusEl.className = "veriqa-status confirmed";
        }
    });

    // Final connection drop (after auto-reconnect is exhausted) —
    // degrade to polling instead of a dead end.
    connection.onclose(function() {
        if (!isTerminal) { startPolling(); }
    });

    connection.start()
        .then(function() {
            return connection.invoke(config.hub.joinMethod, sessionId);
        })
        .catch(function(err) {
            console.error("SignalR error:", err);
            // start/negotiate did not succeed → degrade to REST polling
            startPolling();
        });

    // Heartbeat / reconnect: after an automatic reconnect the connection is back
    // on the wire but out of the transaction group, so it joins the group again
    connection.onreconnected(function() {
        connection.invoke(config.hub.joinMethod, sessionId).catch(function(){});
    });
})();
