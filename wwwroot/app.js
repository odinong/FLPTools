'use strict';

// ------------------------------------------------------------------ bridge
const host = window.chrome && window.chrome.webview;
const waiting = new Map();
const listeners = {};
let seq = 0;

function api(cmd, args = {}) {
  if (!host) return mockApi(cmd, args);
  return new Promise((resolve, reject) => {
    const id = ++seq;
    waiting.set(id, { resolve, reject });
    host.postMessage(JSON.stringify({ id, cmd, args }));
  });
}
if (host) {
  host.addEventListener('message', e => {
    const m = e.data;
    if (m && m.event) return (listeners[m.event] || []).forEach(f => f(m));
    const w = waiting.get(m.id);
    if (!w) return;
    waiting.delete(m.id);
    m.ok ? w.resolve(m.result) : w.reject(new Error(m.error));
  });
}
const on = (ev, f) => (listeners[ev] = listeners[ev] || []).push(f);

// ------------------------------------------------------------------ dom helpers
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
function h(tag, attrs, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'style') for (const [sk, sv] of Object.entries(v)) { if (sv == null) continue; sk.startsWith('--') ? el.style.setProperty(sk, sv) : (el.style[sk] = sv); }
    else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'html') el.innerHTML = v;
    else if (k in el) el[k] = v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const c of kids.flat(Infinity)) if (c != null && c !== false) el.append(c.nodeType ? c : document.createTextNode(c));
  return el;
}
const icon = (name, cls) => { const t = document.createElement('template'); t.innerHTML = `<svg viewBox="0 0 24 24"${cls ? ` class="${cls}"` : ''}><path fill-rule="evenodd" d="${ICONS[name] || ''}"/></svg>`; return t.content.firstChild; };
const fileName = p => (p || '').split(/[\\/]/).pop();
const baseName = p => fileName(p).replace(/\.[^.]+$/, '');
const dirName = p => (p || '').replace(/[\\/][^\\/]*$/, '');

const ICONS = {
  projects: 'M3 6.5A1.5 1.5 0 0 1 4.5 5h5l2 2.5h8A1.5 1.5 0 0 1 21 9v9.5a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z',
  convert: 'M4 7h11V4l5 4.5-5 4.5V10H4zM20 17H9v3l-5-4.5L9 11v3h11z',
  mods: 'M10 3.5a2 2 0 0 1 4 0V5h4a1 1 0 0 1 1 1v4h-1.5a2 2 0 1 0 0 4H19v4a1 1 0 0 1-1 1h-4v-1.5a2 2 0 1 0-4 0V19H6a1 1 0 0 1-1-1v-4h1.5a2 2 0 1 0 0-4H5V6a1 1 0 0 1 1-1h4z',
  themes: 'M12 3a9 9 0 0 0 0 18c1.1 0 1.7-.8 1.7-1.6 0-1.2-1.1-1.6-1.1-2.7 0-.9.7-1.6 1.6-1.6H17a4 4 0 0 0 4-4C21 6.4 17 3 12 3zM7.5 12.5a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm3-4a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm5 0a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z',
  configs: 'M4 6h9v2H4zM17 6h3v2h-3zM13 4h4v6h-4zM4 16h3v2H4zM11 16h9v2h-9zM7 14h4v6H7z',
  settings: 'M10.6 2h2.8l.5 2.6 1.7.7 2.2-1.5 2 2-1.5 2.2.7 1.7 2.6.5v2.8l-2.6.5-.7 1.7 1.5 2.2-2 2-2.2-1.5-1.7.7-.5 2.6h-2.8l-.5-2.6-1.7-.7-2.2 1.5-2-2 1.5-2.2-.7-1.7L2 13.4v-2.8l2.6-.5.7-1.7-1.5-2.2 2-2 2.2 1.5 1.7-.7zM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z',
  power: 'M11 3h2v9h-2zM7.1 5.6l1.4 1.4A6 6 0 1 0 15.5 7l1.4-1.4A8 8 0 1 1 7.1 5.6z',
  plus: 'M11 4h2v7h7v2h-7v7h-2v-7H4v-2h7z',
  check: 'M9.5 16.2 5.3 12l-1.4 1.4 5.6 5.6L20.1 8.4 18.7 7z',
  alert: 'M11 5h2v9h-2zM11 16h2v2h-2z',
};

// ------------------------------------------------------------------ sound
const sfx = (() => {
  let ctx;
  function click(freq, len, vol, delay = 0) {
    if (!state.settings.sounds) return;
    ctx = ctx || new AudioContext();
    const t = ctx.currentTime + delay, o = ctx.createOscillator(), g = ctx.createGain();
    o.frequency.value = freq;
    g.gain.setValueAtTime(vol, t);
    g.gain.exponentialRampToValueAtTime(0.0001, t + len);
    o.connect(g).connect(ctx.destination);
    o.start(t);
    o.stop(t + len + 0.01);
  }
  return {
    move: () => click(1900, 0.03, 0.025),
    edge: () => click(260, 0.06, 0.035),
    ok: () => { click(1250, 0.05, 0.035); click(1660, 0.07, 0.03, 0.05); },
    back: () => click(950, 0.05, 0.03),
    start: () => { click(660, 0.12, 0.035); click(990, 0.16, 0.03, 0.09); click(1320, 0.3, 0.025, 0.18); },
    boot: () => { click(523, 0.5, 0.02, 0.15); click(784, 0.6, 0.018, 0.32); click(1046, 0.9, 0.016, 0.5); },
    bad: () => click(190, 0.14, 0.045),
  };
})();

// ------------------------------------------------------------------ state
const state = {
  installs: [], configs: [], cores: 8,
  settings: { sounds: true, animations: true, userName: '', userColor: '#2d7ff9', minimizeOnLaunch: true },
  tab: 'fl', sel: { fl: 0, tools: 0 }, zone: 'rail', heroBtn: 0, card: 0, tabFocus: 0,
  page: null, running: new Set(), bgUrl: '',
};
const VERSION_COLORS = { 21: '#c76b22', 24: '#c76b22', 25: '#2369b8', 26: '#b83a4b', 27: '#3a9a58' };
const COLORS = ['#2369b8', '#b83a4b', '#c76b22', '#3a9a58', '#6c55c4', '#c49a1e', '#c4508e', '#23918c', '#4a4f5a', '#8a8d94'];
const TOOLS = [
  { id: 'projects', label: 'Projects', color: '#b07a26', desc: 'Change a project\'s info, channel, pattern and mixer names and colours. Fix sample paths, edit notes, export MIDI.' },
  { id: 'convert', label: 'Converter', color: '#2369b8', desc: 'Make FL 26 projects open in FL 25. Turn projects into JSON and back, or run a job on a whole folder.' },
  { id: 'mods', label: 'Mods', color: '#3a9a58', desc: 'Piano roll and Edison scripts for FL, and project templates.' },
  { id: 'themes', label: 'Themes', color: '#b04780', desc: 'Make FL themes and note colour presets, or look through the ones you already have.' },
  { id: 'configs', label: 'Configs', color: '#5c48b0', desc: 'Every launch config for every FL version in one list.' },
  { id: 'settings', label: 'Settings', color: '#4a4f5a', desc: 'Your name and colour, sounds, FL installs and folders.' },
  { id: 'power', label: 'Exit', color: '#9e3434', desc: 'Close FLPTools. FL Studio keeps running.' },
];
const DOCK = TOOLS;
const installOf = c => state.installs.find(i => i.id === c.install);
const colorOf = c => c.accent || VERSION_COLORS[(installOf(c) || {}).major] || '#4a4f5a';
const titleOf = c => { const i = installOf(c); return i ? `FL Studio ${i.year}` : c.install; };
const items = () => state.tab === 'fl' ? [...state.configs, { add: true }] : TOOLS;
const current = () => items()[state.sel[state.tab]];

function toast(text, bad = false) {
  const s = String(text);
  const cut = s.search(/[.!?]\s/);
  const title = cut > 0 ? s.slice(0, cut + 1) : s, body = cut > 0 ? s.slice(cut + 2) : '';
  const t = h('div', { class: 'toast' },
    h('div', { class: 'ti' + (bad ? ' bad' : '') }, icon(bad ? 'alert' : 'check')),
    h('div', { class: 'tt' }, h('b', {}, title.replace(/\.$/, '')), body ? h('span', {}, body) : null));
  $('#toasts').prepend(t);
  while ($('#toasts').children.length > 3) $('#toasts').lastChild.remove();
  if (bad) sfx.bad();
  setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 260); }, bad ? 6000 : 3200);
}
async function run(fn, done) {
  try {
    const r = await fn();
    if (done) { toast(typeof done === 'function' ? done(r) : done); sfx.ok(); }
    return r;
  } catch (e) { toast(e.message || String(e), true); }
}

// ------------------------------------------------------------------ dialogs
function dialog(build) {
  return new Promise(resolve => {
    const m = $('#modal');
    const close = v => { m.hidden = true; m.innerHTML = ''; dialog.close = null; resolve(v); };
    m.innerHTML = '';
    m.append(build(close));
    m.hidden = false;
    m.onclick = e => { if (e.target === m) close(undefined); };
    dialog.close = close;
    setTimeout(() => ($('input', m) || $('.btn.main', m) || $('button', m)).focus(), 20);
  });
}
const ask = (title, text, yes = 'OK', red = false) => dialog(close => h('div', { class: 'dialog' },
  h('div', { class: 'body' }, h('h3', {}, title), text ? h('p', {}, text) : null),
  h('div', { class: 'acts' }, h('button', { class: 'btn', onclick: () => close(false) }, 'Cancel'), h('button', { class: 'btn main' + (red ? ' red' : ''), onclick: () => close(true) }, yes))));
const askText = (title, value = '') => dialog(close => {
  const inp = h('input', { class: 'input', value, onkeydown: e => e.key === 'Enter' && close(inp.value) });
  return h('div', { class: 'dialog' }, h('div', { class: 'body' }, h('h3', {}, title), inp),
    h('div', { class: 'acts' }, h('button', { class: 'btn', onclick: () => close(null) }, 'Cancel'), h('button', { class: 'btn main', onclick: () => close(inp.value) }, 'OK')));
});
const pickFrom = (title, list) => dialog(close => h('div', { class: 'dialog' },
  h('div', { class: 'body' }, h('h3', {}, title)),
  h('div', { class: 'list' }, list.map(it => h('div', { class: 'r click', onclick: () => close(it.value) },
    it.color ? h('span', { style: { width: '16px', height: '16px', borderRadius: '4px', background: it.color, flex: 'none' } }) : null,
    h('div', { class: 'l' }, h('div', { class: 't' }, it.label), it.sub ? h('div', { class: 'd' }, it.sub) : null)))),
  h('div', { class: 'acts' }, h('button', { class: 'btn', onclick: () => close(undefined) }, 'Cancel'))));
