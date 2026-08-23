(function () {
  'use strict';
  var header = document.querySelector('[data-header]');
  var menu = document.querySelector('.menu-toggle');
  var nav = document.getElementById('primary-nav');
  function onScroll() { if (header) header.classList.toggle('is-scrolled', window.scrollY > 24); }
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });
  if (menu && nav) {
    menu.addEventListener('click', function () {
      var open = menu.getAttribute('aria-expanded') === 'true';
      menu.setAttribute('aria-expanded', String(!open));
      nav.classList.toggle('is-open', !open);
    });
    nav.addEventListener('click', function () {
      menu.setAttribute('aria-expanded', 'false');
      nav.classList.remove('is-open');
    });
  }

  var reduced = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var reveals = Array.prototype.slice.call(document.querySelectorAll('.reveal'));
  if (reduced || !('IntersectionObserver' in window)) {
    reveals.forEach(function (el) { el.classList.add('is-visible'); });
  } else {
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) { entry.target.classList.add('is-visible'); observer.unobserve(entry.target); }
      });
    }, { threshold: .12 });
    reveals.forEach(function (el) { observer.observe(el); });
  }

  var canvas = document.getElementById('sky');
  if (!canvas || !canvas.getContext || reduced) return;
  var ctx = canvas.getContext('2d');
  if (!ctx) return;
  var width = 0, height = 0, motes = [], stars = [], frame = 0;
  function reset() {
    width = Math.max(1, window.innerWidth); height = Math.max(1, window.innerHeight);
    var ratio = Math.min(window.devicePixelRatio || 1, 1.5);
    canvas.width = Math.round(width * ratio); canvas.height = Math.round(height * ratio);
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    motes = []; stars = [];
    for (var i = 0; i < (width < 700 ? 18 : 34); i++) motes.push(spawn(Math.random() * width));
    for (var s = 0; s < (width < 700 ? 54 : 110); s++) {
      stars.push({ x: Math.random() * width, y: Math.random() * height, size: .35 + Math.random() * 1.15, alpha: .08 + Math.random() * .28, phase: Math.random() * 6.283 });
    }
  }
  function spawn(x) { return { x: x == null ? -80 : x, y: Math.random() * height, speed: .18 + Math.random() * .48, size: 20 + Math.random() * 75, alpha: .025 + Math.random() * .07, phase: Math.random() * 7 }; }
  function draw(time) {
    ctx.clearRect(0, 0, width, height);
    for (var s = 0; s < stars.length; s++) {
      var star = stars[s];
      ctx.globalAlpha = star.alpha * (.72 + Math.sin(time * .0007 + star.phase) * .28);
      ctx.fillStyle = '#d9edf0';
      ctx.fillRect(star.x, star.y, star.size, star.size);
    }
    ctx.globalAlpha = 1;
    for (var i = 0; i < motes.length; i++) {
      var m = motes[i]; m.x += m.speed; m.y += Math.sin(time * .0003 + m.phase) * .08;
      if (m.x > width + m.size) motes[i] = m = spawn(-m.size);
      ctx.strokeStyle = 'rgba(205,232,230,' + m.alpha + ')'; ctx.lineWidth = 1; ctx.lineCap = 'round';
      ctx.beginPath(); ctx.moveTo(m.x - m.size, m.y); ctx.quadraticCurveTo(m.x - m.size * .45, m.y - 8, m.x, m.y); ctx.stroke();
    }
    frame = window.requestAnimationFrame(draw);
  }
  window.addEventListener('resize', reset, { passive: true });
  document.addEventListener('visibilitychange', function () {
    if (document.hidden && frame) { cancelAnimationFrame(frame); frame = 0; }
    else if (!frame) frame = requestAnimationFrame(draw);
  });
  reset(); frame = requestAnimationFrame(draw);
})();
