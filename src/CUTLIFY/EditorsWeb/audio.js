// Editor de audio de MicLY dentro de CUTLIFY: mismo editor (cortes, fades, efectos de voz, volumen, Premiere).
// Cambia solo de dónde salen los audios: los abre CUTLIFY (o su grabadora) en lugar de la biblioteca de MicLY.
import { FX, fxInfo, makeFx } from './fx.js';

const esc = (s) => String(s).replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
const css = (n) => getComputedStyle(document.documentElement).getPropertyValue(n).trim();
const fmt = (t, d = 1) => {
  t = Math.max(0, t || 0);
  const m = Math.floor(t / 60), s = t - m * 60;
  return `${String(m).padStart(2, '0')}:${s.toFixed(d).padStart(d ? 3 + d : 2, '0')}`;
};
const DEFAULT_KEYS = {
  discard: 'KeyR', commit: 'KeyT',
  split: 'KeyS', split2: 'KeyX', del: 'KeyA', closeGap: 'KeyD', trimLeft: 'KeyQ', trimRight: 'KeyW',
};
const KEY_INFO = {
  discard: ['Borrar desde la última toma', 'rec'], commit: ['Guardar toma', 'rec'],
  split: ['Cortar en el cursor', 'edit'], split2: ['Cortar (alternativa)', 'edit'], del: ['Borrar clip', 'edit'],
  closeGap: ['Borrar hueco (cerrar)', 'edit'], trimLeft: ['Borrar del cursor a la izquierda', 'edit'], trimRight: ['Borrar del cursor a la derecha', 'edit'],
};
const keyLabel = (code) => (code || '—').replace(/^Key/, '').replace(/^Digit/, '').replace(/^Numpad/, 'Num ');
const accel = (code) => (/^(Key[A-Z]|Digit\d|F\d{1,2})$/.test(code) ? keyLabel(code) : null);

const S = { keys: DEFAULT_KEYS };
let audioCtx;
const ctx = () => (audioCtx ??= new AudioContext());

// ---------- audio utils ----------
function encodeWav(samples, sr) {
  const b = new ArrayBuffer(44 + samples.length * 2), v = new DataView(b);
  const str = (o, s) => [...s].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
  str(0, 'RIFF'); v.setUint32(4, 36 + samples.length * 2, true); str(8, 'WAVE'); str(12, 'fmt ');
  v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true); v.setUint32(24, sr, true);
  v.setUint32(28, sr * 2, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true); str(36, 'data'); v.setUint32(40, samples.length * 2, true);
  for (let i = 0; i < samples.length; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]));
    v.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7fff, true);
  }
  return b;
}
async function decode(buf) {
  const ab = await ctx().decodeAudioData(buf.slice(0));
  let data = ab.getChannelData(0);
  if (ab.numberOfChannels > 1) {
    data = new Float32Array(data);
    for (let c = 1; c < ab.numberOfChannels; c++) { const d = ab.getChannelData(c); for (let i = 0; i < data.length; i++) data[i] += d[i]; }
    for (let i = 0; i < data.length; i++) data[i] /= ab.numberOfChannels;
  }
  return { data, sr: ab.sampleRate };
}
// Pico min/max cada 256 muestras para dibujar rápido audios largos.
function buildPeaks(data) {
  const n = Math.ceil(data.length / 256), min = new Float32Array(n), max = new Float32Array(n);
  for (let b = 0; b < n; b++) {
    let lo = 0, hi = 0;
    for (let i = b * 256, e = Math.min(data.length, i + 256); i < e; i++) { const s = data[i]; if (s < lo) lo = s; if (s > hi) hi = s; }
    min[b] = lo; max[b] = hi;
  }
  let top = 0.02;
  for (let b = 0; b < n; b++) top = Math.max(top, max[b], -min[b]);
  return { min, max, top };
}
// Dibuja las muestras [a, b) en el rectángulo x..x+w, centrado en y con media altura hh.
function drawWave(g, data, peaks, a, b, x, w, y, hh) {
  hh /= peaks.top; // normalizado: las grabaciones bajas también se ven
  const spp = (b - a) / w;
  for (let px = 0; px < w; px++) {
    let s0 = Math.floor(a + px * spp), s1 = Math.max(s0 + 1, Math.floor(a + (px + 1) * spp));
    if (s0 >= data.length) break;
    s1 = Math.min(s1, data.length);
    let lo = 0, hi = 0;
    if (spp >= 512) {
      for (let k = s0 >> 8, e = Math.min(peaks.min.length, (s1 >> 8) + 1); k < e; k++) { if (peaks.min[k] < lo) lo = peaks.min[k]; if (peaks.max[k] > hi) hi = peaks.max[k]; }
    } else {
      for (let i = s0; i < s1; i++) { const s = data[i]; if (s < lo) lo = s; if (s > hi) hi = s; }
    }
    const top = y - hi * hh, h = Math.max(1, (hi - lo) * hh);
    g.fillRect(x + px, top, 1, h);
  }
}
function fitCanvas(c) {
  const dpr = devicePixelRatio || 1, w = c.clientWidth, h = c.clientHeight;
  if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
  const g = c.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { g, w, h };
}

