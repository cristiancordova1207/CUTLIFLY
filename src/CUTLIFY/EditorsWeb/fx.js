// Efectos de voz con Web Audio. Cada uno devuelve { input, output }; el mezclado seco/húmedo y los fades los hace el editor.
export const FX = [
  { id: 'underwater', name: 'Bajo el agua', icon: '🌊' },
  { id: 'echo', name: 'Eco', icon: '🏔️' },
  { id: 'room', name: 'Sala', icon: '🛋️' },
  { id: 'cave', name: 'Cueva', icon: '🦇' },
  { id: 'robot', name: 'Robot', icon: '🤖' },
  { id: 'alien', name: 'Alien', icon: '👽' },
  { id: 'phone', name: 'Teléfono', icon: '📞' },
  { id: 'radio', name: 'Radio', icon: '📻' },
  { id: 'megaphone', name: 'Megáfono', icon: '📢' },
  { id: 'distortion', name: 'Distorsión', icon: '🔥' },
  { id: 'lofi', name: 'Lo-fi', icon: '📼' },
  { id: 'tremolo', name: 'Trémolo', icon: '〰️' },
  { id: 'vibrato', name: 'Vibrato', icon: '🎻' },
  { id: 'chorus', name: 'Coro', icon: '👥' },
  { id: 'flanger', name: 'Flanger', icon: '🌀' },
  { id: 'ghost', name: 'Fantasma', icon: '👻' },
];
export const fxInfo = (id) => FX.find((f) => f.id === id) || { name: id, icon: '✦' };

const filt = (ac, type, freq, Q = 0.7) => { const n = ac.createBiquadFilter(); n.type = type; n.frequency.value = freq; n.Q.value = Q; return n; };
const gain = (ac, v) => { const g = ac.createGain(); g.gain.value = v; return g; };
const delay = (ac, t) => { const d = ac.createDelay(1); d.delayTime.value = t; return d; };
const shaper = (ac, curve) => { const n = ac.createWaveShaper(); n.curve = curve; n.oversample = '4x'; return n; };
const distCurve = (k) => Float32Array.from({ length: 2048 }, (_, i) => { const x = (i / 1024) - 1; return ((1 + k) * x) / (1 + k * Math.abs(x)); });
const crushCurve = (steps) => Float32Array.from({ length: 2048 }, (_, i) => Math.round(((i / 1024) - 1) * steps) / steps);
function chain(...n) { for (let i = 0; i < n.length - 1; i++) n[i].connect(n[i + 1]); return { input: n[0], output: n.at(-1) }; }
function lfo(ac, freq, depth, param, oscs) {
  const o = ac.createOscillator(); o.frequency.value = freq;
  o.connect(gain(ac, depth)).connect(param); o.start(); oscs.push(o);
}
// Reverb con ruido pseudoaleatorio fijo: suena igual al reproducir que al guardar.
function impulse(ac, secs, decay) {
  const len = Math.floor(ac.sampleRate * secs), b = ac.createBuffer(2, len, ac.sampleRate);
  let seed = 7;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647) * 2 - 1;
  for (let c = 0; c < 2; c++) { const d = b.getChannelData(c); for (let i = 0; i < len; i++) d[i] = rnd() * Math.pow(1 - i / len, decay); }
  return b;
}
function reverb(ac, secs, decay, wet) {
  const input = gain(ac, 1), output = gain(ac, 1), conv = ac.createConvolver();
  conv.buffer = impulse(ac, secs, decay);
  input.connect(output);
  input.connect(conv).connect(gain(ac, wet)).connect(output);
  return { input, output };
}
function ring(ac, freq, oscs) {
  const g = gain(ac, 0);
  lfo(ac, freq, 1, g.gain, oscs);
  return g;
}

export function makeFx(ac, id, oscs) {
  switch (id) {
    case 'underwater': {
      const f = filt(ac, 'lowpass', 420, 1.2), d = delay(ac, 0.01);
      lfo(ac, 0.8, 0.004, d.delayTime, oscs);
      return chain(f, d, gain(ac, 1.5));
    }
    case 'echo': {
      const input = gain(ac, 1), output = gain(ac, 1), d = delay(ac, 0.32), fb = gain(ac, 0.45);
      input.connect(output); input.connect(d); d.connect(fb).connect(d); d.connect(gain(ac, 0.7)).connect(output);
      return { input, output };
    }
    case 'room': return reverb(ac, 1.2, 3, 0.6);
    case 'cave': return reverb(ac, 3.5, 2, 1.1);
    case 'robot': {
      const r = ring(ac, 48, oscs), d = delay(ac, 0.011), fb = gain(ac, 0.55), out = gain(ac, 1.6);
      r.connect(d); d.connect(fb).connect(d); r.connect(out); d.connect(out);
      return { input: r, output: out };
    }
    case 'alien': {
      const d = delay(ac, 0.006);
      lfo(ac, 6, 0.003, d.delayTime, oscs);
      return chain(d, ring(ac, 420, oscs), gain(ac, 1.6));
    }
    case 'phone': return chain(filt(ac, 'highpass', 500), filt(ac, 'lowpass', 3200), shaper(ac, distCurve(6)), gain(ac, 0.9));
    case 'radio': return chain(filt(ac, 'bandpass', 1700, 1.1), shaper(ac, distCurve(30)), filt(ac, 'highpass', 300), gain(ac, 0.8));
    case 'megaphone': return chain(filt(ac, 'highpass', 900), shaper(ac, distCurve(70)), filt(ac, 'lowpass', 4500), gain(ac, 0.6));
    case 'distortion': return chain(shaper(ac, distCurve(150)), filt(ac, 'lowpass', 6000), gain(ac, 0.5));
    case 'lofi': return chain(shaper(ac, crushCurve(8)), filt(ac, 'lowpass', 3000), gain(ac, 1));
    case 'tremolo': { const g = gain(ac, 0.5); lfo(ac, 7, 0.5, g.gain, oscs); return chain(g); }
    case 'vibrato': { const d = delay(ac, 0.006); lfo(ac, 5.5, 0.003, d.delayTime, oscs); return chain(d); }
    case 'chorus': {
      const input = gain(ac, 1), output = gain(ac, 0.8);
      input.connect(output);
      for (const [t, f] of [[0.022, 0.6], [0.031, 0.9]]) { const d = delay(ac, t); lfo(ac, f, 0.004, d.delayTime, oscs); input.connect(d).connect(gain(ac, 0.6)).connect(output); }
      return { input, output };
    }
    case 'flanger': {
      const input = gain(ac, 1), output = gain(ac, 0.8), d = delay(ac, 0.004), fb = gain(ac, 0.6);
      lfo(ac, 0.25, 0.003, d.delayTime, oscs);
      input.connect(output); input.connect(d); d.connect(fb).connect(d); d.connect(output);
      return { input, output };
    }
    case 'ghost': {
      const d = delay(ac, 0.008), hp = filt(ac, 'highpass', 350), rv = reverb(ac, 3, 2.5, 1.4);
      lfo(ac, 3, 0.004, d.delayTime, oscs);
      hp.connect(d).connect(rv.input);
      return { input: hp, output: rv.output };
    }
    default: { const g = gain(ac, 1); return { input: g, output: g }; }
  }
}
