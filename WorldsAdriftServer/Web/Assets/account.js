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

  var builders = document.querySelectorAll('form.builder');
  for (var b = 0; b < builders.length; b++) { wireEmblem(builders[b]); }

  wireConfirm(document);
  wireRanks(document);
})();