// ================= EDICIÓN =================
// Clips: s/e = inicio/fin en el audio original, t = posición en la línea de tiempo (segundos).
// vol = fades de volumen; fx = efectos del clip (cada uno con sus fades); edit = qué fades mueven los cuadritos (-1 = volumen).
const tl = $('#timeline'), tlc = $('#tlCanvas');
const RULER = 26;
const ed = { loaded: false, clips: [], sel: -1, ph: 0, zoom: 100, undo: [], redo: [], dirty: false, playing: false, nodes: [], drop: -1, drag: null };
const clipLen = (c) => c.e - c.s;
const clipEnd = (c) => c.t + clipLen(c);
const total = () => ed.clips.reduce((m, c) => Math.max(m, clipEnd(c)), 0);
const clipAt = (t) => ed.clips.findIndex((c) => t >= c.t && t < clipEnd(c));
const srcDur = () => ed.data.length / ed.sr;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const EPS = 1e-4;
const newClip = (s, e, t) => ({ s, e, t, vol: { fi: 0, fo: 0 }, fx: [], edit: -1, gain: 0 });
const dbToLin = (db) => Math.pow(10, (db || 0) / 20);
const fmtDb = (db) => (db > 0 ? '+' : '') + (Math.round((db || 0) * 10) / 10) + ' dB';
const fadeTarget = (c) => c.fx[c.edit] || c.vol;
function clampFades(c) {
  const L = clipLen(c);
  for (const f of [c.vol, ...c.fx]) { f.fi = clamp(f.fi, 0, L); f.fo = clamp(f.fo, 0, L - f.fi); }
}
function sortClips() {
  const cur = ed.clips[ed.sel];
  ed.clips.sort((a, b) => a.t - b.t);
  ed.sel = cur ? ed.clips.indexOf(cur) : -1;
}

// Abre un audio (ruta) que CUTLIFY sirve en https://app.cutlify/file?p=…
async function openAudio(path) {
  if (ed.loaded && ed.dirty && !(await modal('Hay cambios sin guardar en la edición. ¿Descartarlos?', { ok: 'Descartar' }))) return;
  let info, data, sr;
  try {
    info = await api.call('audio.info', path);
    ({ data, sr } = await decode(await api.fetchBuf(info.url)));
  } catch (e) { return toast('No se pudo abrir: ' + String(e.message).replace(/^.*Error: /, ''), true); }
  edStop();
  const name = info.name;
  Object.assign(ed, { loaded: true, path: info.path, info, name, data, sr, peaks: buildPeaks(data), clips: [newClip(0, data.length / sr, 0)], sel: -1, ph: 0, undo: [], redo: [], dirty: false });
  ed.abuf = ctx().createBuffer(1, data.length, sr);
  ed.abuf.copyToChannel(data, 0);
  $('#editName').textContent = name.replace(/\.[^.]+$/, '');
  document.title = name + ' — Editor de audio';
  $('#edEmpty').hidden = true;
  $('#editor').hidden = false;
  requestAnimationFrame(fitZoom);
}

function snapshot() { ed.undo.push(JSON.stringify(ed.clips)); if (ed.undo.length > 200) ed.undo.shift(); ed.redo = []; ed.dirty = true; }
function undo(redo) {
  const [from, to] = redo ? [ed.redo, ed.undo] : [ed.undo, ed.redo];
  if (!from.length) return;
  edStop(); hideFxPop();
  to.push(JSON.stringify(ed.clips));
  ed.clips = JSON.parse(from.pop());
  ed.sel = -1; ed.dirty = true;
  drawTimeline();
}
// Desplaza a la izquierda (delta > 0) o derecha (delta < 0) los clips que empiezan en o después de `from`.
const shiftAfter = (from, delta, except) => ed.clips.forEach((c) => { if (c !== except && c.t >= from - EPS) c.t -= delta; });

