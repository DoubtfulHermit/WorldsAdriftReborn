(function () {
  'use strict';

  // THE ACCOUNT PORTAL'S OWN CLOSURE, and it must stay its own.
  //
  // The operator console and the public map compose their JavaScript out of
  // Web/Assets fragments into ONE shared closure - see WebAssets.Script - so
  // every top-level name in those files is in the same scope as every other.
  // This file is NOT one of those fragments: it is loaded only by AccountPage,
  // in a <script> of its own, so nothing here can shadow a console name and
  // nothing there can shadow one of these. That is worth stating rather than
  // leaving to be noticed, because the failure mode is silent - a duplicated
  // top-level name in the shared closure simply replaces the earlier one, and
  // both features go on rendering with the wrong function.

  // ------------------------------------------------------------ crest preview

  // The ONLY thing this script draws is a string. The picture always comes from
  // /alliance-emblem/preview.png, which is the same renderer the game hits - so
  // there is no second implementation here to drift from the server's.
  var EMBLEM_FIELDS = ['shape', 'division', 'charge', 'field', 'detail', 'chargeColour'];

  // Written by the server rather than typed here, so the page cannot go on
  // building codes in a version the parser has moved past.
  var EMBLEM_VERSION = '{{emblemVersion}}';

  function emblemCode(form) {
    var parts = [EMBLEM_VERSION];
    for (var i = 0; i < EMBLEM_FIELDS.length; i++) {
      var el = form.elements[EMBLEM_FIELDS[i]];
      if (!el) { return null; }
      parts.push(el.value);
    }
    return parts.join('-');
  }

  function wireEmblem(form) {
    var img = form.querySelector('.preview');
    if (!img) { return; }
    var pending = 0;

    function refresh() {
      var code = emblemCode(form);
      if (code === null) { return; }
      var next = '/alliance-emblem/preview.png?e=' + encodeURIComponent(code);
      if (img.getAttribute('src') !== next) { img.setAttribute('src', next); }

      // The download link follows the preview, so what a leader saves is what
      // they are looking at rather than what they had when the page loaded.
      var vector = form.querySelector('a.vector');
      if (vector) {
        vector.setAttribute('href',
          '/alliance-emblem/preview.svg?e=' + encodeURIComponent(code));
      }
    }

    form.addEventListener('change', function () {
      // Debounced: dragging across the swatches fires a change per colour, and
      // every one of those is a render on the server.
      window.clearTimeout(pending);
      pending = window.setTimeout(refresh, 120);
    });
  }

  // ------------------------------------------------------- destructive asks

  // A confirm() on the two posts that cannot be undone from this page: throwing
  // somebody out of an alliance and turning an applicant away. It is a courtesy
  // and NOT a control - the server refuses an unpermitted boot whether or not
  // this dialog ran, and a browser with script off simply gets no dialog.
  function wireConfirm(root) {
    var forms = root.querySelectorAll('form[data-confirm]');
    for (var i = 0; i < forms.length; i++) {
      forms[i].addEventListener('submit', function (e) {
        if (!window.confirm(this.getAttribute('data-confirm'))) {
          e.preventDefault();
        }
      });
    }
  }

  // ------------------------------------------------------------ rank selects

  // The rank dropdown posts on change, so a rank move is one gesture rather
  // than a select plus a button nobody presses. The button stays in the markup
  // for the no-script case; it is only hidden once we know we can submit.
  function wireRanks(root) {
    var selects = root.querySelectorAll('select.rank');
    for (var i = 0; i < selects.length; i++) {
      var form = selects[i].form;
      if (!form) { continue; }

      var apply = form.querySelector('button.apply');
      if (apply) { apply.hidden = true; }

      selects[i].addEventListener('change', function () {
        if (this.form) { this.form.submit(); }
      });
    }
  }

  // -------------------------------------------------------------- the rail

  // On a phone the tab strip is one scrolling row rather than two wrapped ones
  // (see the breakpoint in account.css), and a row you can swipe can be a row
  // that opens scrolled away from the tab you are actually on. This puts the
  // current tab in the middle of it.
  //
  // `block: 'nearest'` matters: without it the browser is free to scroll the
  // PAGE vertically to bring the strip into view, and the portal would open
  // having already jumped past its own heading.
  function portalCentreCurrentTab(root) {
    var current = root.querySelector('nav.tabs a.on');
    if (!current || !current.scrollIntoView) { return; }

    var rail = current.parentNode;
    if (!rail || rail.scrollWidth <= rail.clientWidth) { return; }

    try {
      current.scrollIntoView({ block: 'nearest', inline: 'center' });
    } catch (e) {
      // Older browsers take a boolean and would scroll the page to the top of
      // the element. Not centring is a better outcome than that.
    }
  }

  // ------------------------------------------------------------------ busy

  // Every control on this portal posts and is answered with a redirect, so
  // there is a real wait between the click and the next page in which the only
  // honest feedback is "nothing has happened yet". This marks the form, and
  // account.css turns that into a spinner on its button.
  //
  // IT IS A MARK, NOT A GATE. Nothing is disabled and nothing is prevented -
  // the CSS drops pointer-events on the button so a second click cannot land,
  // but the submit that is already in flight is untouched, and a browser with
  // script off simply gets no spinner. Disabling the button outright would be
  // the version of this that can lose a submit.
  // THE EMBLEM EDITOR'S FORM IS NOT ONE OF THESE, and that is not squeamishness
  // about another file - it is a bug this would otherwise have shipped. The
  // editor's footer plank does not post: its own submit handler cancels the
  // event and opens the save sheet instead. This script is emitted BEFORE the
  // editor's, so its listener registers first and would see defaultPrevented
  // still false - marking the form busy for a submit that never happens, which
  // with pointer-events off on the button means the plank spins forever and the
  // emblem can never be saved again. The editor reports its own state; leave it
  // to it.
  function portalWireBusy(root) {
    var forms = root.querySelectorAll('form');
    for (var i = 0; i < forms.length; i++) {
      if (forms[i].closest && forms[i].closest('.editor')) { continue; }

      forms[i].addEventListener('submit', function (e) {
        // wireConfirm runs first and cancels the submit when a player answers
        // no. Marking the form busy for a submit that is not happening would
        // leave a spinner turning forever on a page that never navigates.
        if (e.defaultPrevented) { return; }
        this.setAttribute('data-busy', '');
      });
    }
  }

  var builders = document.querySelectorAll('form.builder');
  for (var b = 0; b < builders.length; b++) { wireEmblem(builders[b]); }

  wireConfirm(document);
  wireRanks(document);
  portalWireBusy(document);
  portalCentreCurrentTab(document);
})();