const showText = (title, text) => dialog(close => h('div', { class: 'dialog', style: { width: 'min(920px, 92vw)' } },
  h('div', { class: 'body' }, h('h3', {}, title), h('div', { class: 'log', style: { maxHeight: '62vh', userSelect: 'text', marginTop: '12px' } }, text)),
  h('div', { class: 'acts' }, h('button', { class: 'btn main', onclick: () => close() }, 'Close'))));

const FILTERS = {
  flp: 'FL Studio project (*.flp)|*.flp|All files (*.*)|*.*',
  json: 'JSON (*.json)|*.json|All files (*.*)|*.*',
  image: 'Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|All files (*.*)|*.*',
  theme: 'FL theme (*.flstheme)|*.flstheme|All files (*.*)|*.*',
  mid: 'MIDI (*.mid)|*.mid',
};
const openFile = (f, multi = false) => api('dialog.open', { filter: FILTERS[f], multi });
const saveFile = (f, name = '') => api('dialog.save', { filter: FILTERS[f], name });
const openFolder = () => api('dialog.folder');

// ------------------------------------------------------------------ backdrop
const imageCache = {};
async function imageUrl(src) {
  if (!src) return '';
  if (/^(https?|data):/.test(src)) return src;
  if (!(src in imageCache)) imageCache[src] = await api('image', { path: src }).catch(() => '');
  return imageCache[src];
}
// A config's backdrop is "anim:<mode>" (waves, particles, flow), a picture URL or path, or empty for animated waves.
const ANIMS = [['waves', 'Waves'], ['particles', 'Particles'], ['flow', 'Flow'], ['orbs', 'Orbs'], ['aurora', 'Aurora']];
let fx = null;
function backdropOf(it) {
  if (state.tab === 'tools') return { src: 'anim:particles', color: '#24406b' };
  if (!it || it.add) return { src: 'anim:waves', color: '#3a3f4b' };
  return { src: it.background || 'anim:waves', color: colorOf(it) };
}
async function setBackdrop({ src, color }) {
  const anim = src.startsWith('anim:');
  document.body.classList.toggle('anim', anim);
  const [a, b] = $$('#bg .layer');
  if (anim) {
    if (!fx) fx = createFx($('#fx'), { animate: () => state.settings.animations !== false, interactive: true });
    if (fx) fx.set(src.slice(5), color);
    $('#fx').classList.add('show');
    a.classList.remove('show'); b.classList.remove('show');
    state.bgUrl = src;
    return;
  }
  const url = await imageUrl(src);
  $('#fx').classList.remove('show');
  if (url === state.bgUrl && (a.classList.contains('show') || b.classList.contains('show'))) return;
  state.bgUrl = url;
  const next = a.classList.contains('show') ? b : a, prev = next === a ? b : a;
  next.style.backgroundImage = url ? `url("${url}")` : 'none';
  next.style.backgroundColor = url ? '' : '#14161c';
  next.classList.add('show');
  prev.classList.remove('show');
}

// ------------------------------------------------------------------ start
async function start() {
  setupTitlebar();
  apply(await api('init'));
  document.body.classList.add('intro');
  $('#app').hidden = false;
  drawHome(true);
  sfx.boot();
  setTimeout(() => document.body.classList.remove('intro'), 1900);
  let hiddenAt = 0;
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { hiddenAt = performance.now(); return; }
    if (state.page || performance.now() - hiddenAt < 1500) return;
    document.body.classList.remove('wake'); void document.body.offsetWidth;
    document.body.classList.add('wake');
    if (fx) fx.surge(0.35), setTimeout(() => fx && fx.surge(0), 450);
    setTimeout(() => document.body.classList.remove('wake'), 1000);
  });
  setInterval(clock, 1000);
  clock();
  setInterval(poll, 4000);
  requestAnimationFrame(gamepad);
}

function apply(d) {
  state.installs = d.installs;
  state.configs = d.configs;
  Object.assign(state.settings, d.settings || {});
  state.cores = d.cores || state.cores;
  document.documentElement.classList.toggle('no-anim', state.settings.animations === false);
  state.sel.fl = Math.max(0, Math.min(state.sel.fl, state.configs.length));
}

async function poll() {
  if (state.page || document.hidden) return;
  const d = await api('init').catch(() => null);
  if (!d) return;
  const was = state.installs.map(i => i.running).join();
  state.installs = d.installs;
  if (was !== d.installs.map(i => i.running).join()) { drawRail(false); drawTop(); }
}

// ------------------------------------------------------------------ title bar
function setupTitlebar() {
  const glyph = { min: '\uE921', max: '\uE922', restore: '\uE923', close: '\uE8BB' };
  const max = h('button', { class: 'wb', title: 'Maximize', onclick: () => api('app.maximize') }, glyph.max);
  $('#titlebar').replaceChildren(
    h('div', { class: 'drag', onmousedown: e => { if (e.button === 0 && e.detail === 1) api('app.drag'); }, ondblclick: () => api('app.maximize') }),
    h('div', { class: 'wbtns' },
      h('button', { class: 'wb', title: 'Minimize', onclick: () => api('app.minimize') }, glyph.min),
      max,
      h('button', { class: 'wb close', title: 'Close', onclick: () => api('app.close') }, glyph.close)));
  on('window', m => {
    max.textContent = m.maximized ? glyph.restore : glyph.max;
    max.title = m.maximized ? 'Restore' : 'Maximize';
    document.body.classList.toggle('maxed', !!m.maximized);
  });
}