const edOps = {
  split() {
    const i = clipAt(ed.ph), c = ed.clips[i];
    if (!c || ed.ph - c.t < 0.01 || clipEnd(c) - ed.ph < 0.01) return toast('Pon el cursor dentro de un clip');
    snapshot();
    const cut = c.s + (ed.ph - c.t);
    const right = { ...structuredClone(c), s: cut, t: ed.ph };
    c.e = cut;
    c.vol.fo = 0; c.fx.forEach((f) => (f.fo = 0));
    right.vol.fi = 0; right.fx.forEach((f) => (f.fi = 0));
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
    const prevEnd = ed.clips.filter((c) => clipEnd(c) <= ed.ph + EPS).reduce((m, c) => Math.max(m, clipEnd(c)), 0);
    const next = ed.clips.filter((c) => c.t >= ed.ph - EPS).reduce((m, c) => Math.min(m, c.t), Infinity);
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
edOps.split2 = edOps.split;
function runEdOp(op) {
  if (!ed.loaded) return;
  edStop(); hideFxPop();
  edOps[op]();
  ed.clips = ed.clips.filter((c) => clipLen(c) > EPS);
  sortClips();
  drawTimeline();
}
// Intercambia el clip seleccionado con el de al lado (← / →), conservando el hueco entre ambos.
function moveClip(dir) {
  const i = ed.sel, j = i + dir;
  if (i < 0 || j < 0 || j >= ed.clips.length) return;
  edStop(); hideFxPop(); snapshot();
  const [A, B] = dir < 0 ? [ed.clips[j], ed.clips[i]] : [ed.clips[i], ed.clips[j]];
  const start = A.t, gap = B.t - clipEnd(A);
  B.t = start;
  A.t = start + clipLen(B) + gap;
  sortClips();
  ed.ph = ed.clips[ed.sel].t;
  followPlayhead(); drawTimeline();
}

// ---------- efectos ----------
function applyFx(i, id) {
  const c = ed.clips[i];
  if (!c) return;
  edStop(); snapshot();
  c.fx.push({ type: id, fi: 0, fo: 0 });
  c.edit = c.fx.length - 1;
  ed.sel = i;
  drawTimeline();
  toast(`${fxInfo(id).icon} ${fxInfo(id).name} aplicado`);
}
$('#fxShelf').innerHTML = '<span class="fx-label">Efectos</span>' +
  FX.map((f) => `<div class="fx-chip" draggable="true" data-fx="${f.id}" title="Arrastra a un clip"><span>${f.icon}</span>${f.name}</div>`).join('');
$$('.fx-chip').forEach((el) => {
  el.ondragstart = (e) => { e.dataTransfer.setData('text/fx', el.dataset.fx); e.dataTransfer.effectAllowed = 'copy'; el.classList.add('lift'); };
  el.ondragend = () => el.classList.remove('lift');
  el.onclick = () => (ed.sel >= 0 ? applyFx(ed.sel, el.dataset.fx) : toast('Arrastra el efecto encima de un clip'));
});
const xToClip = (e) => { const b = tlc.getBoundingClientRect(); return clipAt((e.clientX - b.left + tl.scrollLeft) / ed.zoom); };
tlc.ondragover = (e) => {
  if (!ed.loaded || !e.dataTransfer.types.includes('text/fx')) return;
  e.preventDefault();
  const i = xToClip(e);
  if (i !== ed.drop) { ed.drop = i; drawTimeline(); }
};
tlc.ondragleave = () => { ed.drop = -1; drawTimeline(); };
tlc.ondrop = (e) => {
  e.preventDefault();
  const i = xToClip(e), id = e.dataTransfer.getData('text/fx');
  ed.drop = -1;
  if (i >= 0 && id) applyFx(i, id); else drawTimeline();
};

const pop = $('#fxPop');
function hideFxPop() { pop.hidden = true; }
function showFxPop(i) {
  const c = ed.clips[i];
  pop.innerHTML = `
    <div class="fp-head">Efectos del clip<small>Los cuadritos de las esquinas hacen el fade de lo que elijas</small></div>
    <label class="fp-row"><input type="radio" name="fpt" value="-1" ${c.edit < 0 ? 'checked' : ''}><span>🔊 Volumen</span><em>predeterminado</em></label>
    ${c.fx.map((f, k) => `<label class="fp-row"><input type="radio" name="fpt" value="${k}" ${c.edit === k ? 'checked' : ''}><span>${fxInfo(f.type).icon} ${esc(fxInfo(f.type).name)}</span><button data-rm="${k}" title="Quitar efecto">✕</button></label>`).join('')}
    ${c.fx.length ? '' : '<p class="hint">Arrastra un efecto de la barra de arriba a este clip.</p>'}
    <button class="ghost sm" data-reset>Quitar fade</button>`;
  $$('input', pop).forEach((r) => (r.onchange = () => { c.edit = +r.value; drawTimeline(); }));
  $$('[data-rm]', pop).forEach((b) => (b.onclick = (e) => {
    e.preventDefault();
    snapshot();
    const k = +b.dataset.rm;
    c.fx.splice(k, 1);
    if (c.edit === k) c.edit = -1; else if (c.edit > k) c.edit--;
    showFxPop(ed.clips.indexOf(c)); drawTimeline();
  }));
  $('[data-reset]', pop).onclick = () => { snapshot(); const f = fadeTarget(c); f.fi = f.fo = 0; drawTimeline(); };
  const box = clipBox(c), host = $('#editor').getBoundingClientRect(), tb = tl.getBoundingClientRect();
  pop.hidden = false;
  pop.style.left = clamp(tb.left - host.left + box.b - pop.offsetWidth, 8, host.width - pop.offsetWidth - 8) + 'px';
  pop.style.top = tb.top - host.top + RULER + 40 + 'px';
}
document.addEventListener('mousedown', (e) => { if (!pop.hidden && !pop.contains(e.target) && e.target !== tlc) hideFxPop(); });

// ---------- audio: el mismo grafo sirve para reproducir y para guardar ----------
// Envolvente lineal 0→1→0 con fade in/out (en segundos del clip), empezando en `off`.
function schedEnv(param, when, off, len, fi, fo, inv) {
  const v = (t) => { const x = Math.min(1, fi > 0 ? t / fi : 1, fo > 0 ? (len - t) / fo : 1); return inv ? 1 - x : x; };
  param.setValueAtTime(v(off), when);
  for (const t of [fi, len - fo, len]) if (t > off + EPS) param.linearRampToValueAtTime(v(t), when + t - off);
}
function buildClip(ac, dest, c, when, off, track) {
  const len = clipLen(c), src = ac.createBufferSource();
  src.buffer = ed.abuf;
  let node = src;
  for (const f of c.fx) {
    const st = makeFx(ac, f.type, track), sum = ac.createGain(), dry = ac.createGain(), wet = ac.createGain();
    node.connect(dry).connect(sum);
    node.connect(st.input);
    st.output.connect(wet).connect(sum);
    schedEnv(wet.gain, when, off, len, f.fi, f.fo, false);
    schedEnv(dry.gain, when, off, len, f.fi, f.fo, true);
    node = sum;
  }
  const vol = ac.createGain(), level = ac.createGain();
  level.gain.value = dbToLin(c.gain);
  node.connect(vol).connect(level).connect(dest);
  schedEnv(vol.gain, when, off, len, c.vol.fi, c.vol.fo, false);
  (ed.levels ??= new Map()).set(c, level); // para cambiar el volumen mientras suena
  src.start(when, c.s + off, len - off);
  track.push(src);
}
async function render() {
  const oc = new OfflineAudioContext(1, Math.max(1, Math.ceil(total() * ed.sr)), ed.sr);
  for (const c of ed.clips) buildClip(oc, oc.destination, c, c.t, 0, []);
  return (await oc.startRendering()).getChannelData(0);
}

function edPlay() {
  edStop();
  if (ed.ph >= total() - 0.01) ed.ph = 0;
  const ac = ctx(), p0 = ed.ph, t0 = ac.currentTime + 0.05;
  ed.master = ac.createGain();
  ed.master.connect(ac.destination);
  for (const c of ed.clips) {
    if (clipEnd(c) <= p0) continue;
    buildClip(ac, ed.master, c, t0 + Math.max(0, c.t - p0), Math.max(0, p0 - c.t), ed.nodes);
  }
  ed.playing = true;
  $('#edPlay').textContent = '❚❚';
  (function tick() {
    if (!ed.playing) return;
    ed.ph = p0 + Math.max(0, ac.currentTime - t0);
    if (ed.ph >= total()) { ed.ph = total(); edStop(); }
    followPlayhead();
    drawTimeline();
    requestAnimationFrame(tick);
  })();
}
function edStop() {
  ed.nodes.forEach((n) => { try { n.stop(); } catch {} });
  ed.nodes = [];
  ed.master?.disconnect();
  ed.master = null;
  ed.playing = false;
  $('#edPlay').textContent = '▶';
}
const edToggle = () => (ed.playing ? edStop() : edPlay());
$('#edPlay').onclick = edToggle;

// ---------- timeline ----------
const zoomFromSlider = (v) => 4 * Math.pow(2, v / 10);
const sliderFromZoom = (z) => 10 * Math.log2(z / 4);
function setZoom(z, anchorX = tl.clientWidth / 2) {
  const tAnchor = (tl.scrollLeft + anchorX) / ed.zoom;
  ed.zoom = clamp(z, 4, 4096);
  $('#edZoom').value = sliderFromZoom(ed.zoom);
  $('#tlSpacer').style.width = Math.ceil(Math.max(total(), 1) * ed.zoom + 300) + 'px';
  tl.scrollLeft = tAnchor * ed.zoom - anchorX;
  drawTimeline();
}
function fitZoom() {
  if (!ed.loaded) return;
  setZoom((tl.clientWidth - 60) / Math.max(total(), 0.5), 0);
  tl.scrollLeft = 0;
  drawTimeline();
}
$('#edFit').onclick = fitZoom;
$('#edZoom').oninput = (e) => setZoom(zoomFromSlider(+e.target.value));
function followPlayhead() {
  const x = ed.ph * ed.zoom - tl.scrollLeft;
  if (x > tl.clientWidth - 40 || x < 0) tl.scrollLeft = ed.ph * ed.zoom - 60;
}
tl.onscroll = () => { hideFxPop(); drawTimeline(); };
new ResizeObserver(() => drawTimeline()).observe(tl);

const geo = () => { const top = RULER + 14, bot = tl.clientHeight - 14; return { top, bot, hy: top + 30 }; };
const clipBox = (c) => ({ a: c.t * ed.zoom - tl.scrollLeft, b: clipEnd(c) * ed.zoom - tl.scrollLeft });
// Cuadritos de fade (como en Premiere): en las esquinas, se mueven hacia dentro según la duración del fade.
function handles(c) {
  const { a, b } = clipBox(c), { top, hy } = geo(), f = fadeTarget(c), z = ed.zoom;
  return { inX: Math.min(b - 8, a + 8 + f.fi * z), outX: Math.max(a + 8, b - 8 - f.fo * z), hy, badge: { x: b - 30, y: top + 6, w: 24, h: 16 } };
}
function hitTest(x, y) {
  const { top, bot } = geo();
  if (y < RULER) return { type: 'ruler' };
  for (let i = ed.clips.length - 1; i >= 0; i--) {
    const c = ed.clips[i], { a, b } = clipBox(c);
    if (x < a || x > b || y < top || y > bot) continue;
    if (b - a > 56) {
      const H = handles(c), B = H.badge;
      if (x >= B.x && x <= B.x + B.w && y >= B.y && y <= B.y + B.h) return { type: 'badge', i };
      if (Math.abs(x - H.inX) <= 8 && Math.abs(y - H.hy) <= 8) return { type: 'fadeIn', i };
      if (Math.abs(x - H.outX) <= 8 && Math.abs(y - H.hy) <= 8) return { type: 'fadeOut', i };
    }
    if (x - a <= 6) return { type: 'trimL', i };
    if (b - x <= 6) return { type: 'trimR', i };
    return { type: 'clip', i };
  }
  return { type: 'empty' };
}

function drawTimeline() {
  if (!ed.loaded) return;
  syncVolUi();
  tlc.style.height = tl.clientHeight + 'px';
  tlc.style.width = tl.clientWidth + 'px';
  const { g, w, h } = fitCanvas(tlc);
  const z = ed.zoom, sx = tl.scrollLeft, t0 = sx / z, t1 = (sx + w) / z;
  const X = (t) => t * z - sx;
  const C = { muted: css('--muted'), violet: css('--violet'), mint: css('--mint'), pink: css('--pink'), amber: css('--amber'), panel: css('--panel'), panel2: css('--panel2'), text: css('--text'), onAccent: css('--on-accent') };
  $('#tlSpacer').style.width = Math.ceil(Math.max(total(), 1) * z + 300) + 'px';
  g.clearRect(0, 0, w, h);

  // regla
  g.fillStyle = C.panel2; g.fillRect(0, 0, w, RULER);
  const step = [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600].find((s) => s * z >= 80) || 600;
  g.font = '11px Inter'; g.fillStyle = C.muted; g.globalAlpha = 0.8;
  for (let t = Math.floor(t0 / step) * step; t <= t1; t += step) {
    const x = Math.round(X(t)) + 0.5;
    g.fillRect(x, RULER - 8, 1, 8);
    g.fillText(step < 1 ? fmt(t, 2) : fmt(t, 0), x + 4, 15);
    for (let k = 1; k < 4; k++) g.fillRect(Math.round(X(t + (step * k) / 4)), RULER - 4, 1, 4);
  }
  g.globalAlpha = 1;

  const { top, bot } = geo(), mid = (top + bot) / 2 + 8, hh = (bot - top) / 2 - 16;
  // hueco bajo el cursor (lo que borra D)
  if (clipAt(ed.ph) < 0) {
    const prevEnd = ed.clips.filter((c) => clipEnd(c) <= ed.ph + EPS).reduce((m, c) => Math.max(m, clipEnd(c)), 0);
    const next = ed.clips.filter((c) => c.t >= ed.ph - EPS).reduce((m, c) => Math.min(m, c.t), Infinity);
    if (next !== Infinity) { g.fillStyle = C.amber; g.globalAlpha = 0.12; g.fillRect(X(prevEnd), top, (next - prevEnd) * z, bot - top); g.globalAlpha = 1; }
  }
  ed.clips.forEach((c, i) => {
    const { a, b } = clipBox(c);
    if (b < 0 || a > w) return;
    const sel = i === ed.sel, accent = sel ? C.mint : C.violet;
    g.fillStyle = accent; g.globalAlpha = sel ? 0.24 : 0.15;
    g.beginPath(); g.roundRect(a + 1, top, Math.max(2, b - a - 2), bot - top, 10); g.fill();
    g.globalAlpha = 1; g.lineWidth = sel ? 2 : 1; g.strokeStyle = accent; g.stroke();
    if (i === ed.drop) { g.fillStyle = C.amber; g.globalAlpha = 0.3; g.fill(); g.globalAlpha = 1; g.strokeStyle = C.amber; g.lineWidth = 2; g.stroke(); }

    g.save(); g.beginPath(); g.rect(a + 2, top, Math.max(0, b - a - 4), bot - top); g.clip();
    const va = Math.max(a, 0), vb = Math.min(b, w);
    if (vb - va >= 1) {
      g.fillStyle = accent;
      drawWave(g, ed.data, ed.peaks, (c.s + (va - a) / z) * ed.sr, (c.s + (vb - a) / z) * ed.sr, Math.floor(va), Math.ceil(vb - va), mid, hh);
    }
    if (Math.abs(c.gain || 0) >= 0.05 && b - a > 70) {
      g.font = '700 11px Inter'; const txt = '🔊 ' + fmtDb(c.gain), tw = g.measureText(txt).width + 14;
      g.fillStyle = css('--mint'); g.globalAlpha = 0.9; g.beginPath(); g.roundRect(a + 8, bot - 26, tw, 18, 9); g.fill(); g.globalAlpha = 1;
      g.fillStyle = css('--on-accent'); g.fillText(txt, a + 15, bot - 13);
    }
    if (c.fx.length) {
      g.font = '600 11px Inter'; g.fillStyle = C.text;
      g.fillText(c.fx.map((f) => fxInfo(f.type).icon + ' ' + fxInfo(f.type).name).join('  ·  '), a + 10, top + 18);
    }
    if (b - a > 56) {
      const H = handles(c), f = fadeTarget(c), isFx = !!c.fx[c.edit], line = isFx ? C.pink : C.text;
      const x1 = a + f.fi * z, x2 = b - f.fo * z;
      // zona atenuada por los fades + línea de la envolvente
      g.fillStyle = '#000'; g.globalAlpha = 0.28;
      g.beginPath(); g.moveTo(a, H.hy); g.lineTo(x1, H.hy); g.lineTo(a, bot); g.closePath(); g.fill();
      g.beginPath(); g.moveTo(b, H.hy); g.lineTo(x2, H.hy); g.lineTo(b, bot); g.closePath(); g.fill();
      g.globalAlpha = 0.9; g.strokeStyle = line; g.lineWidth = 1.5;
      g.beginPath(); g.moveTo(a, f.fi > 0 ? bot : H.hy); g.lineTo(x1, H.hy); g.lineTo(x2, H.hy); g.lineTo(b, f.fo > 0 ? bot : H.hy); g.stroke();
      g.globalAlpha = 1;
      for (const hx of [H.inX, H.outX]) {
        g.fillStyle = C.panel; g.strokeStyle = line; g.lineWidth = 1.5;
        g.beginPath(); g.roundRect(hx - 5, H.hy - 5, 10, 10, 2); g.fill(); g.stroke();
      }
      const B = H.badge;
      g.fillStyle = c.fx.length ? C.violet : C.panel2;
      g.beginPath(); g.roundRect(B.x, B.y, B.w, B.h, 5); g.fill();
      g.fillStyle = c.fx.length ? C.onAccent : C.muted; g.font = 'italic 700 10.5px Inter';
      g.fillText('fx', B.x + 6, B.y + 11.5);
    }
    g.restore();
  });
  g.fillStyle = C.muted; g.globalAlpha = 0.4; g.fillRect(X(total()), RULER, 1, h - RULER); g.globalAlpha = 1;
  const px = Math.round(X(ed.ph));
  g.fillStyle = C.pink;
  g.fillRect(px - 1, 0, 2, h);
  g.beginPath(); g.moveTo(px - 7, 0); g.lineTo(px + 7, 0); g.lineTo(px + 7, 10); g.lineTo(px, 17); g.lineTo(px - 7, 10); g.fill();
  $('#edTime').textContent = `${fmt(ed.ph, 2)} / ${fmt(total(), 2)}`;
}

// ---------- ratón ----------
let scrub = false;
const canvasXY = (e) => { const b = tlc.getBoundingClientRect(); return [e.clientX - b.left, e.clientY - b.top]; };
tlc.onmousedown = (e) => {
  if (!ed.loaded || e.button !== 0) return;
  hideFxPop();
  const [x, y] = canvasXY(e), hit = hitTest(x, y), wasPlaying = ed.playing;
  edStop();
  if (hit.type === 'badge') { ed.sel = hit.i; drawTimeline(); return showFxPop(hit.i); }
  if (['fadeIn', 'fadeOut', 'trimL', 'trimR'].includes(hit.type)) {
    ed.sel = hit.i;
    const orig = JSON.stringify(ed.clips);
    snapshot();
    ed.drag = { type: hit.type, i: hit.i, x0: e.clientX, orig };
    return drawTimeline();
  }
  ed.ph = Math.min(Math.max(0, (x + tl.scrollLeft) / ed.zoom), total());
  if (hit.type !== 'ruler') ed.sel = hit.type === 'clip' ? hit.i : -1;
  scrub = true;
  drawTimeline();
  if (wasPlaying) edPlay();
};
// Arrastre de fades y bordes. Se recalcula siempre desde el estado inicial para que sea exacto.
function dragEdit(e) {
  const d = ed.drag, dt = (e.clientX - d.x0) / ed.zoom;
  ed.clips = JSON.parse(d.orig);
  const c = ed.clips[d.i], f = fadeTarget(c), L = clipLen(c);
  if (d.type === 'fadeIn') f.fi = clamp(f.fi + dt, 0, L - f.fo);
  else if (d.type === 'fadeOut') f.fo = clamp(f.fo - dt, 0, L - f.fi);
  else if (d.type === 'trimL') {
    // el borde sigue al ratón; al estirar contra el clip anterior empuja todo lo demás
    const prevEnd = ed.clips.filter((o) => o !== c && clipEnd(o) <= c.t + EPS).reduce((m, o) => Math.max(m, clipEnd(o)), 0);
    const ns = clamp(c.s + dt, 0, c.e - 0.05), oldT = c.t;
    c.t += ns - c.s;
    c.s = ns;
    if (c.t < prevEnd) { const push = prevEnd - c.t; c.t += push; shiftAfter(oldT, -push, c); }
  } else if (d.type === 'trimR') {
    const end = clipEnd(c);
    c.e = clamp(c.e + dt, c.s + 0.05, srcDur());
    const next = ed.clips.filter((o) => o !== c && o.t >= end - EPS).reduce((m, o) => Math.min(m, o.t), Infinity);
    if (clipEnd(c) > next) shiftAfter(end, next - clipEnd(c), c);
  }
  clampFades(c);
  ed.sel = d.i;
  sortClips();
  drawTimeline();
}
window.addEventListener('mousemove', (e) => {
  if (ed.drag) return dragEdit(e);
  if (scrub) {
    const [x] = canvasXY(e);
    ed.ph = clamp((x + tl.scrollLeft) / ed.zoom, 0, total());
    return drawTimeline();
  }
  if (e.target === tlc && ed.loaded) {
    const t = hitTest(...canvasXY(e)).type;
    tlc.style.cursor = t === 'trimL' || t === 'trimR' ? 'ew-resize' : t === 'fadeIn' || t === 'fadeOut' ? 'grab' : t === 'badge' ? 'pointer' : 'crosshair';
  }
});
window.addEventListener('mouseup', () => { scrub = false; if (ed.drag) { ed.drag = null; drawTimeline(); } });
tl.addEventListener('wheel', (e) => {
  e.preventDefault();
  if (e.ctrlKey) setZoom(ed.zoom * (e.deltaY < 0 ? 1.2 : 1 / 1.2), e.clientX - tl.getBoundingClientRect().left);
  else tl.scrollLeft += e.deltaY + e.deltaX;
}, { passive: false });

// ---------- guardar ----------
async function edSave(copy) {
  if (!ed.loaded) return;
  if (!ed.clips.length) return toast('No queda audio que guardar', true);
  edStop();
  const out = await render();
  try {
    const savedPath = await api.call('audio.save', ed.path, await api.toBase64(encodeWav(out, ed.sr)), !copy);
    const saved = savedPath.split(/[\\/]/).pop();
    ed.path = savedPath;
    toast(`💾 ${copy ? 'Copia guardada' : 'Guardado'} · ${fmt(out.length / ed.sr)}`);
    // lo guardado pasa a ser el nuevo original (efectos y fades ya aplicados)
    Object.assign(ed, { name: saved, data: out, peaks: buildPeaks(out), clips: [newClip(0, out.length / ed.sr, 0)], sel: -1, undo: [], redo: [], dirty: false });
    ed.ph = Math.min(ed.ph, total());
    ed.abuf = ctx().createBuffer(1, out.length, ed.sr);
    ed.abuf.copyToChannel(out, 0);
    $('#editName').textContent = saved.replace(/\.wav$/i, '');
    document.title = saved + ' — Editor de audio';
    drawTimeline();
  } catch (e) { toast('No se pudo guardar: ' + e.message, true); }
}
$('#edSave').onclick = () => edSave(false);
$('#edCopy').onclick = () => edSave(true);
$('#edUndo').onclick = () => undo(false);
$('#edRedo').onclick = () => undo(true);

// ---------- volumen de la voz ----------
// Con un clip seleccionado cambia ese clip; si no, todos. Un solo paso de deshacer por arrastre.
const volTargets = () => (ed.sel >= 0 ? [ed.clips[ed.sel]] : ed.clips);
function syncVolUi() {
  if (!ed.loaded) return;
  const t = volTargets(), db = t.length ? t[0].gain || 0 : 0;
  $('#edVol').value = db;
  $('#edVolVal').textContent = fmtDb(db);
  $('#volTarget').textContent = ed.sel >= 0 ? 'clip seleccionado' : 'todos los clips';
}
let volSnap = false;
$('#edVol').addEventListener('pointerdown', () => { if (ed.loaded && !volSnap) { snapshot(); volSnap = true; } });
$('#edVol').addEventListener('change', () => { volSnap = false; });
$('#edVol').addEventListener('input', (e) => {
  if (!ed.loaded) return;
  if (!volSnap) { snapshot(); volSnap = true; }
  const db = +e.target.value;
  for (const c of volTargets()) { c.gain = db; ed.levels?.get(c)?.gain.setTargetAtTime(dbToLin(db), ctx().currentTime, 0.02); }
  $('#edVolVal').textContent = fmtDb(db);
  drawTimeline();
});
$('#edVolReset').onclick = () => {
  if (!ed.loaded) return;
  snapshot();
  for (const c of volTargets()) { c.gain = 0; ed.levels?.get(c)?.gain.setTargetAtTime(1, ctx().currentTime, 0.02); }
  const box = $('.vol-box'); box.classList.remove('pop'); void box.offsetWidth; box.classList.add('pop');
  syncVolUi(); drawTimeline();
};

// ---------- exportar a Premiere Pro (XML de Final Cut Pro 7, sin renderizar) ----------
$('#edPremiere').onclick = async () => {
  if (!ed.loaded || !ed.clips.length) return toast('No hay clips que exportar', true);
  const clips = ed.clips.map((c) => ({ s: c.s, e: c.e, t: c.t, gain: dbToLin(c.gain), fi: c.vol.fi, fo: c.vol.fo }));
  try {
    const xml = buildPremiereXml({
      name: ed.name.replace(/\.[^.]+$/, ''), fps: 60, file: { path: ed.path, duration: srcDur() },
      video: null, audio: { rate: ed.info.rate || ed.sr, channels: ed.info.channels || 1 }, clips,
    });
    const file = await api.saveXml(ed.path.replace(/\.[^.]+$/, ' (Premiere).xml'), xml);
    if (!file) return;
    toast('⇪ Proyecto para Premiere guardado. En Premiere: Archivo › Importar');
    if (ed.clips.some((c) => c.fx.length)) setTimeout(() => toast('Los efectos de voz no pasan a Premiere: cortes, volumen y fades sí', true), 600);
  } catch (e) { toast('No se pudo exportar: ' + e.message.replace(/^.*Error: /, ''), true); }
};

function renderEdKeys() {
  $('#edKeys').innerHTML = ['split', 'del', 'closeGap', 'trimLeft', 'trimRight']
    .map((k) => `<div class="kb"><kbd>${keyLabel(S.keys[k])}</kbd><span>${KEY_INFO[k][0]}</span></div>`).join('') +
    '<div class="kb"><kbd>← →</kbd><span>Mover clip seleccionado</span></div><div class="kb"><kbd>Esc</kbd><span>Deseleccionar</span></div>' +
    '<div class="kb"><kbd>Espacio</kbd><span>Reproducir</span></div><div class="kb"><kbd>Ctrl+Z</kbd><span>Deshacer</span></div><div class="kb"><kbd>Ctrl+rueda</kbd><span>Zoom</span></div>';
}

// ================= TECLADO =================
document.addEventListener('keydown', (e) => {
  if (e.target.closest?.('input:not([type=range]), textarea, select') || $('.modal-bg')) return;
  const action = Object.keys(S.keys).find((k) => S.keys[k] === e.code);
  if (ed.loaded) {
    const k = e.code;
    if (e.ctrlKey && k === 'KeyZ') return undo(e.shiftKey);
    if (e.ctrlKey && k === 'KeyY') return undo(true);
    if (e.ctrlKey && k === 'KeyS') return edSave(false);
    if (e.ctrlKey) return;
    if (k === 'Space') { e.preventDefault(); return edToggle(); }
    if (k === 'Escape') { ed.sel = -1; hideFxPop(); return drawTimeline(); }
    if (k === 'Delete' || k === 'Backspace') return runEdOp('del');
    if ((k === 'ArrowLeft' || k === 'ArrowRight') && ed.sel >= 0) { e.preventDefault(); return moveClip(k === 'ArrowLeft' ? -1 : 1); }
    if (k === 'Home' || k === 'End' || k === 'ArrowLeft' || k === 'ArrowRight') {
      e.preventDefault();
      edStop();
      const d = e.shiftKey ? 1 : 0.1;
      ed.ph = k === 'Home' ? 0 : k === 'End' ? total() : Math.max(0, Math.min(total(), ed.ph + (k === 'ArrowLeft' ? -d : d)));
      followPlayhead(); return drawTimeline();
    }
    if (k === 'Equal' || k === 'NumpadAdd') return setZoom(ed.zoom * 1.25);
    if (k === 'Minus' || k === 'NumpadSubtract') return setZoom(ed.zoom / 1.25);
    if (action && KEY_INFO[action][1] === 'edit') { e.preventDefault(); runEdOp(action); }
  }
});


// ================= CUTLIFY =================
renderEdKeys();
const pickAudio = async () => { const f = await api.call('audio.pick'); if (f) openAudio(f); };
$('#edOpen').onclick = pickAudio;
$('#edOpen2').onclick = pickAudio;
$('#edRecorder').onclick = () => api.openRecorder();
api.on('open', (f) => openAudio(f));
window.addEventListener('themechange', () => drawTimeline());
window.MicEditor = { ed, open: openAudio, runEdOp, applyFx, save: edSave, total }; // para pruebas automáticas, como window.CaptEditor
