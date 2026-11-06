// Puente con CUTLIFY (WebView2) y utilidades compartidas por los editores de audio y vídeo.
// Sustituye al preload de Electron de MicLY/CaptLY: window.api llama al proceso de CUTLIFY por postMessage.
(() => {
  const host = window.chrome?.webview;
  const pending = new Map(), listeners = {};
  let seq = 0;
  const call = (cmd, ...args) => new Promise((resolve, reject) => {
    const id = ++seq;
    pending.set(id, { resolve, reject });
    host.postMessage({ id, cmd, args });
  });
  const on = (ev, fn) => (listeners[ev] ??= []).push(fn);
  host?.addEventListener('message', (e) => {
    const m = e.data;
    if (m.event) return (listeners[m.event] || []).forEach((fn) => fn(m.data));
    const p = pending.get(m.id);
    if (!p) return;
    pending.delete(m.id);
    m.error != null ? p.reject(new Error(m.error)) : p.resolve(m.result);
  });

  // ArrayBuffer → base64 sin bloquear (para guardar el WAV editado)
  const toBase64 = (buf) => new Promise((res) => {
    const r = new FileReader();
    r.onload = () => res(String(r.result).split(',')[1] || '');
    r.readAsDataURL(new Blob([buf]));
  });
  const fetchBuf = async (url) => { const r = await fetch(url); if (!r.ok) throw new Error('No se pudo leer el archivo'); return r.arrayBuffer(); };

  window.api = {
    call, on, toBase64, fetchBuf,
    openRecorder: () => call('openRecorder'),
    saveXml: (defaultPath, xml) => call('saveXml', defaultPath, xml),
    // editor de vídeo (mismas funciones que el preload de CaptLY)
    editor: {
      pick: () => call('video.pick'),
      probe: (file) => call('video.probe', file),
      thumbs: (file, duration) => call('video.thumbs', file, duration),
      wave: async (file) => fetchBuf(await call('video.wave', file)),
      premiere: async (info, clips) => {
        const xml = buildPremiereXml({
          name: info.name.replace(/\.[^.]+$/, ''), fps: info.fps, file: { path: info.path, duration: info.duration },
          video: { width: info.width, height: info.height }, audio: info.audio, clips,
        });
        return call('saveXml', info.path.replace(/\.[^.]+$/, ' (Premiere).xml'), xml);
      },
      render: (info, clips) => call('video.render', info, clips),
      onProgress: (fn) => on('editor-progress', fn),
    },
  };

  // Tema: CUTLIFY manda sus colores (estilo + claro/noche) y la página los aplica al vuelo.
  on('theme', (t) => {
    const root = document.documentElement.style;
    for (const [k, v] of Object.entries(t.vars)) root.setProperty(k, v);
    document.documentElement.dataset.mode = t.dark ? 'dark' : 'light';
    document.body.classList.add('ready');
    window.dispatchEvent(new Event('themechange'));
  });
  setTimeout(() => document.body.classList.add('ready'), 1500); // por si el tema tarda

  // Botones "slime": se aplastan, rebotan y sueltan una gota al pulsarlos (como en MicLY).
  document.addEventListener('pointerdown', (e) => {
    const b = e.target.closest?.('button, .fx-chip');
    if (!b || b.disabled) return;
    b.classList.remove('squish'); void b.offsetWidth; b.classList.add('squish');
    if (b.tagName !== 'BUTTON') return;
    const r = b.getBoundingClientRect(), d = document.createElement('i');
    d.className = 'goo';
    d.style.left = e.clientX - r.left + 'px'; d.style.top = e.clientY - r.top + 'px';
    b.append(d);
    setTimeout(() => d.remove(), 700);
  });
  document.addEventListener('animationend', (e) => { if (e.animationName === 'squish') e.target.classList.remove('squish'); });
  // Que el WebView no abra el archivo si se suelta uno encima
  window.addEventListener('dragover', (e) => { if (!e.dataTransfer.types.includes('text/fx')) e.preventDefault(); });
  window.addEventListener('drop', (e) => { if (!e.dataTransfer.types.includes('text/fx')) e.preventDefault(); });
})();

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const fmt = (t) => { t = Math.max(0, Math.floor(t || 0)); const h = Math.floor(t / 3600), m = Math.floor(t / 60) % 60, s = t % 60; return (h ? h + ':' : '') + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0'); };
function toast(msg, err) {
  const t = document.createElement('div');
  t.className = 'toast' + (err ? ' err' : '');
  t.textContent = msg;
  $('#toasts').append(t);
  setTimeout(() => t.remove(), err ? 6000 : 2600);
}
function modal(title, { ok = 'Aceptar' } = {}) {
  return new Promise((res) => {
    const bg = document.createElement('div');
    bg.className = 'modal-bg';
    bg.innerHTML = '<div class="modal"><h3></h3><div class="row"><button class="ghost sm" data-x>Cancelar</button><button class="primary sm" data-ok></button></div></div>';
    $('h3', bg).textContent = title;
    $('[data-ok]', bg).textContent = ok;
    const done = (v) => { bg.remove(); res(v); };
    $('[data-x]', bg).onclick = () => done(false);
    $('[data-ok]', bg).onclick = () => done(true);
    bg.onkeydown = (e) => { e.stopPropagation(); if (e.key === 'Enter') done(true); if (e.key === 'Escape') done(false); };
    bg.onclick = (e) => e.target === bg && done(false);
    document.body.append(bg);
    $('[data-ok]', bg).focus();
  });
}