function clock() {
  $('#clock').textContent = new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

// ------------------------------------------------------------------ home
function drawHome(first = false) {
  drawTop();
  drawRail(first);
  const k = (key, text) => h('span', { class: 'key' }, h('span', { class: 'kbd' }, key), text);
  $('#hints').replaceChildren(k('Enter', 'Select'), k('O', 'Options'), k('Q / E', 'Switch tab'), k('Esc', 'Back'));
  setZone(state.zone, true);
}

function drawTop() {
  $('#tabs').replaceChildren(...[['fl', 'FL Studio'], ['tools', 'Tools']].map(([id, label], i) =>
    h('button', { class: state.tab === id ? 'on' : '', onclick: () => switchTab(id) }, label)));
  const r = state.installs.filter(i => i.running);
  $('#runNote').textContent = r.length ? r.map(i => `FL Studio ${i.year}`).join(', ') + ' running' : '';
  document.body.classList.toggle('tools', state.tab === 'tools');
}

function switchTab(id) {
  if (state.tab === id) return;
  state.tab = id;
  sfx.move();
  drawTop();
  drawRail(true);
  if (state.zone !== 'tabs') setZone('rail', true);
}

function tileFor(item) {
  if (item.add) return h('div', { class: 'tile', style: { '--tc': 'rgba(255,255,255,.12)' } }, icon('plus', 'g'));
  if (!item.install) return h('div', { class: 'tile', style: { '--tc': item.color } }, icon(item.id, 'g'));
  const inst = installOf(item);
  const t = h('div', { class: 'tile', style: { '--tc': colorOf(item) } });
  if (item.image) imageUrl(item.image).then(u => { if (u) t.style.backgroundImage = `url("${u}")`; });
  else if (inst && inst.icon) t.append(h('img', { src: inst.icon, alt: '' }));
  t.append(h('span', { class: 'run' }));
  if (item.tag) t.append(h('span', { class: 'tag' }, item.tag));
  if (inst && inst.files === 'modified') t.append(h('span', { class: 'mod', title: inst.filesDetail }, 'Modified'));
  if (!inst) t.classList.add('missing');
  const runningHere = state.running.has(item.id);
  const someoneElse = [...state.running].some(id => (state.configs.find(x => x.id === id) || {}).install === item.install);
  if (runningHere || (inst && inst.running && !someoneElse && state.configs.find(x => x.install === item.install) === item)) t.classList.add('running');
  return t;
}

function labelOf(item) {
  if (item.add) return 'New config';
  if (!item.install) return item.label;
  return item.name && item.name !== 'Default' ? `${titleOf(item)}: ${item.name}` : titleOf(item);
}

function drawRail(animate) {
  const rail = $('#rail');
  rail.classList.toggle('static', !animate);
  rail.replaceChildren(...items().map((it, i) => {
    const slot = h('div', { class: 'slot', style: { '--i': i } }, tileFor(it), h('span', { class: 'name' }, labelOf(it)));
    slot.firstChild.onclick = () => {
      if (state.zone === 'rail' && state.sel[state.tab] === i) activate();
      else { setZone('rail', true); select(i); }
    };
    slot.firstChild.oncontextmenu = e => { e.preventDefault(); select(i, true); options(); };
    return slot;
  }));
  select(state.sel[state.tab], true);
}

function select(i, quiet = false) {
  const slots = $$('.slot', $('#rail'));
  if (i < 0 || i >= slots.length) { if (!quiet) sfx.edge(); return; }
  if (!quiet && i !== state.sel[state.tab]) sfx.move();
  state.sel[state.tab] = i;
  slots.forEach((s, k) => s.classList.toggle('sel', k === i));
  const rail = $('#rail'), s = slots[i];
  const target = Math.max(0, s.offsetLeft - 80);
  rail.scrollTo({ left: i <= 1 ? 0 : target - 118, behavior: quiet ? 'auto' : 'smooth' });
  if (fx) fx.shift(i * 0.09);
  if (!quiet) {
    const tile = s.firstChild;
    tile.classList.remove('sweep'); void tile.offsetWidth; tile.classList.add('sweep');
    setTimeout(() => { const r = tile.getBoundingClientRect(); if (fx) fx.pulse(r.left + r.width / 2, r.top + r.height / 2); }, 60);
  }
  drawHero();
}

async function drawHero() {
  const it = current();
  const hero = $('#hero');
  const cards = $('#cards');
  hero.replaceChildren();
  cards.replaceChildren();
  state.card = 0;
  const btn = (label, fn) => h('button', { class: 'play', onclick: fn }, label);
  const more = h('button', { class: 'more', title: 'Options', onclick: () => options() }, h('i', {}, h('b'), h('b'), h('b')));
  if (it.add) {
    hero.append(h('div', { class: 'k' }, 'Launch config'), h('h1', {}, 'New config'),
      h('div', { class: 's' }, 'Another way to start one of your FL versions, with its own project, theme, template and settings.'),
      h('div', { class: 'acts' }, btn('Create', () => newConfig())));
  } else if (!it.install) {
    hero.append(h('div', { class: 'k' }, 'Tools'), h('h1', {}, it.label), h('div', { class: 's' }, it.desc),
      h('div', { class: 'acts' }, btn(it.id === 'power' ? 'Close' : 'Open', () => activate())));
    if (it.id === 'projects') {
      const list = (await api('recent.all').catch(() => [])).filter(p => p.exists).slice(0, 6);
      if (current() !== it) return;
      cards.replaceChildren(...list.map((p, i) => card(p, i, 'Recent project', () => openPage('projects', p.path))));
    }
  } else {
    const inst = installOf(it);
    const info = [];
    if (inst) info.push(inst.version);
    info.push(playtime(it.playSeconds));
    if (it.lastPlayed) info.push(ago(it.lastPlayed));
    if (it.project) info.push('Opens ' + baseName(it.project));
    if (inst && inst.files === 'signed') info.push('Files signed by Image-Line');
    hero.append(h('div', { class: 'k' },
        inst && inst.files === 'modified' ? h('span', { class: 'ktag bad', title: inst.filesDetail }, 'Modified files') : null,
        it.tag ? h('span', { class: 'ktag' }, it.tag) : null, it.name && it.name !== 'Default' ? it.name : 'Default config'),
      h('h1', {}, titleOf(it)),
      h('div', { class: 'acts' }, btn(inst ? 'Start' : 'Not installed', () => launch(it)), more),
      h('div', { class: 'info' }, info.map(x => h('span', {}, x))));
    if (inst) {
      const list = (await api('recent', { install: inst.id }).catch(() => [])).slice(0, 6);
      if (current() !== it) return;
      cards.replaceChildren(...list.map((p, i) => card(p, i, 'Open with this config', () => launch(it, p.path))));
    }
  }
  setBackdrop(backdropOf(it));
  setZone(state.zone, true);
}

function card(p, i, kicker, fn) {
  const c = h('div', { class: 'card' + (p.exists ? '' : ' gone'), style: { '--i': i }, title: p.path },
    h('div', { class: 'ck' }, kicker), h('div', { class: 'cn' }, p.name), h('div', { class: 'cd' }, p.modified || 'Missing'));
  c.onclick = () => p.exists ? fn() : toast('That file is gone.', true);
  return c;
}

function setZone(zone, quiet = false) {
  if (zone === 'cards' && !$$('.card').length) zone = 'hero';
  state.zone = zone;
  $('#app').classList.toggle('home-focus-rail', zone === 'rail');
  $$('#tabs button').forEach((b, k) => b.classList.toggle('focus', zone === 'tabs' && k === state.tabFocus));
  $$('#hero .play, #hero .more').forEach((b, k) => b.classList.toggle('focus', zone === 'hero' && k === state.heroBtn));
  $$('.card').forEach((c, k) => c.classList.toggle('focus', zone === 'cards' && k === state.card));
  if (!quiet) sfx.move();
}

function activate() {
  const it = current();
  if (it.add) return newConfig();
  if (!it.install) return it.id === 'power' ? quit() : openPage(it.id);
  launch(it);
}

function options() {
  const it = current();
  if (it && it.install) openConfig(it);
}

async function quit() {
  if (await ask('Close FLPTools?', 'FL Studio keeps running. Settings a config changed are put back once FL closes.', 'Close')) api('app.close');
}

// ------------------------------------------------------------------ launch
async function launch(c, project) {
  const inst = installOf(c);
  if (!inst) return toast(`${c.install} isn't installed.`, true);
  sfx.start();
  const lf = $('#launchFx');
  const name = h('div', { class: 'n' }, project || c.project ? baseName(project || c.project) : (c.name !== 'Default' ? c.name : ''));
  const sel = !state.page && state.tab === 'fl' ? $('.slot.sel .tile') : null;
  const r = sel ? sel.getBoundingClientRect() : { left: innerWidth / 2 - 76, top: innerHeight / 2 - 76, width: 152, height: 152 };
  const ghost = h('div', { class: 'ghost', style: { left: r.left + 'px', top: r.top + 'px', width: r.width + 'px', height: r.height + 'px', background: colorOf(c) } },
    inst.icon ? h('img', { src: inst.icon, alt: '' }) : null);
  lf.replaceChildren(h('div', { class: 'veil' }), ghost, h('div', { class: 'cap' }, h('div', { class: 't' }, `Starting FL Studio ${inst.year}`), name));
  lf.className = '';
  lf.hidden = false;
  if (fx) fx.surge(1);
  requestAnimationFrame(() => requestAnimationFrame(() => {
    const size = Math.min(innerWidth, innerHeight) * 0.26;
    Object.assign(ghost.style, { left: (innerWidth - size) / 2 + 'px', top: (innerHeight - size) / 2 - 40 + 'px', width: size + 'px', height: size + 'px' });
    lf.classList.add('fly');
    setTimeout(() => lf.classList.add('on'), 420);
  }));
  let res;
  try { res = await api('launch', { id: c.id, project: project || '' }); }
  catch (e) { endLaunch(); return toast(e.message, true); }
  if (res.notes && res.notes.length) name.textContent = res.notes.join('. ');
  state.running.add(c.id);
  c.lastPlayed = res.lastPlayed;
  launch.id = c.id;
  launch.timer = setTimeout(() => launched(c.id), 30000);
}

function launched(id) {
  if (launch.id !== id) return;
  launch.id = null;
  clearTimeout(launch.timer);
  setTimeout(endLaunch, 400);
  if (!state.page) { drawRail(false); drawTop(); }
  if (state.settings.minimizeOnLaunch) setTimeout(() => api('app.minimize'), 300);
}

function endLaunch() {
  const lf = $('#launchFx');
  lf.classList.add('out');
  if (fx) fx.surge(0);
  setTimeout(() => { lf.hidden = true; lf.replaceChildren(); lf.className = ''; }, 450);
}

on('ready', m => launched(m.id));
on('exited', async m => {
  state.running.delete(m.id);
  const c = state.configs.find(x => x.id === m.id);
  if (c) c.playSeconds = m.playSeconds;
  const d = await api('init').catch(() => null);
  if (d) { state.installs = d.installs; if (!state.page) { drawRail(false); drawTop(); } }
  toast(m.restored ? 'FL Studio closed. Your FL settings are back to how they were.' : 'FL Studio closed.');
});

// ------------------------------------------------------------------ settings-style pages
const PAGES = {};

function openPage(id, arg) {
  closePage(true);
  const def = PAGES[id];
  const dockDef = DOCK.find(d => d.id === id);
  const side = h('div', { class: 'side' });
  const content = h('div', { class: 'content' });
  const extra = h('div', { class: 'btns' });
  const page = h('section', { class: 'page' },
    h('div', { class: 'page-head', style: { '--ic': dockDef ? dockDef.color : 'var(--text)' } },
      dockDef ? icon(id) : null, h('h1', {}, def.title(arg)), h('div', { class: 'grow' }), extra),
    h('div', { class: 'page-main' }, side, content),
    h('div', { class: 'page-foot' }, h('span', { class: 'key' }, h('span', { class: 'kbd' }, 'Esc'), 'Back')));
  $('#pages').append(page);
  state.page = { id, page, side, content, extra, sections: [], cur: 0 };
  sfx.ok();
  def.build(state.page, arg);
}

/** Fill a page's left list. Each section: { label, sub?, draw(content) }. */
function sections(p, list, start = 0) {
  p.sections = list;
  p.side.replaceChildren(...list.map((s, i) => h('button', { onclick: () => show(p, i) }, s.label, s.sub ? h('span', { class: 'sub' }, s.sub) : null)));
  show(p, Math.min(start, list.length - 1), true);
}
function show(p, i, quiet = false) {
  if (!p.sections[i]) return;
  if (!quiet && i !== p.cur) sfx.move();
  p.cur = i;
  $$('button', p.side).forEach((b, k) => b.classList.toggle('on', k === i));
  p.content.replaceChildren();
  p.content.scrollTop = 0;
  p.sections[i].draw(p.content);
}

function closePage(instant = false) {
  if (!state.page) return;
  const p = state.page;
  state.page = null;
  p.page.remove();
  if (instant) return;
  sfx.back();
  api('init').then(d => { apply(d); drawHome(); }).catch(() => {});
}

// row helpers
const rows = (...r) => h('div', { class: 'rows' }, r);
const row = (title, desc, ...right) => h('div', { class: 'r' }, h('div', { class: 'l' }, h('div', { class: 't' }, title), desc ? h('div', { class: 'd' }, desc) : null), right);
const toggle = (checked, onchange) => h('label', { class: 'tg' }, h('input', { type: 'checkbox', checked, onchange: e => onchange(e.target.checked) }), h('span'));
const button = (text, onclick, cls = '') => h('button', { class: 'btn ' + cls, onclick }, text);
const sec = text => h('h2', { class: 'sec' }, text);
const note = text => h('p', { class: 'note' }, text);
function logBox() {
  const el = h('div', { class: 'log' });
  el.add = (line, cls) => { el.append(h('div', { class: cls || '' }, line)); el.scrollTop = el.scrollHeight; };
  return el;
}
function playtime(sec) {
  if (!sec) return 'Not played yet';
  if (sec < 3600) return `Played for ${Math.max(1, Math.round(sec / 60))} min`;
  return `Played for ${(sec / 3600).toFixed(sec < 36000 ? 1 : 0)} hours`;
}
function ago(iso) {
  if (!iso) return '';
  const s = (Date.now() - new Date(iso)) / 1000;
  if (s < 3600) return 'Last played just now';
  if (s < 86400) return `Last played ${Math.round(s / 3600)} h ago`;
  const d = Math.round(s / 86400);
  return d === 1 ? 'Last played yesterday' : `Last played ${d} days ago`;
}

// ------------------------------------------------------------------ config options page
async function newConfig() {
  if (!state.installs.length) return toast('No FL Studio install found.', true);
  const install = state.installs.length === 1 ? state.installs[0].id : await pickFrom('Which FL Studio?', state.installs.map(i => ({
    label: `FL Studio ${i.year}`, sub: i.version, value: i.id, color: VERSION_COLORS[i.major],
  })));
  if (!install) return;
  const c = {
    id: Math.random().toString(36).slice(2, 12), name: 'New config', install, accent: '', image: '', project: '', template: '', theme: '', wallpaper: '',
    fullscreen: false, priority: 'Normal', cores: 0, closeOthers: false, autoConvert: true, syncMods: false, restoreAfter: true, args: '',
  };
  const saved = await run(() => api('config.save', { config: c }));
  if (!saved) return;
  state.configs.push(saved);
  state.sel.fl = state.configs.length - 1;
  openConfig(saved);
}

function openConfig(c) { openPage('config', c); }

PAGES.config = {
  title: c => c.name,
  build(p, c) {
    let timer;
    const save = (redraw = false) => {
      clearTimeout(timer);
      timer = setTimeout(async () => {
        const saved = await run(() => api('config.save', { config: c }));
        if (!saved) return;
        Object.assign(c, saved);
        const k = state.configs.findIndex(x => x.id === c.id);
        if (k >= 0) state.configs[k] = c;
        $('h1', p.page).textContent = c.name;
        if (redraw) show(p, p.cur, true);
      }, 300);
    };
    const set = (key, redraw) => v => { c[key] = v; save(redraw); };
    const fileRow = (title, desc, key, kind, empty, more = []) => row(title, c[key] || desc,
      ...more,
      button(c[key] ? 'Change' : 'Choose', async () => { const f = await openFile(kind); if (f) { c[key] = f; save(true); } }, 'sm'),
      c[key] ? button('Clear', () => { c[key] = ''; save(true); }, 'sm') : null);

    sections(p, [
      { label: 'General', draw: el => {
        const nameInp = h('input', { class: 'input', value: c.name, oninput: e => set('name')(e.target.value) });
        const ver = h('select', { class: 'input', onchange: e => set('install', true)(e.target.value) },
          state.installs.map(i => h('option', { value: i.id, selected: i.id === c.install }, `FL Studio ${i.year} (${i.version})`)),
          installOf(c) ? null : h('option', { value: c.install, selected: true }, `${c.install} (missing)`));
        const colors = h('div', { class: 'colors' },
          h('button', { class: c.accent ? '' : 'on', title: 'Version colour', style: { '--c': VERSION_COLORS[(installOf(c) || {}).major] || '#555' }, onclick: () => set('accent', true)('') }),
          COLORS.map(x => h('button', { class: c.accent === x ? 'on' : '', style: { '--c': x }, onclick: () => set('accent', true)(x) })),
          h('input', { type: 'color', class: 'sw', value: c.accent || colorOf(c), onchange: e => set('accent', true)(e.target.value) }));
        el.append(rows(
          row('Name', null, nameInp),
          row('Tag', 'A short label shown on the tile', h('input', { class: 'input', value: c.tag || '', maxLength: 18, placeholder: 'None', oninput: e => set('tag')(e.target.value.trim()) })),
          row('FL Studio version', null, ver),
          row('Tile colour', null, colors),
          fileRow('Tile picture', 'Uses the FL icon when empty', 'image', 'image')));
        const inst = installOf(c);
        const art = (inst && inst.art) || [];
        const current = c.background || 'anim:waves';
        const animPick = h('div', { class: 'art-pick' }, ANIMS.map(([mode, label]) => {
          const cv = h('canvas');
          const b = h('button', { class: current === 'anim:' + mode ? 'on' : '', onclick: () => { c.background = mode === 'waves' ? '' : 'anim:' + mode; save(true); } }, cv, h('span', {}, label));
          fxPreviews.add(cv, mode, colorOf(c));
          return b;
        }));
        el.append(sec('Home screen background'), note('Animated backgrounds use the tile colour.'), animPick,
          sec('Pictures'), h('div', { class: 'btns', style: { marginBottom: '12px' } },
            button('Choose a file', async () => { const f = await openFile('image'); if (f) { c.background = f; save(true); } }, 'sm')),
          h('div', { class: 'art-pick' }, art.map(a => h('button', { class: a.url === current ? 'on' : '', style: { backgroundImage: `url("${a.url}")` }, onclick: () => { c.background = a.url; save(true); } }, h('span', {}, a.name)))));
        el.append(sec('Play time'), rows(row(playtime(c.playSeconds), ago(c.lastPlayed))));
      } },
      { label: 'When it starts', draw: el => {
        const splashStyle = (c.splash || '').split('@')[0];
        const splashPick = h('select', { class: 'input', onchange: e => set('splash')(e.target.value ? e.target.value + '@' + colorOf(c).slice(1) : '') },
          h('option', { value: '', selected: !c.splash }, 'FL\'s own'));
        api('splash.styles').then(r => r.styles.forEach(x => splashPick.append(h('option', { value: x.id, selected: splashStyle === x.id }, x.label)))).catch(() => {});
        const recent = button('Recent', async () => {
          const inst = installOf(c);
          const list = inst ? await api('recent', { install: inst.id }) : [];
          if (!list.length) return toast('No recent projects for this version.');
          const v = await pickFrom('Recent projects', list.map(x => ({ label: x.name, sub: x.path, value: x.path })));
          if (v) { c.project = v; save(true); }
        }, 'sm');
        const themePick = button('From list', async () => {
          const list = await api('themes.list');
          const v = await pickFrom('Themes', list.map(t => ({ label: t.name, sub: t.builtIn ? 'Comes with FL' : 'Yours', value: t.path, color: t.swatch[0] })));
          if (v) { c.theme = v; save(true); }
        }, 'sm');
        el.append(rows(
          fileRow('Project', 'Nothing, FL opens normally', 'project', 'flp', '', [recent]),
          fileRow('Startup template', 'FL\'s own setting', 'template', 'flp'),
          fileRow('Theme', 'FL\'s own setting', 'theme', 'theme', '', [themePick]),
          fileRow('Background picture', 'FL\'s own setting', 'wallpaper', 'image'),
          row('Full screen', null, toggle(c.fullscreen, set('fullscreen'))),
          row('Startup animation', 'Plays while FL loads, in this config\'s tile colour', splashPick)));
        el.append(note('Template, theme, background and full screen are written into FL\'s settings just before it starts. They only apply when that FL version isn\'t already open.'));
      } },
      { label: 'Extras', draw: el => el.append(rows(
        row('Convert newer projects', 'An FL 26 project opened with FL 25 is converted to a copy first', toggle(c.autoConvert, set('autoConvert'))),
        row('Install the mod pack', 'Copies the FLPTools piano roll and Edison scripts in before starting', toggle(c.syncMods, set('syncMods'))),
        row('Close other FL windows first', 'FL still asks about unsaved changes', toggle(c.closeOthers, set('closeOthers'))),
        row('Undo setting changes afterwards', 'When FL closes, its template, theme, background and full screen go back', toggle(c.restoreAfter, set('restoreAfter'))))) },
      { label: 'Performance', draw: el => {
        const prio = h('select', { class: 'input', onchange: e => set('priority')(e.target.value) },
          [['BelowNormal', 'Below normal'], ['Normal', 'Normal'], ['AboveNormal', 'Above normal'], ['High', 'High']].map(([v, l]) => h('option', { value: v, selected: c.priority === v }, l)));
        const cores = h('select', { class: 'input', onchange: e => set('cores')(+e.target.value) },
          h('option', { value: 0, selected: !c.cores }, `All ${state.cores}`),
          Array.from({ length: state.cores - 1 }, (_, k) => k + 1).map(n => h('option', { value: n, selected: c.cores === n }, String(n))));
        const args = h('input', { class: 'input', value: c.args, placeholder: 'None', oninput: e => set('args')(e.target.value) });
        el.append(rows(row('Process priority', null, prio), row('CPU cores', null, cores), row('Command line', 'Added after the project path', args)));
      } },
      { label: 'Manage', draw: el => el.append(rows(
        row('Start now', null, button('Start', () => { closePage(true); drawHome(); launch(c); }, 'main')),
        row('Duplicate', 'Makes a copy of this config', button('Duplicate', async () => {
          const copy = Object.assign({}, c, { id: Math.random().toString(36).slice(2, 12), name: c.name + ' (copy)', playSeconds: 0, lastPlayed: null });
          const s = await run(() => api('config.save', { config: copy }), 'Copied.');
          if (s) { state.configs.push(s); openConfig(s); }
        })),
        row('Delete', 'Each FL version keeps at least one config', button('Delete', async () => {
          if (!await ask(`Delete "${c.name}"?`, null, 'Delete', true)) return;
          if (await run(() => api('config.delete', { id: c.id }), 'Deleted.') !== undefined) {
            state.configs = state.configs.filter(x => x.id !== c.id);
            state.sel.fl = Math.max(0, state.sel.fl - 1);
            closePage();
          }
        }, 'red')))) },
    ]);
  },
};

// ------------------------------------------------------------------ projects
PAGES.projects = {
  title: () => 'Projects',
  async build(p, openPath) {
    p.extra.append(button('Open file', async () => { const f = await openFile('flp'); if (f) openProject(p, f); }));
    const recent = await api('recent.all').catch(() => []);
    const list = recent.filter(r => r.exists);
    if (!list.length) {
      sections(p, [{ label: 'No recent projects', draw: el => el.append(h('div', { class: 'empty' }, 'Open an .flp to start.')) }]);
      return;
    }
    const i = openPath ? list.findIndex(r => r.path === openPath) : 0;
    sections(p, list.map(r => ({
      label: r.name, sub: `${r.install.replace('FL Studio ', 'FL ')}, ${r.modified}`,
      draw: el => projectEditor(el, r.path),
    })), Math.max(0, i));
    if (openPath && i < 0) openProject(p, openPath);
  },
};

function openProject(p, path) {
  p.side.querySelectorAll('button').forEach(b => b.classList.remove('on'));
  p.content.replaceChildren();
  projectEditor(p.content, path);
}

async function projectEditor(el, path) {
  el.append(h('div', { class: 'empty' }, 'Reading ' + fileName(path) + '...'));
  let proj;
  try { proj = await api('project.open', { path }); }
  catch (e) {
    const msg = e.message || String(e), cut = msg.search(/[.!?]\s/);
    el.replaceChildren(h('div', { class: 'blocked' }, h('div', { class: 'ti bad' }, icon('alert')),
      h('div', {}, h('div', { class: 'bt' }, cut > 0 ? msg.slice(0, cut) : msg), cut > 0 ? h('div', { class: 'bd' }, msg.slice(cut + 2)) : null,
        h('div', { class: 'bd faint' }, path))));
    return;
  }
  let tab = 'Info';
  let edits = { info: {}, channels: {}, patterns: {}, inserts: {} };
  const count = () => Object.keys(edits.info).length + ['channels', 'patterns', 'inserts'].reduce((n, k) => n + Object.keys(edits[k]).length, 0);
  const changed = h('span', { class: 'changes' });
  const mark = () => { const n = count(); changed.textContent = n ? `${n} unsaved` : ''; };
  const edit = (group, i, key) => v => { (edits[group][i] = edits[group][i] || {})[key] = v; mark(); };

  async function push() {
    if (!count()) return true;
    const r = await run(() => api('project.apply', {
      info: edits.info,
      channels: Object.entries(edits.channels).map(([i, v]) => ({ index: +i, ...v })),
      patterns: Object.entries(edits.patterns).map(([i, v]) => ({ index: +i, ...v })),
      inserts: Object.entries(edits.inserts).map(([i, v]) => ({ index: +i, ...v })),
    }));
    if (!r) return false;
    proj = r;
    edits = { info: {}, channels: {}, patterns: {}, inserts: {} };
    mark();
    return true;
  }
  async function save(copy) {
    if (!await push()) return;
    let to = proj.path;
    if (copy) { to = await saveFile('flp', proj.path.replace(/\.flp$/i, ' (edited).flp')); if (!to) return; }
    const s = await run(() => api('project.save', { path: to, backup: !copy }), copy ? 'Saved a copy.' : 'Saved. The old version is kept as .flp.bak.');
    if (s) { proj.path = s; draw(); }
  }
  async function openInFl() {
    if (count() && await ask('Save first?', 'Changes only exist in FLPTools until you save.', 'Save')) await save(false);
    const id = await pickFrom('Open with', state.configs.map(c => ({ label: `${titleOf(c)}${c.name !== 'Default' ? ', ' + c.name : ''}`, sub: (installOf(c) || {}).version, value: c.id, color: colorOf(c) })));
    if (!id) return;
    closePage(true);
    drawHome();
    launch(state.configs.find(c => c.id === id), proj.path);
  }

  function draw() {
    el.replaceChildren(
      h('div', { class: 'edhead' },
        h('div', { class: 'l' }, h('div', { class: 't' }, baseName(proj.path)), h('div', { class: 'd' }, `Saved with FL ${proj.version}. ${proj.path}`)),
        changed,
        h('div', { class: 'btns' }, button('Show in folder', () => api('shell.reveal', { path: proj.path }), 'sm'), button('Open in FL', openInFl, 'sm'),
          button('Save as', () => save(true), 'sm'), button('Save', () => save(false), 'sm main'))),
      h('div', { class: 'tabs' }, Object.keys(TABS).map(t => h('button', { class: t === tab ? 'on' : '', onclick: () => { tab = t; sfx.move(); draw(); } }, t))),
      TABS[tab]());
    mark();
  }

  const TABS = {
    Info() {
      const i = proj.info;
      const inp = (k, area) => h(area ? 'textarea' : 'input', { class: 'input', value: edits.info[k] ?? i[k] ?? '', oninput: e => { edits.info[k] = e.target.value; mark(); } });
      const secs = i.tempo ? proj.lengthBeats * 60 / i.tempo : 0;
      const missing = proj.channels.filter(c => !c.sampleOk).length;
      return h('div', {},
        h('div', { class: 'facts' }, [
          [(+i.tempo).toFixed(i.tempo % 1 ? 3 : 0).replace(/\.?0+$/, ''), 'BPM'], [secs ? `${Math.floor(secs / 60)}:${String(Math.round(secs % 60)).padStart(2, '0')}` : '-', 'Length'],
          [i.timeSig, 'Time signature'], [proj.clips, 'Playlist clips'], [proj.channels.length, 'Channels'], [proj.patterns.length, 'Patterns'],
          [proj.inserts.filter(x => x.used).length, 'Mixer tracks used'], [missing, 'Missing samples'],
        ].map(([v, k]) => h('div', {}, h('b', {}, String(v)), h('span', {}, k)))),
        rows(
          row('Title', null, inp('title')), row('Artists', null, inp('artists')), row('Genre', null, inp('genre')),
          row('Tempo', null, h('input', { class: 'input', type: 'number', step: '0.001', value: edits.info.tempo ?? i.tempo, oninput: e => { edits.info.tempo = +e.target.value; mark(); } })),
          row('URL', null, inp('url')), row('Comments', null, inp('comments', true))),
        sec('Other'),
        rows(
          row('Report', 'Channels, plugins, mixer, samples', button('Show', async () => { const r = await run(() => api('project.report')); if (r) showText('Report', r); }, 'sm')),
          row('Use as a template', 'Shows up in FL under File, New from template', button('Install', async () => {
            const name = await askText('Template name', baseName(proj.path));
            if (name) run(() => api('template.install', { src: proj.path, name }), 'Installed.');
          }, 'sm'))));
    },
    Channels() {
      return h('table', { class: 'tbl' }, h('tr', {}, h('th', {}, '#'), h('th', {}, 'Colour'), h('th', {}, 'Name'), h('th', {}, 'Type'), h('th', {}, 'Plugin or sample')),
        proj.channels.map(c => h('tr', {},
          h('td', { class: 'n' }, c.index),
          h('td', {}, c.color ? h('input', { type: 'color', class: 'sw', value: edits.channels[c.index]?.color || c.color, onchange: e => edit('channels', c.index, 'color')(e.target.value) }) : ''),
          h('td', {}, h('input', { class: 'input', value: edits.channels[c.index]?.name ?? c.name, oninput: e => edit('channels', c.index, 'name')(e.target.value) })),
          h('td', { class: 'small' }, c.type),
          h('td', { class: 'small', title: c.sample || c.plugin }, c.sample ? [h('span', { class: 'dot' + (c.sampleOk ? '' : ' bad') }), fileName(c.sample)] : c.plugin))));
    },
    Patterns() {
      return h('table', { class: 'tbl' }, h('tr', {}, h('th', {}, '#'), h('th', {}, 'Colour'), h('th', {}, 'Name'), h('th', {}, 'Notes')),
        proj.patterns.map(c => h('tr', {},
          h('td', { class: 'n' }, c.index),
          h('td', {}, h('input', { type: 'color', class: 'sw', value: edits.patterns[c.index]?.color || c.color || '#485156', onchange: e => edit('patterns', c.index, 'color')(e.target.value) })),
          h('td', {}, h('input', { class: 'input', value: edits.patterns[c.index]?.name ?? c.name, placeholder: `Pattern ${c.index}`, oninput: e => edit('patterns', c.index, 'name')(e.target.value) })),
          h('td', { class: 'n' }, c.notes))));
    },
    Mixer() {
      const all = TABS.Mixer.all;
      return h('div', {},
        rows(row('Show unused tracks', null, toggle(!!all, v => { TABS.Mixer.all = v; draw(); }))),
        h('div', { style: { height: '12px' } }),
        h('table', { class: 'tbl' }, h('tr', {}, h('th', {}, 'Track'), h('th', {}, 'Colour'), h('th', {}, 'Name'), h('th', {}, 'Effects')),
          proj.inserts.filter(x => all || x.used).map(c => h('tr', {},
            h('td', { class: 'n', style: { width: '90px' } }, c.label),
            h('td', {}, h('input', { type: 'color', class: 'sw', value: edits.inserts[c.index]?.color || c.color || '#5c656a', onchange: e => edit('inserts', c.index, 'color')(e.target.value) })),
            h('td', {}, h('input', { class: 'input', value: edits.inserts[c.index]?.name ?? c.name, placeholder: c.label, oninput: e => edit('inserts', c.index, 'name')(e.target.value) })),
            h('td', { class: 'small' }, c.plugins.join(', '))))));
    },
    Samples() {
      const list = proj.channels.filter(c => c.sample);
      const log = logBox();
      const find = h('input', { class: 'input', placeholder: 'C:\\Old\\Samples\\' });
      const repl = h('input', { class: 'input', placeholder: 'D:\\Samples\\' });
      const after = r => { if (!r) return; proj = r; draw(); (r.log || []).forEach(l => toast(l)); };
      return h('div', {},
        rows(
          row('Find missing samples', 'Looks through a folder and its subfolders for files with the same name', button('Choose folder', async () => { const f = await openFolder(); if (f) after(await run(() => api('samples.relink', { folder: f }))); }, 'sm')),
          row('Collect samples', 'Copies every sample next to the project and points the project at the copies',
            button('Into project folder', async () => after(await run(() => api('samples.collect', { folder: dirName(proj.path) + '\\Samples' }), 'Collected.')), 'sm'),
            button('Somewhere else', async () => { const f = await openFolder(); if (f) after(await run(() => api('samples.collect', { folder: f }), 'Collected.')); }, 'sm')),
          row('Replace in paths', null, find, repl, button('Replace', async () => { const r = await run(() => api('samples.replace', { find: find.value, replace: repl.value })); if (r) { toast(`${r.changed} changed.`); after(r); } }, 'sm'))),
        sec(`${list.length} samples, ${list.filter(c => !c.sampleOk).length} missing`),
        h('table', { class: 'tbl' }, h('tr', {}, h('th', {}, 'Ch'), h('th', {}, 'Channel'), h('th', {}, 'File')),
          list.map(c => h('tr', {}, h('td', { class: 'n' }, c.index), h('td', {}, c.name), h('td', { class: 'small', style: { overflowWrap: 'anywhere' } }, h('span', { class: 'dot' + (c.sampleOk ? '' : ' bad') }), c.sample)))));
    },
    Notes() {
      const picked = new Set();
      const num = (v, step = 1) => h('input', { class: 'input', type: 'number', value: v, step, style: { width: '120px' } });
      const tr = num(0), vs = num(1, 0.05), va = num(0), ls = num(1, 0.05), st = num(1, 0.1);
      const grid = h('select', { class: 'input' }, [['Off', 0], ['1/4', 1], ['1/8', 0.5], ['1/16', 0.25], ['1/32', 0.125], ['1/8 triplet', 1 / 3], ['1/16 triplet', 1 / 6]].map(([l, v]) => h('option', { value: v }, l)));
      const chan = h('select', { class: 'input' }, h('option', { value: -1 }, 'All'), proj.channels.map(c => h('option', { value: c.index }, `${c.index}. ${c.name}`)));
      const pats = proj.patterns.filter(x => x.notes);
      return h('div', {},
        sec('Patterns'), note('None picked means all of them.'),
        h('div', { class: 'btns', style: { marginBottom: '16px' } }, pats.map(x => {
          const b = button(`${x.name || 'Pattern ' + x.index} (${x.notes})`, () => { picked.has(x.index) ? picked.delete(x.index) : picked.add(x.index); b.classList.toggle('main', picked.has(x.index)); }, 'sm');
          return b;
        })),
        rows(row('Transpose', 'Semitones', tr), row('Velocity', 'Multiply, then add', vs, va), row('Length', 'Multiply', ls),
          row('Quantize', 'Grid and strength (0 to 1)', grid, st), row('Channel', null, chan),
          row('', null, button('Apply', async () => {
            if (!await push()) return;
            const r = await run(() => api('project.notes', {
              patterns: picked.size ? [...picked] : null, transpose: +tr.value, velScale: +vs.value, velAdd: +va.value,
              quantize: Math.round(+grid.value * proj.info.ppq), strength: +st.value, lengthScale: +ls.value, channel: +chan.value,
            }));
            if (r) { proj = r; toast(`${r.changed} notes changed. Save to keep them.`); sfx.ok(); }
          }, 'main'))),
        sec('MIDI'),
        rows(
          row('Song', 'Follows the playlist (pattern clips only)', button('Export', async () => { const f = await saveFile('mid', proj.path.replace(/\.flp$/i, '.mid')); if (f) run(() => api('project.midi', { song: true, path: f }), n => `${n} notes exported.`); }, 'sm')),
          row('Patterns', 'One .mid file per pattern', button('Export to folder', async () => { const f = await openFolder(); if (f) run(() => api('project.midi', { song: false, path: f }), n => `${n} patterns exported.`); }, 'sm'))));
    },
  };
  draw();
}

// ------------------------------------------------------------------ converter
PAGES.convert = {
  title: () => 'Converter',
  build(p) {
    const log = logBox();
    const fl25 = state.installs.filter(i => i.major === 25).map(i => i.version);
    const ver = h('select', { class: 'input' }, (fl25.length ? fl25 : ['25.2.5.5319']).map(v => h('option', { value: v }, v)));
    const results = (list, what) => { log.add(what); (list || []).forEach(r => log.add(`${r.ok ? 'ok  ' : 'fail'}  ${fileName(r.file)}  ${r.msg}`, r.ok ? 'ok' : 'bad')); };
    const withLog = (...parts) => el => { el.append(...parts, sec('Log'), log); };
    sections(p, [
      { label: 'FL 26 to FL 25', draw: withLog(
        note('Makes a copy of an FL 26 project that FL 25 can open. FL 26-only features are dropped.'),
        rows(row('Save as FL version', null, ver),
          row('Project', null, button('Choose and convert', async () => {
            const src = await openFile('flp'); if (!src) return;
            const dst = await saveFile('flp', src.replace(/\.flp$/i, ' (FL25).flp')); if (!dst) return;
            const r = await run(() => api('convert.fl25', { src, dst, version: ver.value }), 'Converted.');
            if (r) { log.add(`${fileName(src)} (FL ${r.from}) to ${fileName(dst)}`); r.log.forEach(l => log.add('  ' + l, 'ok')); }
          }, 'main')))) },
      { label: 'FLP to JSON', draw: withLog(
        note('Writes every event in the project to a JSON file you can read and edit. Notes, playlist clips and mixer settings come out as tables.'),
        rows(row('Project', null, button('Choose', async () => {
          const src = await openFile('flp'); if (!src) return;
          const dst = await saveFile('json', src.replace(/\.flp$/i, '.json')); if (!dst) return;
          const r = await run(() => api('json.decompile', { src, dst }), 'Done.');
          if (r) log.add(`${fileName(src)} to ${fileName(dst)}, ${r.events} events, ${r.identical ? 'rebuilds byte for byte' : 'would NOT rebuild identically'}`, r.identical ? 'ok' : 'bad');
        }, 'main')))) },
      { label: 'JSON to FLP', draw: withLog(
        note('Builds an .flp from a JSON file made by FLP to JSON.'),
        rows(row('JSON file', null, button('Choose', async () => {
          const src = await openFile('json'); if (!src) return;
          const dst = await saveFile('flp', src.replace(/\.json$/i, '.flp')); if (!dst) return;
          const r = await run(() => api('json.recompile', { src, dst }), 'Done.');
          if (r) log.add(`${fileName(src)} to ${fileName(dst)}, ${r.events} events, ${r.bytes.toLocaleString()} bytes`, 'ok');
        }, 'main')))) },
      { label: 'Check round-trip', draw: withLog(
        note('Converts projects to JSON and back in memory and checks the bytes match.'),
        rows(row('Projects', null, button('Choose', async () => { const files = await openFile('flp', true); if (files) results(await run(() => api('verify', { files })), 'Check'); }, 'main')))) },
      { label: 'Whole folder', draw: el => {
        let op = 'convert';
        const pick = h('select', { class: 'input', onchange: e => (op = e.target.value) },
          h('option', { value: 'convert' }, 'Convert FL 26 projects to FL 25'), h('option', { value: 'verify' }, 'Check round-trip'), h('option', { value: 'report' }, 'Write a report for each'));
        withLog(note('Goes through every .flp in a folder and its subfolders. Converted copies are saved next to the originals as "name (FL25).flp".'),
          rows(row('Job', null, pick), row('Folder', null, button('Choose and run', async () => {
            const folder = await openFolder(); if (!folder) return;
            log.add('Working in ' + folder);
            results(await run(() => api('batch', { folder, op, version: ver.value })), 'Finished');
          }, 'main'))))(el);
      } },
    ]);
  },
};

// ------------------------------------------------------------------ mods
PAGES.mods = {
  title: () => 'Mods',
  async build(p) {
    const paths = await api('paths');
    let mods = await api('mods.list');
    const set = async (cmd, list) => {
      const n = await run(() => api(cmd, { names: list.map(m => m.kind + '/' + m.name) }));
      if (n === undefined) return;
      mods = await api('mods.list');
      toast(cmd === 'mods.install' ? `${n} installed.` : `${n} removed.`);
      show(p, p.cur, true);
    };
    const list = (kind, where, folder) => el => {
      const ms = mods.filter(m => m.kind === kind);
      el.append(note(where), h('div', { class: 'btns', style: { marginBottom: '14px' } },
        button('Install all', () => set('mods.install', ms), 'sm'), button('Remove all', () => set('mods.uninstall', ms), 'sm'),
        button('Open folder', () => api('shell.reveal', { path: folder }), 'sm')),
      rows(ms.map(m => row(m.name, m.description, toggle(m.installed, v => set(v ? 'mods.install' : 'mods.uninstall', [m]))))));
    };
    const count = kind => { const ms = mods.filter(m => m.kind === kind); return `${ms.filter(m => m.installed).length} of ${ms.length} installed`; };
    sections(p, [
      { label: 'Window animations', sub: 'FL windows fade in', draw: el => windowAnimPane(el) },
      { label: 'Companion', sub: 'Runs inside FL', draw: el => companionPane(el) },
      { label: 'Startup animation', sub: 'Plays while FL loads', draw: el => splashPane(el) },
      { label: 'Piano roll scripts', sub: count('pianoroll'), draw: list('pianoroll', 'In FL: piano roll menu, Tools, Scripting.', paths.pianoRoll) },
      { label: 'Edison scripts', sub: count('edison'), draw: list('edison', 'In Edison: Tools, Scripting. They work on the selection, or the whole sample if nothing is selected.', paths.audio) },
      { label: 'Templates', draw: el => el.append(note('Templates show up in FL under File, New from template.'), rows(
        row('Add a project as a template', null, button('Choose', async () => {
          const src = await openFile('flp'); if (!src) return;
          const name = await askText('Template name', baseName(src)); if (!name) return;
          run(() => api('template.install', { src, name }), 'Installed.');
        }, 'sm')),
        row('Templates folder', paths.templates, button('Open', () => api('shell.reveal', { path: paths.templates }), 'sm')))) },
    ]);
  },
};

// ------------------------------------------------------------------ FL mods: companion script, startup animation
async function companionPane(el) {
  let st = await run(() => api('companion.status'));
  if (!st) return;
  const cfg = st.config;
  let timer;
  const save = () => { clearTimeout(timer); timer = setTimeout(() => run(() => api('companion.config', { config: cfg })), 300); };
  const set = k => v => { cfg[k] = v; save(); };
  const redraw = async () => { st = await api('companion.status'); el.replaceChildren(); companionPane(el); };

  el.append(note('A script that runs inside FL Studio while you work. It colours things from their names, names mixer tracks after their channels, can autosave, and shows how long you have been working in the hint bar. Changes here reach FL within a few seconds.'));
  el.append(rows(
    row('Script', st.installed ? (st.upToDate ? 'Installed' : 'Installed, older version') : 'Not installed',
      st.installed ? null : button('Install', async () => { await run(() => api('companion.install'), 'Installed.'); redraw(); }, 'sm main'),
      st.installed && !st.upToDate ? button('Update', async () => { await run(() => api('companion.install'), 'Updated.'); redraw(); }, 'sm main') : null,
      st.installed ? button('Remove', async () => { await run(() => api('companion.uninstall'), 'Removed.'); redraw(); }, 'sm') : null),
    row('Running in FL', st.assigned.length ? st.assigned.join(', ') : 'Not attached to a MIDI input yet')));
  if (st.installed && !st.assigned.length) {
    el.append(sec('Turn it on in FL (once)'), rows(
      row('1', 'In FL: Options, MIDI settings'),
      row('2', 'Under Input, click a port' + (st.inputs.length ? ' (for example ' + st.inputs[st.inputs.length - 1] + ')' : '')),
      row('3', 'Set Controller type to "FLPTools Companion (user)" and click Enable'),
      row('4', 'Your MIDI keeps working; the script passes it through')));
  }
  el.append(sec('What it does'), rows(
    row('Colour by name', 'Channels, mixer tracks and playlist tracks get a colour when you name them', toggle(cfg.autoColor, set('autoColor'))),
    row('   Channels', null, toggle(cfg.colorChannels, set('colorChannels'))),
    row('   Mixer tracks', null, toggle(cfg.colorMixer, set('colorMixer'))),
    row('   Playlist tracks', null, toggle(cfg.colorPlaylist, set('colorPlaylist'))),
    row('Mixer follows channel', 'An empty mixer track takes the name and colour of the channel routed to it', toggle(cfg.mixerFollowsChannel, set('mixerFollowsChannel'))),
    row('Autosave', 'Saves the open project when it has unsaved changes', h('select', { class: 'input', onchange: e => set('autosaveMinutes')(+e.target.value) },
      [[0, 'Off'], [2, 'Every 2 min'], [5, 'Every 5 min'], [10, 'Every 10 min'], [20, 'Every 20 min']].map(([v, l]) => h('option', { value: v, selected: cfg.autosaveMinutes === v }, l)))),
    row('Session time', 'Shows how long you have been working in the hint bar', h('select', { class: 'input', onchange: e => set('sessionHintMinutes')(+e.target.value) },
      [[0, 'Off'], [15, 'Every 15 min'], [30, 'Every 30 min'], [60, 'Every hour']].map(([v, l]) => h('option', { value: v, selected: cfg.sessionHintMinutes === v }, l))))));
  el.append(sec('Colours'), note('Words in a name pick the colour. The first rule that matches wins.'));
  const ruleRows = h('div', { class: 'rows' });
  const drawRules = () => ruleRows.replaceChildren(...cfg.rules.map((r, i) => row('', null,
    h('input', { type: 'color', class: 'sw', value: r.color, onchange: e => { r.color = e.target.value; save(); } }),
    h('input', { class: 'input', style: { width: '100%' }, value: r.words.join(', '), onchange: e => { r.words = e.target.value.split(',').map(w => w.trim()).filter(Boolean); save(); } }),
    button('Remove', () => { cfg.rules.splice(i, 1); save(); drawRules(); }, 'sm'))));
  drawRules();
  el.append(ruleRows, h('div', { class: 'btns', style: { marginTop: '10px' } }, button('Add a rule', () => { cfg.rules.push({ words: [], color: '#888888' }); save(); drawRules(); }, 'sm')));
  if (st.log.length) el.append(sec('Recent activity'), h('div', { class: 'log' }, st.log.join('\n')));
}

function windowAnimPane(el) {
  const s = state.settings;
  const save = () => run(() => api('settings.save', { settings: s }));
  const set = k => v => { s[k] = v; save(); };
  el.append(note('FL windows (mixer, piano roll, plugins, dialogs) fade in when they open, and floating ones rise into place. FL itself is not changed: FLPTools does it from outside while it is open (minimized is fine). Turn it off here if a window ever looks wrong.'));
  el.append(rows(
    row('Window animations', null, toggle(!!s.flWindowAnim, set('flWindowAnim'))),
    row('Speed', null, h('select', { class: 'input', onchange: e => set('flWindowAnimMs')(+e.target.value) },
      [[150, 'Quick'], [220, 'Smooth'], [380, 'Slow']].map(([v, l]) => h('option', { value: v, selected: (s.flWindowAnimMs || 220) === v }, l)))),
    row('Rise into place', 'Floating windows slide up a little as they fade in', toggle(s.flWindowAnimRise !== false, set('flWindowAnimRise'))),
    row('Windows inside FL\'s main window', 'Experimental. Docked windows like the playlist and channel rack', toggle(!!s.flWindowAnimInside, set('flWindowAnimInside')))));
  el.append(sec('Try it'), rows(row('See the effect without FL', 'Opens Notepad with the animation applied to it',
    button('Try on Notepad', async () => { await run(() => api('winanim.try', { process: 'notepad' })); api('shell.run', { file: 'notepad.exe' }).catch(() => toast('Open Notepad yourself within 30 seconds to see it.')); }, 'sm'))));
}

async function splashPane(el) {
  const info = await run(() => api('splash.styles'));
  if (!info) return;
  let current = info.current;
  let accent = (current.split('@')[1] ? '#' + current.split('@')[1] : info.accents[0]).toLowerCase();
  el.append(note('An animation FL plays on its loading screen, made from your FL icon. It applies to every FL version on this PC. A launch config can also pick its own.'));
  const colors = h('div', { class: 'colors', style: { justifyContent: 'flex-start', marginBottom: '16px' } });
  const drawColors = () => colors.replaceChildren(...info.accents.map(x => h('button', { class: x === accent ? 'on' : '', style: { '--c': x }, onclick: () => { accent = x; drawColors(); draw(); } })),
    h('input', { type: 'color', class: 'sw', value: accent, onchange: e => { accent = e.target.value.toLowerCase(); drawColors(); draw(); } }));
  const grid = h('div', { class: 'splash-grid' });
  const choose = async spec => { const r = await run(() => api('splash.apply', { spec }), spec ? 'FL will play it next time it starts.' : 'FL\'s own loading screen is back.'); if (r !== undefined) { current = r; draw(); } };
  const draw = () => grid.replaceChildren(
    h('button', { class: 'splash' + (!current ? ' on' : ''), onclick: () => choose('') }, h('div', { class: 'pv-none' }, 'FL\'s own'), h('span', {}, 'Default')),
    ...info.styles.map(s => {
      const spec = s.id + '@' + accent.slice(1);
      const pv = h('div', { class: 'pv-anim loading', style: { '--n': info.frames } });
      api('splash.preview', { spec }).then(url => { pv.style.backgroundImage = `url("${url}")`; pv.classList.remove('loading'); }).catch(() => {});
      return h('button', { class: 'splash' + (current.toLowerCase() === spec.toLowerCase() ? ' on' : ''), onclick: () => choose(spec) }, pv, h('span', {}, s.label));
    }));
  drawColors();
  draw();
  el.append(sec('Colour'), colors, sec('Style'), grid);
}

// ------------------------------------------------------------------ themes
PAGES.themes = {
  title: () => 'Themes',
  build(p) {
    const opts = { base: '#2f8ad6', scheme: 'Complementary', dark: true, seed: 1 };
    let theme = null, themeName = 'My theme';
    const preview = h('div');
    function drawPreview() {
      const c = theme.colors, g = k => c[k] || '#777';
      const notes = Array.from({ length: 16 }, (_, i) => g('NoteColor' + i));
      const heights = [62, 80, 45, 90, 70, 55, 85, 40, 75, 66];
      preview.replaceChildren(h('div', { class: 'pv', style: { '--pvb': g('BackColor') } },
        h('div', { class: 'box' }, h('span', { class: 'cap' }, 'Step sequencer'),
          h('div', { class: 'steps' }, Array.from({ length: 16 }, (_, i) => h('i', { class: [0, 4, 7, 10, 12].includes(i) ? 'hit' : '', style: { '--c': Math.floor(i / 4) % 2 ? g('StepOdd') : g('StepEven'), '--h': g('Selected') } })))),
        h('div', { class: 'box' }, h('span', { class: 'cap' }, 'Meters'),
          h('div', { class: 'meters', style: Object.fromEntries([0, 1, 2, 3, 4, 5].map(i => ['--m' + i, g('Meter' + i)])) }, heights.map(v => h('i', { style: { '--hgt': v + '%' } })))),
        h('div', { class: 'box' }, h('span', { class: 'cap' }, 'Piano roll'),
          h('div', { class: 'roll' }, [[3, 1, 0], [14, 3, 1], [27, 0, 2], [33, 2, 3], [45, 1, 4], [53, 5, 5], [66, 2, 6], [74, 3, 7], [84, 4, 8], [9, 7, 9], [40, 6, 10], [60, 8, 11]].map(([x, y, k]) =>
            h('i', { style: { left: x + '%', top: (y * 10 + 1) + 'px', width: (5 + (k % 3) * 3) + '%', '--c': notes[k] } })))),
        h('div', { class: 'box' }, h('span', { class: 'cap' }, 'Note colours'), h('div', { class: 'pal' }, notes.map(x => h('i', { style: { '--c': x } }))),
          h('span', { class: 'cap' }, 'Highlight, option, mute'),
          h('div', { class: 'btns' }, ['Highlight', 'Option', 'Mute'].map(k => h('span', { style: { width: '44px', height: '18px', borderRadius: '3px', background: g(k) } }))))));
    }
    const gen = async () => { theme = await api('themes.generate', opts); drawPreview(); };
    const saveRows = () => {
      const name = h('input', { class: 'input', value: themeName, oninput: e => (themeName = e.target.value) });
      return rows(row('Name', null, name),
        row('Save as an FL theme', 'Pick it in FL under Options, General, Theme, or set it on a config', button('Save theme', () => run(() => api('themes.save', { name: themeName, values: theme.values }), 'Theme saved.'), 'sm main')),
        row('Save note colours only', 'A piano roll note colour preset', button('Save colours', () => run(() => api('palette.save', { name: themeName, colors: Array.from({ length: 16 }, (_, i) => theme.colors['NoteColor' + i] || '#888888') }), 'Colours saved.'), 'sm')));
    };
    sections(p, [
      { label: 'Make a theme', draw: async el => {
        const base = h('input', { type: 'color', class: 'sw', value: opts.base, onchange: e => { opts.base = e.target.value; gen(); } });
        const scheme = h('select', { class: 'input', onchange: e => { opts.scheme = e.target.value; gen(); } },
          ['Monochrome', 'Complementary', 'Analogous', 'Neon', 'Pastel', 'Random'].map(s => h('option', { selected: s === opts.scheme }, s)));
        const mode = h('select', { class: 'input', onchange: e => { opts.dark = e.target.value === 'dark'; gen(); } },
          h('option', { value: 'dark', selected: opts.dark }, 'Dark'), h('option', { value: 'light', selected: !opts.dark }, 'Light'));
        el.append(preview, rows(row('Base colour', null, base), row('Colour scheme', null, scheme), row('Brightness', null, mode),
          row('Random', 'New base colour and seed', button('Randomize', () => {
            opts.seed = Math.floor(Math.random() * 1e6);
            opts.base = '#' + Math.floor(Math.random() * 0xffffff).toString(16).padStart(6, '0');
            base.value = opts.base; gen();
          }, 'sm'))), sec('Save'), saveRows());
        if (!theme) await gen(); else drawPreview();
      } },
      { label: 'Saved themes', draw: async el => {
        el.append(note('Your themes and the ones that come with FL. Pick one to preview it, then save a copy under a new name if you change it.'));
        const grid = h('div', { class: 'thl' });
        el.append(grid);
        (await api('themes.list')).forEach(t => grid.append(h('button', { onclick: async () => {
          theme = await api('themes.load', { path: t.path });
          themeName = t.builtIn ? t.name + ' (edit)' : t.name;
          sfx.ok();
          show(p, 0, true);
        } }, h('div', { class: 'bar' }, (t.swatch.length ? t.swatch : ['#777']).map(x => h('i', { style: { background: x } }))),
          h('div', { class: 'nm' }, t.name), h('div', { class: 'src' }, t.builtIn ? 'Comes with FL' : 'Yours'))));
      } },
    ]);
  },
};

// ------------------------------------------------------------------ configs
PAGES.configs = {
  title: () => 'Configs',
  build(p) {
    p.extra.append(button('New config', () => { closePage(true); newConfig(); }));
    const groups = state.installs.map(i => i.id).concat(state.configs.map(c => c.install).filter(id => !state.installs.some(i => i.id === id)));
    sections(p, [...new Set(groups)].map(g => {
      const inst = state.installs.find(i => i.id === g);
      return {
        label: inst ? `FL Studio ${inst.year}` : g, sub: inst ? inst.version : 'Not installed',
        draw: el => {
          const list = state.configs.filter(c => c.install === g);
          el.append(rows(list.map(c => {
            const r = row(c.name, [playtime(c.playSeconds), c.project && 'Opens ' + baseName(c.project), c.theme && 'Theme ' + baseName(c.theme), c.priority !== 'Normal' && c.priority + ' priority'].filter(Boolean).join('. '),
              h('span', { style: { width: '22px', height: '22px', borderRadius: '5px', background: colorOf(c), flex: 'none' } }),
              button('Start', e => { e.stopPropagation(); closePage(true); drawHome(); launch(c); }, 'sm'));
            r.classList.add('click');
            r.onclick = () => openConfig(c);
            return r;
          })));
          if (inst) el.append(sec('Install'), rows(row('Folder', inst.dir), row('Settings key', `HKEY_CURRENT_USER\\Software\\Image-Line\\${inst.registryKey}`), row('Startup template', inst.template || 'None')));
        },
      };
    }));
  },
};

// ------------------------------------------------------------------ settings
PAGES.settings = {
  title: () => 'Settings',
  async build(p) {
    const s = state.settings;
    const paths = await api('paths');
    const save = async () => { await run(() => api('settings.save', { settings: s })); apply({ installs: state.installs, configs: state.configs, settings: s, cores: state.cores }); drawTop(); };
    sections(p, [
      { label: 'Sound and motion', draw: el => el.append(rows(
        row('Sounds', null, toggle(s.sounds, v => { s.sounds = v; save(); })),
        row('Animations', null, toggle(s.animations, v => { s.animations = v; save(); })),
        row('Minimize when FL opens', null, toggle(s.minimizeOnLaunch, v => { s.minimizeOnLaunch = v; save(); })))) },
      { label: 'FL Studio', draw: el => {
        const files = i => i.files === 'signed' ? 'Files signed by Image-Line' : i.files === 'modified' ? 'Modified: ' + i.filesDetail : 'File check: ' + (i.filesDetail || 'unknown');
        el.append(rows(state.installs.map(i => row(`FL Studio ${i.year}`, `${i.version}. ${i.dir}`,
          h('span', { class: 'val' + (i.files === 'modified' ? ' bad' : ''), style: { maxWidth: '55%' } }, files(i) + (i.running ? '. Running' : ''))))),
          h('div', { style: { height: '14px' } }),
          rows(row('Look for installs again', null, button('Scan', async () => {
            const d = await run(() => api('probe'), d => `${d.installs.length} found.`);
            if (d) { apply(d); show(p, p.cur, true); }
          }, 'sm'))));
      } },
      { label: 'Folders', draw: el => el.append(rows(
        [['FL user data', paths.userData], ['Piano roll scripts', paths.pianoRoll], ['Edison scripts', paths.audio], ['Themes', paths.themes], ['Templates', paths.templates], ['FLPTools settings', paths.config]]
          .map(([t, path]) => row(t, path, button('Open', () => api('shell.reveal', { path }), 'sm'))))) },
      { label: 'Controls', draw: el => el.append(rows(
        row('Move', 'Arrow keys, mouse wheel, or a gamepad\'s d-pad / left stick'),
        row('Start', 'Enter, click a selected tile, or A'),
        row('Options for a config', 'O, right click, the ... button, or Y'),
        row('Back', 'Esc or B'),
        row('Switch between FL Studio and Tools', 'Q and E, or the shoulder buttons'),
        row('Full screen', 'F11'))) },
    ]);
  },
};

// ------------------------------------------------------------------ input
function back() {
  if (dialog.close) { dialog.close(undefined); return true; }
  if (state.page) {
    const wasConfig = state.page.id === 'config';
    closePage();
    if (wasConfig) drawHome();
    return true;
  }
  if (state.zone !== 'rail') { setZone('rail'); return true; }
  return false;
}

function move(dx, dy) {
  if (dialog.close || !$('#launchFx').hidden) return;
  if (state.page) {
    const p = state.page;
    if (dy && p.sections.length) {
      const n = p.cur + dy;
      if (n < 0 || n >= p.sections.length) sfx.edge(); else show(p, n);
    }
    return;
  }
  const z = state.zone;
  if (z === 'tabs') {
    if (dx) {
      const n = state.tabFocus + dx;
      if (n < 0 || n > 1) return sfx.edge();
      state.tabFocus = n;
      switchTab(n ? 'tools' : 'fl');
      setZone('tabs', true);
    } else if (dy > 0) setZone('rail');
    else sfx.edge();
  } else if (z === 'rail') {
    if (dx) select(state.sel[state.tab] + dx);
    else if (dy < 0) { state.tabFocus = state.tab === 'fl' ? 0 : 1; setZone('tabs'); }
    else { state.heroBtn = 0; setZone('hero'); }
  } else if (z === 'hero') {
    const n = $$('#hero .play, #hero .more').length;
    if (dx) { const k = state.heroBtn + dx; if (k < 0 || k >= n) sfx.edge(); else { state.heroBtn = k; setZone('hero'); } }
    else if (dy < 0) setZone('rail');
    else if ($$('.card').length) setZone('cards');
    else sfx.edge();
  } else if (z === 'cards') {
    const n = $$('.card').length;
    if (dx) { const k = state.card + dx; if (k < 0 || k >= n) sfx.edge(); else { state.card = k; setZone('cards'); } }
    else if (dy < 0) setZone('hero');
    else sfx.edge();
  }
}

function confirmKey() {
  if (state.page || dialog.close) return;
  const z = state.zone;
  if (z === 'tabs') setZone('rail');
  else if (z === 'rail') activate();
  else if (z === 'hero') { const b = $$('#hero .play, #hero .more')[state.heroBtn]; if (b) b.click(); }
  else if (z === 'cards') { const c = $$('.card')[state.card]; if (c) c.click(); }
}

document.addEventListener('keydown', e => {
  const typing = /INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName);
  if (e.key === 'Escape') { if (back()) e.preventDefault(); return; }
  if (typing) return;
  const k = e.key.toLowerCase();
  if (k === 'arrowleft') move(-1, 0);
  else if (k === 'arrowright') move(1, 0);
  else if (k === 'arrowup') move(0, -1);
  else if (k === 'arrowdown') move(0, 1);
  else if (k === 'enter' && !state.page && !dialog.close) confirmKey();
  else if (k === 'o' && !state.page && !dialog.close) options();
  else if ((k === 'q' || k === 'e') && !state.page && !dialog.close) switchTab(k === 'q' ? 'fl' : 'tools');
  else if (k === 'backspace') back();
  else return;
  e.preventDefault();
});

