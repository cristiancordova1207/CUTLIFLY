// Editor de vídeo de CaptLY (copiado tal cual a CUTLIFY): cortes, huecos, fades y volumen por clip (mismo manejo que el editor de MicLY).
// Clip = { s, e, t, fi, fo, gain }: inicio/fin en el vídeo original, posición en la línea de tiempo, fades (s) y volumen (dB).
(() => {
const css = (n) => getComputedStyle(document.documentElement).getPropertyValue(n).trim();
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const fmt2 = (t) => { t = Math.max(0, t || 0); const m = Math.floor(t / 60), s = t - m * 60; return String(m).padStart(2, '0') + ':' + s.toFixed(2).padStart(5, '0'); };
const dbToLin = (db) => Math.pow(10, (db || 0) / 20);
const fmtDb = (db) => (db > 0 ? '+' : '') + (Math.round((db || 0) * 10) / 10) + ' dB';
const EPS = 1e-4, RULER = 26;

const tl = $('#evTl'), tlc = $('#evCanvas'), video = $('#evVideo');
const ed = { loaded: false, info: null, clips: [], sel: -1, ph: 0, zoom: 10, undo: [], redo: [], playing: false, drag: null, wave: null, thumbs: null, sprite: null };
const clipLen = (c) => c.e - c.s;
const clipEnd = (c) => c.t + clipLen(c);
const total = () => ed.clips.reduce((m, c) => Math.max(m, clipEnd(c)), 0);
const clipAt = (t) => ed.clips.findIndex((c) => t >= c.t && t < clipEnd(c));
const newClip = (s, e, t) => ({ s, e, t, fi: 0, fo: 0, gain: 0 });
const clampFades = (c) => { const L = clipLen(c); c.fi = clamp(c.fi, 0, L); c.fo = clamp(c.fo, 0, L - c.fi); };
const sortClips = () => { const cur = ed.clips[ed.sel]; ed.clips.sort((a, b) => a.t - b.t); ed.sel = cur ? ed.clips.indexOf(cur) : -1; };
const env = (c, x) => Math.max(0, Math.min(1, c.fi > 0 ? x / c.fi : 1, c.fo > 0 ? (clipLen(c) - x) / c.fo : 1));
const editing = () => true;

// ---------- audio: vídeo → fade → volumen → altavoces ----------
let ac, fadeGain;
function audioGraph() {
  if (ac) return;
  ac = new AudioContext();
  fadeGain = ac.createGain();
  ac.createMediaElementSource(video).connect(fadeGain).connect(ac.destination);
}

// ---------- abrir ----------
async function openVideo(file) {
  try {
    if (ed.loaded && ed.undo.length && !(await modal('Hay cambios en la edición actual. ¿Descartarlos y abrir otro vídeo?', { ok: 'Descartar' }))) return;
    stop();
    const info = await api.editor.probe(file);
    Object.assign(ed, { loaded: true, info, clips: [newClip(0, info.duration, 0)], sel: -1, ph: 0, undo: [], redo: [], wave: null, thumbs: null, sprite: null });
    video.src = info.url;
    $('#evName').textContent = info.name.replace(/\.[^.]+$/, '');
    $('#evMeta').textContent = `${info.width}×${info.height} · ${info.fps} fps · ${fmt2(info.duration)}${info.audio ? '' : ' · sin audio'}`;
    $('#evEmpty').hidden = true; $('#evBody').hidden = false;
    document.title = info.name + ' — Editor de vídeo';
    requestAnimationFrame(fitZoom);
    api.editor.wave(file).then((buf) => {
      ed.wave = new Float32Array(buf);
      let pk = 0; for (let i = 0; i < ed.wave.length; i += 7) pk = Math.max(pk, Math.abs(ed.wave[i]));
      ed.waveK = 0.9 / Math.max(pk, 0.05); // las grabaciones suelen ser bajas: se normaliza solo para dibujar
      draw();
    }).catch(() => {});
    api.editor.thumbs(file, info.duration).then((t) => {
      const img = new Image();
      img.onload = () => { ed.thumbs = t; ed.sprite = img; draw(); };
      img.src = t.url;
    }).catch(() => {});
  } catch (e) { toast('No se pudo abrir: ' + String(e.message).replace(/^.*Error: /, ''), true); }
}
const pickVideo = async () => { const f = await api.editor.pick(); if (f) openVideo(f); };
$('#evOpen').onclick = pickVideo;
$('#evOpen2').onclick = pickVideo;
api.on('open', (f) => openVideo(f));
window.addEventListener('themechange', () => draw());

// ---------- deshacer ----------
function snapshot() { ed.undo.push(JSON.stringify(ed.clips)); if (ed.undo.length > 200) ed.undo.shift(); ed.redo = []; }
function undo(redo) {
  const [from, to] = redo ? [ed.redo, ed.undo] : [ed.undo, ed.redo];
  if (!from.length) return;
  stop();
  to.push(JSON.stringify(ed.clips));
  ed.clips = JSON.parse(from.pop());
  ed.sel = -1;
  draw(); seekPreview();
}
$('#evUndo').onclick = () => undo(false);
$('#evRedo').onclick = () => undo(true);

// ---------- operaciones (mismas teclas que MicLY) ----------
const shiftAfter = (from, delta, except) => ed.clips.forEach((c) => { if (c !== except && c.t >= from - EPS) c.t -= delta; });
const gapAt = () => {
  const prevEnd = ed.clips.filter((c) => clipEnd(c) <= ed.ph + EPS).reduce((m, c) => Math.max(m, clipEnd(c)), 0);
  const next = ed.clips.filter((c) => c.t >= ed.ph - EPS).reduce((m, c) => Math.min(m, c.t), Infinity);
  return { prevEnd, next };
};
const ops = {
  split() {
    const i = clipAt(ed.ph), c = ed.clips[i];
    if (!c || ed.ph - c.t < 0.01 || clipEnd(c) - ed.ph < 0.01) return toast('Pon el cursor dentro de un clip');
    snapshot();
    const cut = c.s + (ed.ph - c.t);
    const right = { ...c, s: cut, t: ed.ph, fi: 0 };
    c.e = cut; c.fo = 0;
    clampFades(c); clampFades(right);
    ed.clips.splice(i + 1, 0, right);
    ed.sel = i + 1;
  },
  del() {
    const i = ed.sel >= 0 ? ed.sel : clipAt(ed.ph);
    if (i < 0) return toast('No hay clip seleccionado');
    snapshot();
    ed.clips.splice(i, 1);
    ed.sel = -1;
  },
  closeGap() {
    if (clipAt(ed.ph) >= 0) return toast('Pon el cursor sobre un hueco');
    const { prevEnd, next } = gapAt();
    if (next === Infinity) return toast('No hay hueco: no hay nada a la derecha');
    snapshot();
    shiftAfter(next, next - prevEnd);
    ed.ph = prevEnd;
  },
  trimLeft() {
    const c = ed.clips[clipAt(ed.ph)];
    if (!c || ed.ph - c.t < EPS) return toast('Pon el cursor dentro de un clip');
    snapshot();
    const delta = ed.ph - c.t, end = clipEnd(c);
    c.s += delta;
    shiftAfter(end, delta, c);
    clampFades(c);
    ed.ph = c.t;
  },
  trimRight() {
    const c = ed.clips[clipAt(ed.ph)];
    if (!c) return toast('Pon el cursor dentro de un clip');
    snapshot();
    const end = clipEnd(c), delta = end - ed.ph;
    c.e -= delta;
    shiftAfter(end, delta, c);
    clampFades(c);
  },
};
function runOp(op) {
  if (!ed.loaded) return;
  stop();
  ops[op]();
  ed.clips = ed.clips.filter((c) => clipLen(c) > EPS);
  sortClips();
  draw(); seekPreview();
}
// Intercambia el clip seleccionado con el de al lado, conservando el hueco entre ambos.
function moveClip(dir) {
  const i = ed.sel, j = i + dir;
  if (i < 0 || j < 0 || j >= ed.clips.length) return;
  stop(); snapshot();
  const [A, B] = dir < 0 ? [ed.clips[j], ed.clips[i]] : [ed.clips[i], ed.clips[j]];
  const start = A.t, gap = B.t - clipEnd(A);
  B.t = start;
  A.t = start + clipLen(B) + gap;
  sortClips();
  ed.ph = ed.clips[ed.sel].t;
  follow(); draw(); seekPreview();
}

// ---------- vista previa ----------
// Fuera de un clip la imagen queda en negro; dentro, opacidad y volumen siguen los fades.
function applyLook() {
  const c = ed.clips[clipAt(ed.ph)];
  const k = c ? env(c, ed.ph - c.t) : 0;
  video.style.opacity = k;
  if (fadeGain) fadeGain.gain.setTargetAtTime(c ? k * dbToLin(c.gain) : 0, ac.currentTime, 0.01);
}
function seekPreview() {
  const c = ed.clips[clipAt(ed.ph)];
  if (c) { const t = c.s + (ed.ph - c.t); if (Math.abs(video.currentTime - t) > 0.02) video.currentTime = t; }
  applyLook();
}
let cur = -1, gapStart = 0, gapPh = 0;
async function play() {
  if (!ed.loaded || !ed.clips.length) return;
  audioGraph(); ac.resume();
  if (ed.ph >= total() - 0.01) ed.ph = 0;
  ed.playing = true; cur = -2;
  $('#evPlay').textContent = '❚❚';
  (function tick() {
    if (!ed.playing) return;
    const i = clipAt(ed.ph);
    if (i !== cur) { // entrar en un clip o en un hueco
      cur = i;
      if (i >= 0) { const c = ed.clips[i]; video.currentTime = c.s + (ed.ph - c.t); video.play().catch(() => {}); }
      else { video.pause(); gapStart = performance.now(); gapPh = ed.ph; }
    }
    if (cur >= 0) {
      const c = ed.clips[cur];
      ed.ph = c.t + (video.currentTime - c.s);
      if (video.currentTime >= c.e - 0.5 / ed.info.fps || video.ended) ed.ph = clipEnd(c) + EPS * 2;
    } else ed.ph = gapPh + (performance.now() - gapStart) / 1000;
    if (ed.ph >= total()) { ed.ph = total(); stop(); }
    applyLook(); follow(); draw();
    requestAnimationFrame(tick);
  })();
}
function stop() {
  if (!ed.playing) return;
  ed.playing = false;
  video.pause();
  $('#evPlay').textContent = '▶';
  draw();
}
const toggle = () => (ed.playing ? stop() : play());
$('#evPlay').onclick = toggle;
video.onclick = toggle;

// ---------- línea de tiempo ----------
const zoomFromSlider = (v) => 2 * Math.pow(2, v / 10);
const sliderFromZoom = (z) => 10 * Math.log2(z / 2);
function setZoom(z, anchorX = tl.clientWidth / 2) {
  const tAnchor = (tl.scrollLeft + anchorX) / ed.zoom;
  ed.zoom = clamp(z, 2, 2000);
  $('#evZoom').value = sliderFromZoom(ed.zoom);
  $('#evSpacer').style.width = Math.ceil(Math.max(total(), 1) * ed.zoom + 300) + 'px';
  tl.scrollLeft = tAnchor * ed.zoom - anchorX;
  draw();
}
function fitZoom() { if (!ed.loaded) return; setZoom((tl.clientWidth - 60) / Math.max(total(), 0.5), 0); tl.scrollLeft = 0; draw(); }
$('#evFit').onclick = fitZoom;
$('#evZoom').oninput = (e) => setZoom(zoomFromSlider(+e.target.value));
function follow() { const x = ed.ph * ed.zoom - tl.scrollLeft; if (x > tl.clientWidth - 40 || x < 0) tl.scrollLeft = ed.ph * ed.zoom - 60; }
tl.onscroll = () => draw();
new ResizeObserver(() => draw()).observe(tl);

const geo = () => { const top = RULER + 12, bot = tl.clientHeight - 12, split = top + Math.round((bot - top) * 0.55); return { top, bot, split, hy: top + 12 }; };
const clipBox = (c) => ({ a: c.t * ed.zoom - tl.scrollLeft, b: clipEnd(c) * ed.zoom - tl.scrollLeft });
// Cuadritos de fade (como en Premiere) en las esquinas superiores; se mueven hacia dentro según el fade.
function handles(c) {
  const { a, b } = clipBox(c), z = ed.zoom;
  return { inX: Math.min(b - 8, a + 8 + c.fi * z), outX: Math.max(a + 8, b - 8 - c.fo * z), hy: geo().hy };
}
function hitTest(x, y) {
  const { top, bot } = geo();
  if (y < RULER) return { type: 'ruler' };
  for (let i = ed.clips.length - 1; i >= 0; i--) {
    const c = ed.clips[i], { a, b } = clipBox(c);
    if (x < a || x > b || y < top || y > bot) continue;
    if (b - a > 40) {
      const H = handles(c);
      if (Math.abs(x - H.inX) <= 8 && Math.abs(y - H.hy) <= 8) return { type: 'fadeIn', i };
      if (Math.abs(x - H.outX) <= 8 && Math.abs(y - H.hy) <= 8) return { type: 'fadeOut', i };
    }
    if (x - a <= 6) return { type: 'trimL', i };
    if (b - x <= 6) return { type: 'trimR', i };
    return { type: 'clip', i };
  }
  return { type: 'empty' };
}

function fitCanvas() {
  const dpr = devicePixelRatio || 1, w = tl.clientWidth, h = tl.clientHeight;
  tlc.style.width = w + 'px'; tlc.style.height = h + 'px';
  if (tlc.width !== Math.round(w * dpr) || tlc.height !== Math.round(h * dpr)) { tlc.width = Math.round(w * dpr); tlc.height = Math.round(h * dpr); }
  const g = tlc.getContext('2d'); g.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { g, w, h };
}
function drawWave(g, s0, s1, x, w, mid, hh) {
  const d = ed.wave, per = (s1 - s0) / w;
  g.beginPath();
  for (let px = 0; px < w; px++) {
    let lo = 0, hi = 0;
    const i0 = Math.max(0, Math.floor(s0 + px * per)), i1 = Math.min(d.length, Math.max(i0 + 1, Math.floor(s0 + (px + 1) * per)));
    for (let i = i0; i < i1; i += Math.max(1, ((i1 - i0) / 64) | 0)) { const v = d[i]; if (v < lo) lo = v; if (v > hi) hi = v; }
    g.rect(x + px, mid - hi * hh, 1, Math.max(1, (hi - lo) * hh));
  }
  g.fill();
}
function draw() {
  if (!ed.loaded || !editing()) return;
  syncVolUi();
  const { g, w, h } = fitCanvas();
  const z = ed.zoom, sx = tl.scrollLeft, t0 = sx / z, t1 = (sx + w) / z, X = (t) => t * z - sx;
  const C = { muted: css('--muted'), violet: css('--violet'), mint: css('--mint'), pink: css('--pink'), amber: css('--amber'), panel: css('--panel'), panel2: css('--panel2'), text: css('--text'), onAccent: css('--on-accent') };
  $('#evSpacer').style.width = Math.ceil(Math.max(total(), 1) * z + 300) + 'px';
  g.clearRect(0, 0, w, h);

  // regla
  g.fillStyle = C.panel2; g.fillRect(0, 0, w, RULER);
  const step = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600].find((s) => s * z >= 80) || 600;
  g.font = '11px Inter'; g.fillStyle = C.muted; g.globalAlpha = 0.8;
  for (let t = Math.floor(t0 / step) * step; t <= t1; t += step) {
    const x = Math.round(X(t)) + 0.5;
    g.fillRect(x, RULER - 8, 1, 8);
    g.fillText(step < 1 ? fmt2(t) : fmt(t), x + 4, 15);
    for (let k = 1; k < 4; k++) g.fillRect(Math.round(X(t + (step * k) / 4)), RULER - 4, 1, 4);
  }
  g.globalAlpha = 1;

  const { top, bot, split } = geo();
  if (clipAt(ed.ph) < 0) { // hueco bajo el cursor (lo que cierra D)
    const { prevEnd, next } = gapAt();
    if (next !== Infinity) { g.fillStyle = C.amber; g.globalAlpha = 0.12; g.fillRect(X(prevEnd), top, (next - prevEnd) * z, bot - top); g.globalAlpha = 1; }
  }
  ed.clips.forEach((c, i) => {
    const { a, b } = clipBox(c);
    if (b < 0 || a > w) return;
    const sel = i === ed.sel, accent = sel ? C.mint : C.violet;
    g.fillStyle = accent; g.globalAlpha = sel ? 0.24 : 0.15;
    g.beginPath(); g.roundRect(a + 1, top, Math.max(2, b - a - 2), bot - top, 10); g.fill();
    g.globalAlpha = 1;
    g.save(); g.clip();
    // miniaturas (arriba)
    const T = ed.thumbs;
    if (T && ed.sprite) {
      const th = split - top - 4, tw = Math.round(th * 16 / 9);
      for (let x = Math.max(a, a + Math.floor((-a) / tw) * tw); x < Math.min(b, w); x += tw) {
        const k = clamp(Math.floor((c.s + (x - a) / z) / T.iv), 0, T.n - 1);
        g.drawImage(ed.sprite, (k % T.cols) * T.w, Math.floor(k / T.cols) * T.h, T.w, T.h, x, top + 2, tw, th);
      }
    } else { g.fillStyle = C.panel2; g.fillRect(a, top, b - a, split - top); }
    // onda del audio (abajo)
    g.fillStyle = C.panel; g.globalAlpha = 0.6; g.fillRect(a, split, b - a, bot - split); g.globalAlpha = 1;
    const va = Math.max(a, 0), vb = Math.min(b, w);
    if (ed.wave && vb - va >= 1) {
      g.fillStyle = accent;
      const k = dbToLin(c.gain);
      drawWave(g, (c.s + (va - a) / z) * 4000, (c.s + (vb - a) / z) * 4000, Math.floor(va), Math.ceil(vb - va), (split + bot) / 2, ((bot - split) / 2 - 4) * ed.waveK * Math.min(2, k));
    }
    if (Math.abs(c.gain || 0) >= 0.05 && b - a > 70) {
      g.font = '700 11px Inter'; const txt = '🔊 ' + fmtDb(c.gain), tw = g.measureText(txt).width + 14;
      g.fillStyle = C.mint; g.globalAlpha = 0.95; g.beginPath(); g.roundRect(a + 8, bot - 24, tw, 18, 9); g.fill(); g.globalAlpha = 1;
      g.fillStyle = C.onAccent; g.fillText(txt, a + 15, bot - 11);
    }
    if (b - a > 40) { // fades: zona oscurecida + envolvente + cuadritos
      const H = handles(c), x1 = a + c.fi * z, x2 = b - c.fo * z;
      g.fillStyle = '#000'; g.globalAlpha = 0.45;
      g.beginPath(); g.moveTo(a, H.hy); g.lineTo(x1, H.hy); g.lineTo(a, bot); g.closePath(); g.fill();
      g.beginPath(); g.moveTo(b, H.hy); g.lineTo(x2, H.hy); g.lineTo(b, bot); g.closePath(); g.fill();
      g.globalAlpha = 0.95; g.strokeStyle = C.text; g.lineWidth = 1.5;
      g.beginPath(); g.moveTo(a, c.fi > 0 ? bot : H.hy); g.lineTo(x1, H.hy); g.lineTo(x2, H.hy); g.lineTo(b, c.fo > 0 ? bot : H.hy); g.stroke();
      g.globalAlpha = 1;
      for (const hx of [H.inX, H.outX]) { g.fillStyle = C.panel; g.beginPath(); g.roundRect(hx - 5, H.hy - 5, 10, 10, 2); g.fill(); g.stroke(); }
    }
    g.restore();
    g.lineWidth = sel ? 2 : 1; g.strokeStyle = accent;
    g.beginPath(); g.roundRect(a + 1, top, Math.max(2, b - a - 2), bot - top, 10); g.stroke();
  });
  g.fillStyle = C.muted; g.globalAlpha = 0.4; g.fillRect(X(total()), RULER, 1, h - RULER); g.globalAlpha = 1;
  const px = Math.round(X(ed.ph));
  g.fillStyle = C.pink; g.fillRect(px - 1, 0, 2, h);
  g.beginPath(); g.moveTo(px - 7, 0); g.lineTo(px + 7, 0); g.lineTo(px + 7, 10); g.lineTo(px, 17); g.lineTo(px - 7, 10); g.fill();
  $('#evTime').textContent = `${fmt2(ed.ph)} / ${fmt2(total())}`;
}

// ---------- ratón ----------
let scrub = false;
const canvasXY = (e) => { const r = tlc.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
tlc.onmousedown = (e) => {
  if (!ed.loaded || e.button !== 0) return;
  const [x, y] = canvasXY(e), hit = hitTest(x, y), was = ed.playing;
  stop();
  if (['fadeIn', 'fadeOut', 'trimL', 'trimR'].includes(hit.type)) {
    ed.sel = hit.i;
    const orig = JSON.stringify(ed.clips);
    snapshot();
    ed.drag = { type: hit.type, i: hit.i, x0: e.clientX, orig };
    return draw();
  }
  ed.ph = clamp((x + tl.scrollLeft) / ed.zoom, 0, total());
  if (hit.type !== 'ruler') ed.sel = hit.type === 'clip' ? hit.i : -1;
  scrub = true;
  draw(); seekPreview();
  if (was) play();
};
// Arrastre de fades y bordes, recalculado desde el estado inicial para que sea exacto.
function dragEdit(e) {
  const d = ed.drag, dt = (e.clientX - d.x0) / ed.zoom;
  ed.clips = JSON.parse(d.orig);
  const c = ed.clips[d.i], L = clipLen(c);
  if (d.type === 'fadeIn') c.fi = clamp(c.fi + dt, 0, L - c.fo);
  else if (d.type === 'fadeOut') c.fo = clamp(c.fo - dt, 0, L - c.fi);
  else if (d.type === 'trimL') {
    const prevEnd = ed.clips.filter((o) => o !== c && clipEnd(o) <= c.t + EPS).reduce((m, o) => Math.max(m, clipEnd(o)), 0);
    const ns = clamp(c.s + dt, 0, c.e - 0.05), oldT = c.t;
    c.t += ns - c.s; c.s = ns;
    if (c.t < prevEnd) { const push = prevEnd - c.t; c.t += push; shiftAfter(oldT, -push, c); }
    ed.ph = c.t;
  } else if (d.type === 'trimR') {
    const end = clipEnd(c);
    c.e = clamp(c.e + dt, c.s + 0.05, ed.info.duration);
    const next = ed.clips.filter((o) => o !== c && o.t >= end - EPS).reduce((m, o) => Math.min(m, o.t), Infinity);
    if (clipEnd(c) > next) shiftAfter(end, next - clipEnd(c), c);
    ed.ph = clipEnd(c) - 0.001;
  }
  clampFades(c);
  ed.sel = d.i;
  sortClips();
  draw(); seekPreview();
}
window.addEventListener('mousemove', (e) => {
  if (ed.drag) return dragEdit(e);
  if (scrub) { ed.ph = clamp((canvasXY(e)[0] + tl.scrollLeft) / ed.zoom, 0, total()); draw(); return seekPreview(); }
  if (e.target === tlc && ed.loaded) {
    const t = hitTest(...canvasXY(e)).type;
    tlc.style.cursor = t === 'trimL' || t === 'trimR' ? 'ew-resize' : t === 'fadeIn' || t === 'fadeOut' ? 'grab' : 'crosshair';
  }
});
window.addEventListener('mouseup', () => { scrub = false; if (ed.drag) { ed.drag = null; draw(); } });
tl.addEventListener('wheel', (e) => {
  e.preventDefault();
  if (e.ctrlKey) setZoom(ed.zoom * (e.deltaY < 0 ? 1.2 : 1 / 1.2), e.clientX - tl.getBoundingClientRect().left);
  else tl.scrollLeft += e.deltaY + e.deltaX;
}, { passive: false });

// ---------- volumen del audio ----------
const volTargets = () => (ed.sel >= 0 ? [ed.clips[ed.sel]] : ed.clips);
function syncVolUi() {
  const t = volTargets(), db = t.length ? t[0].gain || 0 : 0;
  $('#evVol').value = db;
  $('#evVolVal').textContent = fmtDb(db);
  $('#evVolTarget').textContent = ed.sel >= 0 ? 'clip seleccionado' : 'todos los clips';
}
let volSnap = false;
$('#evVol').addEventListener('change', (e) => { volSnap = false; e.target.blur(); });
$('#evVol').addEventListener('input', (e) => {
  if (!ed.loaded) return;
  if (!volSnap) { snapshot(); volSnap = true; }
  for (const c of volTargets()) c.gain = +e.target.value;
  applyLook(); draw();
});
$('#evVolReset').onclick = () => {
  if (!ed.loaded) return;
  snapshot();
  for (const c of volTargets()) c.gain = 0;
  applyLook(); draw();
};

// ---------- exportar ----------
const exportClips = () => ed.clips.map((c) => ({ s: c.s, e: c.e, t: c.t, gain: dbToLin(c.gain), fi: c.fi, fo: c.fo }));
const errMsg = (e) => String(e.message).replace(/^.*Error: /, '');
$('#evPremiere').onclick = async () => {
  if (!ed.loaded || !ed.clips.length) return toast('No hay clips que exportar', true);
  stop();
  try {
    if (await api.editor.premiere(ed.info, exportClips())) toast('⇪ Proyecto para Premiere guardado. En Premiere: Archivo › Importar');
  } catch (e) { toast('No se pudo exportar: ' + errMsg(e), true); }
};
const prog = $('#evProg');
api.editor.onProgress((p) => { $('i', prog).style.width = p * 100 + '%'; $('span', prog).textContent = Math.round(p * 100) + ' %'; });
$('#evRender').onclick = async () => {
  if (!ed.loaded || !ed.clips.length) return toast('No hay clips que exportar', true);
  stop();
  $('i', prog).style.width = '0'; $('span', prog).textContent = '0 %';
  prog.hidden = false; $('#evRender').disabled = true;
  try {
    const f = await api.editor.render(ed.info, exportClips());
    if (f) toast('⬇ Vídeo exportado: ' + f.split('\\').pop());
  } catch (e) { toast('No se pudo exportar: ' + errMsg(e), true); }
  prog.hidden = true; $('#evRender').disabled = false;
};

// ---------- teclado ----------
const KEYS = { KeyS: 'split', KeyX: 'split', KeyA: 'del', Delete: 'del', KeyD: 'closeGap', KeyQ: 'trimLeft', KeyW: 'trimRight' };
$('#evKeys').innerHTML = [['S / X', 'Cortar'], ['A / Supr', 'Borrar clip'], ['D', 'Cerrar hueco'], ['Q / W', 'Recortar izq. / der.'], ['← →', 'Mover clip'],
  ['Esc', 'Deseleccionar'], ['Espacio', 'Reproducir'], ['Ctrl+Z', 'Deshacer'], ['Ctrl+rueda', 'Zoom'], ['Arrastra ▫', 'Fade in / out']]
  .map(([k, t]) => `<div class="kb"><kbd>${k}</kbd> <span>${t}</span></div>`).join('');
document.addEventListener('keydown', (e) => {
  if (!editing() || !ed.loaded || e.target.matches('input:not([type=range]), select, textarea')) return;
  if (e.ctrlKey && (e.code === 'KeyZ' || e.code === 'KeyY')) { e.preventDefault(); return undo(e.code === 'KeyY' || e.shiftKey); }
  if (e.ctrlKey || e.altKey) return;
  if (e.code === 'Space') { e.preventDefault(); return toggle(); }
  if (e.code === 'Escape') { ed.sel = -1; return draw(); }
  if (e.code === 'Home' || e.code === 'End') { stop(); ed.ph = e.code === 'Home' ? 0 : total(); follow(); draw(); return seekPreview(); }
  if (e.code === 'ArrowLeft' || e.code === 'ArrowRight') { e.preventDefault(); return moveClip(e.code === 'ArrowLeft' ? -1 : 1); }
  if (KEYS[e.code]) { e.preventDefault(); runOp(KEYS[e.code]); }
});

window.CaptEditor = { open: openVideo, ed };
})();
