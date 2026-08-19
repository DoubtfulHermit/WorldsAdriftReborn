(function () {
  'use strict';

  // THE LAYERED EMBLEM EDITOR.
  //
  // ITS OWN CLOSURE, like account.js beside it and for the same reason: the
  // operator console and the public map compose their fragments into ONE shared
  // closure, where a duplicated top-level name silently replaces an earlier one.
  // Nothing here is one of those fragments, and every name in here is prefixed
  // `emb` anyway, so neither this file nor that closure can shadow the other even
  // if somebody later concatenates them.
  //
  // WHAT DRAWS WHAT. The picture in the canvas while you are dragging is drawn
  // HERE, as SVG. That is unavoidable - a 256-pixel PNG round trip per mouse move
  // is not a preview - and it is the one place this feature could drift from the
  // server. Four things stop it, and all four are load-bearing:
  //
  //   1. the SHAPES are not described here. They arrive from the catalogue as the
  //      exact `d` attribute the server's own vector export writes, off the same
  //      path objects the rasteriser samples;
  //   2. the PALETTE and every unit, limit and alphabet below is stamped in by the
  //      server, not typed here;
  //   3. the MIRROR region is the transform, the markup and the code, built from
  //      integers only - no floating-point formatting anywhere - and a test runs
  //      THIS FILE as the server serves it, in a real JavaScript engine, and
  //      asserts it produces byte-identical strings to the C#;
  //   4. and the server's own PNG is swapped over the top of the canvas a moment
  //      after you stop moving. If the two ever disagreed you would see the emblem
  //      change in front of you, rather than only in game.

  // ==== EMBLEM LAYER MIRROR BEGIN ====
  //
  // Everything between these markers is the browser's half of the parity
  // contract, and it is extracted verbatim by EmblemLayerMirrorTests. It must
  // stay pure: no DOM, no fetch, no state. A layer is
  // {o, x, y, s, r, c, a, fx, fy, lk} - object, centre, size, rotation, colour,
  // opacity, the two flips and the lock - and every one of those is an INTEGER or
  // a boolean, because a decimal is the thing that drifts.

  var embLimits = {{emblemLimits}};
  var embPalette = {{emblemPalette}};

  // A whole number of thousandths as a decimal, built with no floating point at
  // all: sign, integer part, dot, three padded digits. This function IS the
  // agreement with EmblemLayer.Thousandths.
  function embThousandths(value) {
    var sign = '';
    if (value < 0) { sign = '-'; value = -value; }

    var whole = Math.floor(value / embLimits.unit);
    var fraction = String(value % embLimits.unit);
    while (fraction.length < 3) { fraction = '0' + fraction; }

    return sign + whole + '.' + fraction;
  }

  // translate, then turn, then scale - and SVG applies a transform list right to
  // left to the geometry, so the shape is scaled (the flip being the sign of the
  // scale), then turned, then moved. Any other order puts a rotated layer
  // somewhere else entirely, and the server's rasteriser undoes exactly this.
  function embTransform(layer) {
    var sx = layer.fx ? -layer.s : layer.s;
    var sy = layer.fy ? -layer.s : layer.s;

    return 'translate(' + layer.x + ' ' + layer.y + ') rotate(' + layer.r
      + ') scale(' + embThousandths(sx) + ' ' + embThousandths(sy) + ')';
  }

  function embLayerMarkup(layer, pathData) {
    return '<g transform="' + embTransform(layer) + '"><path fill="'
      + embPalette[layer.c].h + '" fill-opacity="'
      + embThousandths(layer.a * embLimits.opacityUnit)
      + '" d="' + pathData + '"/></g>\n';
  }

  function embFlags(layer) {
    return (layer.fx ? 1 : 0) | (layer.fy ? 2 : 0) | (layer.lk ? 4 : 0);
  }

  function embChar(value) { return embLimits.alphabet.charAt(value & 63); }

  function embPair(value) { return embChar((value >> 6) & 63) + embChar(value & 63); }

  function embEncode(layers) {
    var code = embLimits.version + '-';

    for (var i = 0; i < layers.length; i++) {
      var layer = layers[i];
      code += embPair(layer.o)
        + embPair(layer.x + embLimits.offsetBias)
        + embPair(layer.y + embLimits.offsetBias)
        + embPair(layer.s)
        + embPair(layer.r)
        + embChar(layer.c)
        + embChar(layer.a)
        + embChar(embFlags(layer));
    }

    return code;
  }

  // Whether a layer is one the server would accept. The object index is only
  // checked for sign: how many objects the catalogue holds is the server's
  // business, and this file has not necessarily loaded it yet.
  function embValid(layer) {
    return layer.o >= 0
      && layer.x >= -embLimits.maxOffset && layer.x <= embLimits.maxOffset
      && layer.y >= -embLimits.maxOffset && layer.y <= embLimits.maxOffset
      && layer.s >= embLimits.minSize && layer.s <= embLimits.maxSize
      && layer.r >= 0 && layer.r < embLimits.rotationSteps
      && layer.c >= 0 && layer.c < embPalette.length
      && layer.a >= 0 && layer.a <= embLimits.opacitySteps;
  }

  // Total: every rejection returns null and nothing here throws on any input.
  function embDecode(code) {
    if (typeof code !== 'string') { return null; }

    var head = embLimits.version + '-';
    if (code.slice(0, head.length) !== head) { return null; }

    var body = code.slice(head.length);
    if (body.length % embLimits.codeWidth !== 0) { return null; }

    var count = body.length / embLimits.codeWidth;
    if (count > embLimits.maxLayers) { return null; }

    var layers = [];

    for (var i = 0; i < count; i++) {
      var at = i * embLimits.codeWidth;
      var digits = [];

      for (var j = 0; j < embLimits.codeWidth; j++) {
        var index = embLimits.alphabet.indexOf(body.charAt(at + j));
        if (index < 0) { return null; }
        digits.push(index);
      }

      // An unknown flag bit is a code from a vocabulary this build does not have.
      // Refused rather than masked, because masking draws a layer that is missing
      // whatever the bit meant.
      if (digits[12] > 7) { return null; }

      var layer = {
        o: digits[0] * 64 + digits[1],
        x: digits[2] * 64 + digits[3] - embLimits.offsetBias,
        y: digits[4] * 64 + digits[5] - embLimits.offsetBias,
        s: digits[6] * 64 + digits[7],
        r: digits[8] * 64 + digits[9],
        c: digits[10],
        a: digits[11],
        fx: (digits[12] & 1) !== 0,
        fy: (digits[12] & 2) !== 0,
        lk: (digits[12] & 4) !== 0
      };

      if (!embValid(layer)) { return null; }

      layers.push(layer);
    }

    return layers;
  }
  // ==== EMBLEM LAYER MIRROR END ====

  var EMB_CATALOGUE_URL = '{{emblemCatalogueUrl}}';
  var EMB_ROUTE = '{{emblemRoute}}';

  // How long after the last change the server's own render is fetched. Long
  // enough that dragging does not queue a render per frame, short enough that it
  // has landed before anybody reaches for Save.
  var EMB_SETTLE_MS = 400;

  var EMB_ICONS = {
    clone: 'M3 3h8v8H3z M5 1h8v8',
    del: 'M2 2l10 10 M12 2L2 12',
    lock: 'M3 6h8v7H3z M5 6V4a2 2 0 0 1 4 0v2'
  };

  // ------------------------------------------------------------- the catalogue

  var embCatalogue = null;
  var embCataloguePending = null;

  function embLoadCatalogue() {
    if (embCatalogue) { return Promise.resolve(embCatalogue); }
    if (embCataloguePending) { return embCataloguePending; }

    embCataloguePending = fetch(EMB_CATALOGUE_URL, { credentials: 'same-origin' })
      .then(function (response) {
        if (!response.ok) { throw new Error('catalogue ' + response.status); }
        return response.json();
      })
      .then(function (data) {
        embCatalogue = data.objects || [];
        return embCatalogue;
      });

    return embCataloguePending;
  }

  // ------------------------------------------------------------------ helpers

  function embFind(root, selector) { return root.querySelector(selector); }

  function embAll(root, selector) {
    return Array.prototype.slice.call(root.querySelectorAll(selector));
  }

  function embClamp(value, low, high) {
    return value < low ? low : (value > high ? high : value);
  }

  function embEscape(text) {
    return String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function embIcon(name) {
    return '<svg viewBox="0 0 14 14" aria-hidden="true"><path d="' + EMB_ICONS[name] + '"/></svg>';
  }

  function embCopy(layer) {
    return {
      o: layer.o, x: layer.x, y: layer.y, s: layer.s, r: layer.r,
      c: layer.c, a: layer.a, fx: layer.fx, fy: layer.fy, lk: layer.lk
    };
  }

  // ------------------------------------------------------------- one editor

  function embEditor(form) {
    var stage = embFind(form, '[data-stage]');
    var live = embFind(form, '[data-live]');
    var served = embFind(form, '[data-served]');
    var overlay = embFind(form, '[data-overlay]');
    var codeBox = embFind(form, '[data-code]');
    var layerList = embFind(form, '[data-layers]');
    var objectGrid = embFind(form, '[data-objects]');
    var paletteBox = embFind(form, '[data-palette]');
    var opacity = embFind(form, '[data-opacity]');
    var opacityValue = embFind(form, '[data-opacity-value]');
    var counter = embFind(form, '[data-count]');
    var hint = embFind(form, '[data-hint]');
    var sheet = embFind(form, '[data-savesheet]');

    if (!stage || !live || !codeBox || !layerList) { return; }

    var baseline = codeBox.value;
    var baselinePicture = served ? served.getAttribute('src') : null;

    // AN ALLIANCE MAY BE WEARING A CREST THE OLD HERALDIC BUILDER MADE. Those are
    // still rendered, exactly as they always were, but they are not a stack of
    // layers and cannot be decoded into one - so the canvas opens EMPTY and,
    // crucially, nothing is written back over the design code or the picture until
    // the player actually places something. Otherwise merely opening this tab
    // would replace their emblem with a blank one the moment they pressed save.
    var layers = embDecode(baseline) || [];
    var legacy = layers.length === 0 && embDecode(baseline) === null;
    var active = layers.length > 0 ? layers.length - 1 : -1;
    var colour = layers.length > 0 ? layers[layers.length - 1].c : 0;
    var category = 'Shapes';
    var filter = '';
    var settle = 0;
    var drag = null;

    // ------------------------------------------------------------- the picture

    function pathOf(index) {
      if (!embCatalogue || index < 0 || index >= embCatalogue.length) { return null; }
      return embCatalogue[index].d;
    }

    function nameOf(index) {
      if (!embCatalogue || index < 0 || index >= embCatalogue.length) {
        return 'object ' + index;
      }
      return embCatalogue[index].n;
    }

    function drawLive() {
      if (!embCatalogue) { return; }

      var markup = '';

      for (var i = 0; i < layers.length; i++) {
        var data = pathOf(layers[i].o);
        if (data === null) { continue; }

        // The wrapper carries the index so a click anywhere on the shape finds
        // its layer; the inner group is the mirror's own markup, untouched.
        markup += '<g class="lyr" data-layer="' + i + '">'
          + embLayerMarkup(layers[i], data) + '</g>';
      }

      live.innerHTML = markup;

      // The selection goes in the layer ABOVE the server's render - see the note
      // in AccountEmblemEditor. It is measured after the shapes are in the
      // document, because it asks the browser for the outline's real extent.
      if (overlay) { overlay.innerHTML = selection(); }
    }

    // The dashed box and its two handles, drawn in the ACTIVE layer's rotated
    // frame but at its own scale - so the handles stay the same size on screen
    // however small or large the layer is, and still sit on its corners.
    function selection() {
      if (active < 0 || active >= layers.length) { return ''; }

      var layer = layers[active];
      var data = pathOf(layer.o);
      if (data === null) { return ''; }

      var box = embBounds(layer, data);
      if (!box) { return ''; }

      var stem = box.y0 - 170;

      // The box is drawn TWICE: a dark casing under a light dashed stroke. One
      // stroke of either colour disappears against half the palette, and the one
      // it disappears against is whichever the player has just picked.
      var rect = ' x="' + box.x0 + '" y="' + box.y0
        + '" width="' + (box.x1 - box.x0) + '" height="' + (box.y1 - box.y0) + '"/>';

      return '<g class="sel" transform="translate(' + layer.x + ' ' + layer.y
        + ') rotate(' + layer.r + ')">'
        + '<rect class="selcase"' + rect
        + '<rect class="selbox"' + rect
        + (layer.lk ? '' :
          '<line class="selstem" x1="0" y1="' + box.y0 + '" x2="0" y2="' + stem + '"/>'
          + '<circle class="handle" data-handle="rotate" cx="0" cy="' + stem + '" r="60"/>'
          + '<circle class="handle" data-handle="scale" cx="' + box.x1 + '" cy="' + box.y1 + '" r="60"/>')
        + '</g>';
    }

    // The layer's bounds in its own rotated frame. Measured off a real path
    // element rather than computed, because the browser already knows the exact
    // extent of the outline and re-deriving it here would be a third description
    // of the same geometry.
    var ruler = null;

    function embBounds(layer, data) {
      // Re-made when the last render threw it away: drawLive replaces the whole
      // of the canvas's markup, so a ruler measured into it does not survive.
      if (!ruler || !ruler.isConnected) {
        ruler = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        ruler.setAttribute('class', 'ruler');
        live.appendChild(ruler);
      }

      ruler.setAttribute('d', data);

      var raw;
      try { raw = ruler.getBBox(); } catch (e) { return null; }
      if (!raw || raw.width === 0 && raw.height === 0) { return null; }

      var sx = (layer.fx ? -layer.s : layer.s) / embLimits.unit;
      var sy = (layer.fy ? -layer.s : layer.s) / embLimits.unit;

      var ax = raw.x * sx, bx = (raw.x + raw.width) * sx;
      var ay = raw.y * sy, by = (raw.y + raw.height) * sy;

      return {
        x0: Math.round(Math.min(ax, bx)), x1: Math.round(Math.max(ax, bx)),
        y0: Math.round(Math.min(ay, by)), y1: Math.round(Math.max(ay, by))
      };
    }

    // ------------------------------------------------------------ the server

    function refreshServed() {
      if (legacy && layers.length === 0) {
        // Untouched, and what is stored is not ours to rewrite.
        codeBox.value = baseline;
        if (served && baselinePicture) { served.setAttribute('src', baselinePicture); }
        stage.classList.add('settled');
        return;
      }

      var code = embEncode(layers);

      codeBox.value = code;

      var vector = embFind(form, '[data-savevector]');
      if (vector) { vector.setAttribute('href', EMB_ROUTE + '.svg?e=' + encodeURIComponent(code)); }

      window.clearTimeout(settle);
      settle = window.setTimeout(function () {
        var next = EMB_ROUTE + '.png?e=' + encodeURIComponent(code);

        // Loaded first, shown second, so the canvas never blinks empty while a
        // render is in flight.
        var probe = new Image();
        probe.onload = function () {
          if (served) {
            served.setAttribute('src', next);
            stage.classList.add('settled');
          }
          var savePreview = embFind(form, '[data-savepreview]');
          if (savePreview) { savePreview.setAttribute('src', next); }
        };
        probe.src = next;
      }, EMB_SETTLE_MS);

      stage.classList.remove('settled');
    }

    // ----------------------------------------------------------- the panels

    function drawLayers() {
      var markup = '';

      // Top layer FIRST, which is what a layers panel means - the row at the top
      // of the list is the thing in front.
      for (var i = layers.length - 1; i >= 0; i--) {
        var layer = layers[i];
        var data = pathOf(layer.o);

        markup += '<li class="lrow' + (i === active ? ' on' : '')
          + (layer.lk ? ' locked' : '') + '" data-row="' + i + '" draggable="true">'
          + '<span class="grip" aria-hidden="true">&#8942;&#8942;</span>'
          + '<span class="thumb">' + (data === null ? '' :
            '<svg viewBox="-1050 -1050 2100 2100" aria-hidden="true"><path fill="'
            + embPalette[layer.c].h + '" d="' + data + '"/></svg>') + '</span>'
          + '<span class="lname">' + embEscape(nameOf(layer.o)) + '</span>'
          + '<span class="lacts">'
          + '<button type="button" class="licon" data-act="clone" data-at="' + i
          + '" title="Clone this layer" aria-label="Clone this layer">' + embIcon('clone') + '</button>'
          + '<button type="button" class="licon" data-act="delete" data-at="' + i
          + '" title="Delete this layer" aria-label="Delete this layer"'
          + (layer.lk ? ' disabled' : '') + '>' + embIcon('del') + '</button>'
          + '<button type="button" class="licon' + (layer.lk ? ' on' : '')
          + '" data-act="lock" data-at="' + i
          + '" title="' + (layer.lk ? 'Unlock this layer' : 'Lock this layer')
          + '" aria-pressed="' + (layer.lk ? 'true' : 'false') + '">' + embIcon('lock') + '</button>'
          + '</span></li>';
      }

      layerList.innerHTML = markup;

      if (counter) { counter.textContent = layers.length + ' / ' + embLimits.maxLayers; }
    }

    function drawPalette() {
      if (!paletteBox || paletteBox.childNodes.length > 0) { return; }

      var markup = '';
      for (var i = 0; i < embPalette.length; i++) {
        markup += '<button type="button" class="sw" data-colour="' + i
          + '" style="--sw:' + embPalette[i].h + '" title="' + embEscape(embPalette[i].n)
          + '" aria-label="' + embEscape(embPalette[i].n) + '"></button>';
      }

      paletteBox.innerHTML = markup;
    }

    function drawObjects() {
      if (!objectGrid) { return; }

      if (!embCatalogue) {
        objectGrid.innerHTML = '<p class="waiting">Loading the object catalogue&hellip;</p>';
        return;
      }

      var needle = filter.trim().toLowerCase();
      var markup = '';
      var shown = 0;

      for (var i = 0; i < embCatalogue.length; i++) {
        var entry = embCatalogue[i];

        if (needle.length > 0) {
          if (entry.n.toLowerCase().indexOf(needle) < 0) { continue; }
        } else if (entry.c !== category) {
          continue;
        }

        shown++;
        markup += '<button type="button" class="obj" data-object="' + i
          + '" title="' + embEscape(entry.n) + '" aria-label="' + embEscape(entry.n) + '">'
          + '<svg viewBox="-1050 -1050 2100 2100" aria-hidden="true"><path d="'
          + entry.d + '"/></svg></button>';
      }

      objectGrid.innerHTML = shown > 0 ? markup
        : '<p class="waiting">Nothing here matches that.</p>';
    }

    function drawControls() {
      var layer = active >= 0 && active < layers.length ? layers[active] : null;

      if (opacity) {
        opacity.value = layer ? layer.a : embLimits.opacitySteps;
        opacity.disabled = !layer || layer.lk;
      }

      if (opacityValue) {
        var steps = layer ? layer.a : embLimits.opacitySteps;
        opacityValue.textContent = Math.round(steps * 100 / embLimits.opacitySteps) + '%';
      }

      embAll(form, '[data-flip]').forEach(function (button) {
        button.disabled = !layer || layer.lk;
      });

      embAll(paletteBox || form, '[data-colour]').forEach(function (button) {
        var index = parseInt(button.getAttribute('data-colour'), 10);
        var on = layer ? index === layer.c : index === colour;
        button.classList.toggle('on', on);
        button.setAttribute('aria-pressed', on ? 'true' : 'false');
      });

      if (hint) {
        hint.textContent = layers.length === 0
          ? 'Pick an object on the left to add your first layer.'
          : (layer
            ? (layer.lk
              ? nameOf(layer.o) + ' is locked. Unlock it in the layers panel to change it.'
              : 'Drag ' + nameOf(layer.o) + ' to move it, the corner handle to resize, the top '
                + 'handle to turn it. Arrow keys nudge; hold shift for bigger steps.')
            : 'Click a layer on the canvas or in the layers panel to work on it.');
      }
    }

    function draw() {
      drawLive();
      drawLayers();
      drawControls();
      refreshServed();
    }

    // ------------------------------------------------------------- mutations

    function mutate(index, change) {
      if (index < 0 || index >= layers.length) { return; }
      if (layers[index].lk) { return; }

      var layer = embCopy(layers[index]);
      change(layer);

      if (!embValid(layer)) { return; }

      layers[index] = layer;
    }

    function add(object) {
      if (layers.length >= embLimits.maxLayers) { return; }

      layers.push({
        o: object, x: 0, y: 0, s: 500, r: 0, c: colour,
        a: embLimits.opacitySteps, fx: false, fy: false, lk: false
      });

      active = layers.length - 1;
      draw();
    }

    // ------------------------------------------------------------ the canvas

    function toCanvas(event) {
      var box = stage.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) { return null; }

      return {
        x: Math.round((event.clientX - box.left) / box.width * 2000 - 1000),
        y: Math.round((event.clientY - box.top) / box.height * 2000 - 1000)
      };
    }

    stage.addEventListener('pointerdown', function (event) {
      var point = toCanvas(event);
      if (!point) { return; }

      var handle = event.target.closest ? event.target.closest('[data-handle]') : null;

      if (handle && active >= 0) {
        var layer = layers[active];
        drag = {
          mode: handle.getAttribute('data-handle'),
          at: active,
          from: point,
          size: layer.s,
          rotation: layer.r,
          reach: Math.max(1, Math.hypot(point.x - layer.x, point.y - layer.y))
        };
        stage.setPointerCapture(event.pointerId);
        event.preventDefault();
        return;
      }

      var group = event.target.closest ? event.target.closest('[data-layer]') : null;
      if (!group) {
        active = -1;
        draw();
        return;
      }

      active = parseInt(group.getAttribute('data-layer'), 10);
      drawLayers();
      drawControls();
      drawLive();

      if (!layers[active].lk) {
        drag = {
          mode: 'move', at: active, from: point,
          originX: layers[active].x, originY: layers[active].y
        };
        stage.setPointerCapture(event.pointerId);
      }

      event.preventDefault();
    });

    stage.addEventListener('pointermove', function (event) {
      if (!drag) { return; }

      var point = toCanvas(event);
      if (!point) { return; }

      var layer = layers[drag.at];
      if (!layer) { drag = null; return; }

      if (drag.mode === 'move') {
        mutate(drag.at, function (next) {
          next.x = embClamp(drag.originX + point.x - drag.from.x,
            -embLimits.maxOffset, embLimits.maxOffset);
          next.y = embClamp(drag.originY + point.y - drag.from.y,
            -embLimits.maxOffset, embLimits.maxOffset);
        });
      } else if (drag.mode === 'scale') {
        var reach = Math.max(1, Math.hypot(point.x - layer.x, point.y - layer.y));
        mutate(drag.at, function (next) {
          next.s = embClamp(Math.round(drag.size * reach / drag.reach),
            embLimits.minSize, embLimits.maxSize);
        });
      } else if (drag.mode === 'rotate') {
        var degrees = Math.atan2(point.y - layer.y, point.x - layer.x) * 180 / Math.PI + 90;
        mutate(drag.at, function (next) {
          var turned = Math.round(degrees);
          // Shift snaps to fifteen degrees, which is where a mark stops looking
          // like it was placed by hand and starts looking deliberate.
          if (event.shiftKey) { turned = Math.round(turned / 15) * 15; }
          next.r = ((turned % embLimits.rotationSteps) + embLimits.rotationSteps)
            % embLimits.rotationSteps;
        });
      }

      drawLive();
      refreshServed();
      event.preventDefault();
    });

    function endDrag(event) {
      if (!drag) { return; }
      drag = null;
      try { stage.releasePointerCapture(event.pointerId); } catch (e) { /* already gone */ }
      draw();
    }

    stage.addEventListener('pointerup', endDrag);
    stage.addEventListener('pointercancel', endDrag);

    stage.addEventListener('wheel', function (event) {
      if (active < 0 || !layers[active] || layers[active].lk) { return; }

      var step = event.deltaY < 0 ? 1 : -1;

      mutate(active, function (next) {
        if (event.shiftKey) {
          next.r = ((next.r + step * 5) % embLimits.rotationSteps + embLimits.rotationSteps)
            % embLimits.rotationSteps;
        } else {
          next.s = embClamp(next.s + step * 25, embLimits.minSize, embLimits.maxSize);
        }
      });

      drawLive();
      refreshServed();
      event.preventDefault();
    }, { passive: false });

    // The arrow keys, on the canvas rather than the document: a player tabbing
    // through the page must still be able to move the caret in the design code.
    stage.setAttribute('tabindex', '0');

    stage.addEventListener('keydown', function (event) {
      var steps = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
      var step = steps[event.key];
      if (!step || active < 0) { return; }

      var distance = event.shiftKey ? 50 : 10;

      mutate(active, function (next) {
        next.x = embClamp(next.x + step[0] * distance, -embLimits.maxOffset, embLimits.maxOffset);
        next.y = embClamp(next.y + step[1] * distance, -embLimits.maxOffset, embLimits.maxOffset);
      });

      drawLive();
      refreshServed();
      event.preventDefault();
    });

    // ------------------------------------------------------------- the panels

    if (objectGrid) {
      objectGrid.addEventListener('click', function (event) {
        var button = event.target.closest('[data-object]');
        if (!button) { return; }
        add(parseInt(button.getAttribute('data-object'), 10));
      });
    }

    embAll(form, '[data-cat]').forEach(function (button) {
      button.addEventListener('click', function () {
        category = button.getAttribute('data-cat');
        filter = '';
        var find = embFind(form, '[data-find]');
        if (find) { find.value = ''; }

        embAll(form, '[data-cat]').forEach(function (other) {
          var on = other === button;
          other.classList.toggle('on', on);
          other.setAttribute('aria-pressed', on ? 'true' : 'false');
        });

        drawObjects();
      });
    });

    var find = embFind(form, '[data-find]');
    if (find) {
      find.addEventListener('input', function () {
        filter = find.value;
        drawObjects();
      });
    }

    if (paletteBox) {
      paletteBox.addEventListener('click', function (event) {
        var button = event.target.closest('[data-colour]');
        if (!button) { return; }

        colour = parseInt(button.getAttribute('data-colour'), 10);
        mutate(active, function (next) { next.c = colour; });
        draw();
      });
    }

    if (opacity) {
      opacity.addEventListener('input', function () {
        var value = parseInt(opacity.value, 10);
        mutate(active, function (next) { next.a = value; });
        drawLive();
        drawLayers();
        drawControls();
        refreshServed();
      });
    }

    embAll(form, '[data-flip]').forEach(function (button) {
      button.addEventListener('click', function () {
        var axis = button.getAttribute('data-flip');
        mutate(active, function (next) {
          if (axis === 'x') { next.fx = !next.fx; } else { next.fy = !next.fy; }
        });
        draw();
      });
    });

    layerList.addEventListener('click', function (event) {
      var action = event.target.closest('[data-act]');

      if (action) {
        var at = parseInt(action.getAttribute('data-at'), 10);
        var verb = action.getAttribute('data-act');

        if (verb === 'clone' && layers.length < embLimits.maxLayers) {
          // A CLONE OF A LOCKED LAYER IS UNLOCKED, and lands just above the
          // original where you can see it. Cloning a locked layer is allowed on
          // purpose - it is how you keep a finished piece and vary it.
          var copy = embCopy(layers[at]);
          copy.lk = false;
          layers.splice(at + 1, 0, copy);
          active = at + 1;
        } else if (verb === 'delete' && !layers[at].lk) {
          layers.splice(at, 1);
          if (active >= layers.length) { active = layers.length - 1; }
        } else if (verb === 'lock') {
          layers[at].lk = !layers[at].lk;
        }

        draw();
        return;
      }

      var row = event.target.closest('[data-row]');
      if (!row) { return; }

      active = parseInt(row.getAttribute('data-row'), 10);
      draw();
    });

    // Reordering. HTML drag and drop rather than pointer maths, because the
    // browser already draws the drag image and handles the autoscroll.
    var carrying = -1;

    layerList.addEventListener('dragstart', function (event) {
      var row = event.target.closest('[data-row]');
      if (!row) { return; }
      carrying = parseInt(row.getAttribute('data-row'), 10);
      if (event.dataTransfer) { event.dataTransfer.effectAllowed = 'move'; }
    });

    layerList.addEventListener('dragover', function (event) {
      if (carrying < 0) { return; }
      event.preventDefault();
      if (event.dataTransfer) { event.dataTransfer.dropEffect = 'move'; }
    });

    layerList.addEventListener('drop', function (event) {
      var row = event.target.closest('[data-row]');
      if (carrying < 0 || !row) { return; }

      var onto = parseInt(row.getAttribute('data-row'), 10);
      event.preventDefault();

      if (onto !== carrying) {
        var moved = layers.splice(carrying, 1)[0];
        layers.splice(onto, 0, moved);
        active = onto;
      }

      carrying = -1;
      draw();
    });

    var deleteAll = embFind(form, '[data-delete-all]');
    if (deleteAll) {
      deleteAll.addEventListener('click', function () {
        var kept = layers.filter(function (layer) { return layer.lk; });
        if (kept.length === layers.length && layers.length > 0) { return; }

        layers = kept;
        active = layers.length - 1;
        draw();
      });
    }

    var undo = embFind(form, '[data-undo]');
    if (undo) {
      undo.addEventListener('click', function () {
        layers = embDecode(baseline) || [];
        active = layers.length - 1;
        draw();
      });
    }

    var apply = embFind(form, '[data-apply-code]');
    if (apply) {
      apply.addEventListener('click', function () {
        var read = embDecode(codeBox.value.trim());
        if (!read) {
          codeBox.setAttribute('aria-invalid', 'true');
          return;
        }

        codeBox.removeAttribute('aria-invalid');
        layers = read;
        active = layers.length - 1;
        draw();
      });
    }

    // ------------------------------------------------------------ the saving

    if (sheet) {
      form.addEventListener('submit', function (event) {
        // The submit inside the sheet is the real one; the button in the footer
        // opens the sheet instead. With no script at all neither of these runs
        // and the footer button simply posts, which is the only destination that
        // exists anyway.
        if (sheet.contains(event.submitter)) { return; }

        event.preventDefault();
        sheet.hidden = false;
        sheet.scrollIntoView({ block: 'nearest' });
      });

      var cancel = embFind(sheet, '[data-savecancel]');
      if (cancel) {
        cancel.addEventListener('click', function () { sheet.hidden = true; });
      }

      var copy = embFind(sheet, '[data-copycode]');
      if (copy) {
        copy.addEventListener('click', function () {
          codeBox.select();
          try {
            navigator.clipboard.writeText(codeBox.value);
            copy.textContent = 'Copied';
            window.setTimeout(function () { copy.textContent = 'Copy the design code'; }, 1600);
          } catch (e) {
            // No clipboard permission. The code is selected, which is the whole
            // of what a player needs to press the two keys themselves.
          }
        });
      }
    }

    // ------------------------------------------------------------------ start

    drawPalette();
    drawObjects();
    drawLayers();
    drawControls();

    embLoadCatalogue().then(function () {
      // A design whose objects the catalogue does not have is refused wholesale
      // rather than drawn with the missing layers dropped: a partly drawn emblem
      // that then gets SAVED would quietly throw away what was missing.
      for (var i = 0; i < layers.length; i++) {
        if (layers[i].o >= embCatalogue.length) { layers = []; active = -1; break; }
      }

      drawObjects();
      draw();
    }).catch(function () {
      if (objectGrid) {
        objectGrid.innerHTML = '<p class="waiting">The object catalogue could not be loaded. '
          + 'Reload the page to try again.</p>';
      }
    });
  }

  embAll(document, 'form[data-emblem]').forEach(embEditor);
})();