$('#rail').addEventListener('wheel', e => {
  e.preventDefault();
  const now = performance.now();
  if (now - (move.wheel || 0) < 110) return;
  move.wheel = now;
  setZone('rail', true);
  select(state.sel[state.tab] + ((e.deltaY || e.deltaX) > 0 ? 1 : -1));
}, { passive: false });

const pad = { held: {}, next: {} };
function gamepad(t) {
  const gp = navigator.getGamepads && [...navigator.getGamepads()].find(Boolean);
  if (gp) {
    const ax = gp.axes[0] || 0, ay = gp.axes[1] || 0, b = i => gp.buttons[i] && gp.buttons[i].pressed;
    const input = { left: b(14) || ax < -0.6, right: b(15) || ax > 0.6, up: b(12) || ay < -0.6, down: b(13) || ay > 0.6, a: b(0), b: b(1), y: b(3), lb: b(4), rb: b(5), start: b(9) };
    const act = {
      left: () => move(-1, 0), right: () => move(1, 0), up: () => move(0, -1), down: () => move(0, 1),
      a: () => (dialog.close || state.page) ? document.activeElement.click() : confirmKey(),
      b: back, y: options, lb: () => !state.page && switchTab('fl'), rb: () => !state.page && switchTab('tools'), start: () => openPage('settings'),
    };
    for (const [k, down] of Object.entries(input)) {
      const was = pad.held[k], dir = ['left', 'right', 'up', 'down'].includes(k);
      if (down && (!was || (dir && t > pad.next[k]))) { pad.next[k] = t + (was ? 110 : 360); act[k](); }
      pad.held[k] = down;
    }
  }
  requestAnimationFrame(gamepad);
}

// ------------------------------------------------------------------ browser preview without the app
function mockApi(cmd, args) {
  const ico = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><circle cx="32" cy="32" r="30" fill="#fff"/><path d="M24 16h18v6H31v6h9v6h-9v14h-7z" fill="#444"/></svg>');
  const installs = [['2024', '24.1.1.4285', 24, 'FL Studio 21'], ['2025', '25.2.5.5319', 25, 'FL Studio 25'], ['2026', '26.1.0.5530', 26, 'FL Studio 26']]
    .map(([y, v, m, k]) => ({ id: 'FL Studio ' + y, year: y, version: v, major: m, dir: 'C:\\Program Files\\Image-Line\\FL Studio ' + y, registryKey: k, icon: ico, running: false, template: '', background: '', art: [] }));
  mockApi.cfg = mockApi.cfg || installs.map((i, k) => ({ id: 'c' + k, name: 'Default', install: i.id, accent: '', image: '', background: '', project: '', template: '', theme: '', wallpaper: '', fullscreen: false, priority: 'Normal', cores: 0, closeOthers: false, autoConvert: true, syncMods: false, restoreAfter: true, args: '', playSeconds: k * 50000 }));
  const r = {
    init: () => ({ installs, configs: mockApi.cfg, settings: state.settings, cores: 16 }),
    recent: () => ['Night drive', 'Demo beat', 'Sketch 12'].map(n => ({ name: n, path: `C:\\FLPS\\${n}.flp`, exists: true, modified: '2026-11-02 21:14' })),
    'recent.all': () => r.recent().map(x => ({ ...x, install: 'FL Studio 2025' })),
    image: () => '', 'config.save': () => args.config, 'themes.list': () => [],
    paths: () => ({ userData: '', pianoRoll: '', audio: '', themes: '', templates: '', config: '' }),
    'mods.list': () => [],
    launch: async () => { setTimeout(() => (listeners.ready || []).forEach(f => f({ id: args.id })), 1500); return { notes: [] }; },
  };
  return Promise.resolve(r[cmd] ? r[cmd]() : null);
}

start();
