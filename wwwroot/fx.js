'use strict';
// Animated console-style backdrops drawn with one WebGL fragment shader.
//   waves     - a ribbon of thin light lines drifting over a soft gradient
//   particles - drifting specks of light over slow light bands
//   flow      - slow glossy folds of colour
//   orbs      - large soft bokeh orbs floating through haze
//   aurora    - hanging curtains of light
// Every mode reacts to: parallax shift (selection), a ripple pulse (selection change), the mouse, and a surge (launch).

const FX_MODES = { waves: 0, particles: 1, flow: 2, orbs: 3, aurora: 4 };

const FX_FRAG = `
precision highp float;
uniform vec2 uRes;
uniform float uTime;
uniform vec3 uColor;
uniform float uMode;
uniform float uShift;
uniform vec3 uPulse;   // xy = position (0..1), z = age in seconds
uniform vec2 uMouse;   // 0..1
uniform float uSurge;  // 0..1

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
  float v = 0.0, a = 0.5;
  for (int i = 0; i < 5; i++) { v += a * noise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
  return v;
}

vec3 waves(vec2 uv, vec2 p, float t) {
  float band = 1.0 - abs(uv.y - 0.5) * 1.6;
  vec3 col = mix(uColor * 0.22, uColor * 0.95, clamp(band, 0.0, 1.0) * 0.8 + uv.x * 0.2);
  float x = p.x * 1.6;
  float lean = (uMouse.y - 0.5) * 0.05 * exp(-abs(p.x - uMouse.x * uRes.x / uRes.y) * 1.5);
  float lines = 0.0;
  for (int i = 0; i < 32; i++) {
    float fi = float(i);
    float y = 0.5 + lean
      + 0.09 * sin(x * 0.85 + t * 0.32 + fi * 0.035)
      + 0.05 * sin(x * 1.9 - t * 0.21 + fi * 0.08)
      + 0.02 * sin(x * 3.7 + t * 0.47 + fi * 0.02)
      + (fi - 16.0) * 0.0035 * sin(x * 0.6 + t * 0.15);
    float d = abs(uv.y - y);
    lines += (0.0011 / (d + 0.0011)) * (0.25 + 0.75 * sin(fi / 31.0 * 3.1416));
  }
  col += vec3(1.0) * lines * 0.042;
  float sheet = smoothstep(0.16, 0.0, abs(uv.y - (0.5 + lean + 0.09 * sin(x * 0.85 + t * 0.32 + 0.5))));
  col += vec3(1.0) * sheet * 0.08;
  return col;
}

vec3 particles(vec2 uv, vec2 p, float t) {
  vec3 col = mix(uColor * 0.12, uColor * 0.55, uv.y * 0.7 + 0.15);
  for (int k = 0; k < 2; k++) {
    float fk = float(k);
    float y = 0.42 + fk * 0.16 + 0.07 * sin(p.x * (1.1 + fk * 0.4) + t * (0.12 + fk * 0.05) + fk * 2.0);
    col += uColor * 0.55 * exp(-abs(uv.y - y) * (10.0 - fk * 3.0)) * (0.35 - fk * 0.1);
  }
  vec2 m = vec2(uMouse.x * uRes.x / uRes.y, uMouse.y);
  for (int l = 0; l < 3; l++) {
    float fl = float(l);
    float scale = 7.0 + fl * 6.0;
    vec2 q = p + (p - m) * 0.06 * exp(-length(p - m) * 4.0) * (1.0 - fl * 0.3);   // specks drift away from the mouse
    vec2 g = q * scale + vec2(t * (0.04 + fl * 0.015), -t * (0.10 + fl * 0.05));
    vec2 id = floor(g);
    vec2 f = fract(g) - 0.5;
    float h = hash(id + fl * 17.0);
    vec2 off = vec2(hash(id + 3.1), hash(id + 7.7)) - 0.5;
    float d = length(f - off * 0.7);
    float twinkle = 0.55 + 0.45 * sin(t * (0.8 + h * 2.0) + h * 6.283);
    float size = 0.07 - fl * 0.017;
    float s = smoothstep(size, 0.0, d) * step(0.6, h) * twinkle;
    col += mix(vec3(1.0), uColor + 0.4, 0.3) * s * (0.9 - fl * 0.25);
  }
  return col;
}

vec3 flow(vec2 uv, vec2 p, float t) {
  vec2 q = vec2(fbm(p * 1.1 + vec2(0.0, t * 0.04)), fbm(p * 1.1 + vec2(5.2, 1.3) - t * 0.035));
  vec2 r = vec2(fbm(p * 1.4 + q * 2.2 + vec2(1.7, 9.2) + t * 0.02), fbm(p * 1.4 + q * 2.2 + vec2(8.3, 2.8) - t * 0.025));
  float n = fbm(p * 1.3 + r * 1.8);
  vec3 col = mix(uColor * 0.1, uColor * 0.85, smoothstep(0.25, 0.85, n));
  col = mix(col, uColor * 0.35 + vec3(0.04, 0.05, 0.08), clamp(length(q) * 0.5, 0.0, 1.0) * 0.5);
  col += vec3(1.0) * pow(smoothstep(0.55, 0.9, n), 3.0) * 0.45;
  return col;
}

vec3 orbs(vec2 uv, vec2 p, float t) {
  vec3 col = mix(uColor * 0.08, uColor * 0.42, smoothstep(-0.2, 1.2, uv.y + uv.x * 0.3));
  col += uColor * 0.25 * fbm(p * 1.5 + t * 0.02);
  for (int i = 0; i < 14; i++) {
    float fi = float(i);
    float h1 = hash(vec2(fi, 1.3)), h2 = hash(vec2(fi, 7.1)), h3 = hash(vec2(fi, 4.7));
    float aspect = uRes.x / uRes.y;
    vec2 c = vec2(fract(h1 + t * (0.006 + h3 * 0.01)) * (aspect + 0.6) - 0.3,
                  0.5 + 0.38 * sin(t * (0.05 + h2 * 0.05) + fi * 1.7));
    float r = 0.05 + h3 * 0.16;
    float d = length(p - c);
    float body = smoothstep(r, r * 0.2, d);
    float rim = smoothstep(r * 0.08, 0.0, abs(d - r * 0.92)) * 0.6;
    vec3 tint = mix(vec3(1.0), uColor * 1.6 + 0.2, 0.55);
    col += tint * (body * (0.10 + h2 * 0.12) + rim * 0.12);
  }
  return col;
}

vec3 aurora(vec2 uv, vec2 p, float t) {
  vec3 col = mix(vec3(0.01, 0.015, 0.03), uColor * 0.18, uv.y);
  for (int i = 0; i < 4; i++) {
    float fi = float(i);
    float x = p.x * (0.9 + fi * 0.25) + t * (0.03 + fi * 0.012) + fi * 3.1;
    float base = 0.62 + fi * 0.06 + 0.12 * sin(x * 1.3 + fbm(vec2(x * 0.8, t * 0.05 + fi)) * 3.0);
    float curtain = smoothstep(0.0, 0.35, uv.y - base + 0.35) * smoothstep(0.25, 0.0, uv.y - base);
    float rays = 0.55 + 0.45 * fbm(vec2(x * 6.0, uv.y * 1.5 - t * 0.15));
    vec3 tint = mix(uColor * 1.4 + vec3(0.05, 0.25, 0.15), vec3(0.4, 1.0, 0.8), 0.25 + fi * 0.15);
    col += tint * curtain * rays * (0.35 - fi * 0.06);
  }
  float stars = step(0.997, hash(floor(uv * uRes / 2.0))) * (0.5 + 0.5 * sin(t * 2.0 + hash(floor(uv * uRes)) * 6.28));
  col += vec3(stars) * 0.5 * smoothstep(0.4, 1.0, uv.y);
  return col;
}

void main() {
  vec2 uv = gl_FragCoord.xy / uRes;
  vec2 p = vec2(uv.x * uRes.x / uRes.y + uShift, uv.y);
  float t = uTime;
  vec3 col = uMode < 0.5 ? waves(uv, p, t) : uMode < 1.5 ? particles(uv, p, t) : uMode < 2.5 ? flow(uv, p, t) : uMode < 3.5 ? orbs(uv, p, t) : aurora(uv, p, t);

  // ripple from the newly selected tile
  vec2 a = vec2(uRes.x / uRes.y, 1.0);
  float dist = length((uv - uPulse.xy) * a);
  float age = uPulse.z;
  float ring = exp(-abs(dist - age * 0.75) * 18.0) * exp(-age * 2.2);
  float glow = exp(-dist * 5.0) * exp(-age * 3.0);
  col += (vec3(1.0) * 0.7 + uColor * 0.5) * (ring * 0.18 + glow * 0.12);

  // launch surge: lift and wash out toward white
  col = mix(col, col * 1.8 + uColor * 0.4 + 0.15, uSurge);

  float vig = smoothstep(1.25, 0.35, length(uv - vec2(0.55, 0.5)));
  col *= 0.55 + 0.45 * vig;
  col += (hash(gl_FragCoord.xy + fract(t)) - 0.5) / 255.0;
  gl_FragColor = vec4(col, 1.0);
}`;

const FX_VERT = 'attribute vec2 a; void main() { gl_Position = vec4(a, 0.0, 1.0); }';

function hexToRgb(hex) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex || '');
  const n = m ? parseInt(m[1], 16) : 0x2369b8;
  return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255];
}

/** Attach an animated backdrop to a canvas. scale < 1 renders below screen resolution to save GPU. */
/** Compile the backdrop shader into a WebGL context; returns its uniform locations. */
function fxProgram(gl) {
  const sh = (type, src) => { const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s); if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; };
  const prog = gl.createProgram();
  gl.attachShader(prog, sh(gl.VERTEX_SHADER, FX_VERT));
  gl.attachShader(prog, sh(gl.FRAGMENT_SHADER, FX_FRAG));
  gl.linkProgram(prog);
  gl.useProgram(prog);
  gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
  const loc = gl.getAttribLocation(prog, 'a');
  gl.enableVertexAttribArray(loc);
  gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
  const u = name => gl.getUniformLocation(prog, name);
  return { res: u('uRes'), time: u('uTime'), color: u('uColor'), mode: u('uMode'), shift: u('uShift'), pulse: u('uPulse'), mouse: u('uMouse'), surge: u('uSurge') };
}

/**
 * Small animated previews (config page) all share ONE hidden WebGL context and copy its frames into
 * plain 2D canvases, so redrawing a page never piles up WebGL contexts (browsers cap them at ~16 and
 * drop the oldest, which used to be the main backdrop).
 */
const fxPreviews = (() => {
  const W = 192, H = 108;
  let gl = null, U = null, raf = 0;
  const glCanvas = document.createElement('canvas');
  glCanvas.width = W; glCanvas.height = H;
  glCanvas.addEventListener('webglcontextlost', e => { e.preventDefault(); gl = null; });
  glCanvas.addEventListener('webglcontextrestored', () => { gl = null; });
  const list = new Set();
  const t0 = performance.now();
  function frame(now) {
    raf = 0;
    for (const p of [...list]) if (!p.canvas.isConnected) list.delete(p);
    if (!list.size) return;
    if (!gl) {
      gl = glCanvas.getContext('webgl', { antialias: false, alpha: false, preserveDrawingBuffer: true });
      if (!gl) return;
      U = fxProgram(gl);
      gl.viewport(0, 0, W, H);
    }
    for (const p of list) {
      gl.uniform2f(U.res, W, H);
      gl.uniform1f(U.time, (now - t0) / 1000);
      gl.uniform3f(U.color, p.color[0], p.color[1], p.color[2]);
      gl.uniform1f(U.mode, p.mode);
      gl.uniform1f(U.shift, 0);
      gl.uniform3f(U.pulse, 0.5, 0.5, 99);
      gl.uniform2f(U.mouse, 0.5, 0.5);
      gl.uniform1f(U.surge, 0);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
      if (p.canvas.width !== W) { p.canvas.width = W; p.canvas.height = H; }
      p.ctx.drawImage(glCanvas, 0, 0);
    }
    if (!document.hidden) raf = requestAnimationFrame(frame);
  }
  document.addEventListener('visibilitychange', () => { if (!raf && list.size) raf = requestAnimationFrame(frame); });
  return {
    /** Show an animated preview of a mode in a normal canvas element. */
    add(canvas, mode, hex) {
      list.add({ canvas, ctx: canvas.getContext('2d'), mode: FX_MODES[mode] ?? 0, color: hexToRgb(hex) });
      if (!raf) raf = requestAnimationFrame(frame);
    },
  };
})();

function createFx(canvas, { scale = 0.6, animate = () => true, interactive = false } = {}) {
  let gl = canvas.getContext('webgl', { antialias: false, alpha: false, preserveDrawingBuffer: false });
  if (!gl) return null;
  let U = fxProgram(gl);
  let lost = false;
  // If the GPU or browser drops the context, wait for it to come back and rebuild instead of staying black.
  canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); lost = true; cancelAnimationFrame(raf); raf = 0; });
  canvas.addEventListener('webglcontextrestored', () => { lost = false; U = fxProgram(gl); canvas.width = 0; kick(); });

  let mode = 0, color = hexToRgb('#2369b8'), from = color, to = color, blendStart = 0, raf = 0, dead = false;
  let shift = 0, shiftTarget = 0, pulse = [0.5, 0.5, 99], pulseAt = -1e9, mouse = [0.5, 0.5], mouseTarget = [0.5, 0.5], surge = 0, surgeTarget = 0;
  let last = performance.now();
  const t0 = performance.now() - Math.random() * 60000;

  function size() {
    const w = Math.max(1, Math.round(canvas.clientWidth * scale)), h = Math.max(1, Math.round(canvas.clientHeight * scale));
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; gl.viewport(0, 0, w, h); }
  }
  function frame(now) {
    if (dead || lost) { raf = 0; return; }
    raf = 0;
    size();
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    const k = Math.min(1, (now - blendStart) / 700);
    const e = k * k * (3 - 2 * k);
    color = from.map((c, i) => c + (to[i] - c) * e);
    const ease = 1 - Math.exp(-dt * 3.2);
    shift += (shiftTarget - shift) * ease;
    mouse = mouse.map((m, i) => m + (mouseTarget[i] - m) * (1 - Math.exp(-dt * 2)));
    surge += (surgeTarget - surge) * (1 - Math.exp(-dt * (surgeTarget > surge ? 5 : 2.5)));
    gl.uniform2f(U.res, canvas.width, canvas.height);
    gl.uniform1f(U.time, (now - t0) / 1000);
    gl.uniform3f(U.color, color[0], color[1], color[2]);
    gl.uniform1f(U.mode, mode);
    gl.uniform1f(U.shift, shift);
    gl.uniform3f(U.pulse, pulse[0], pulse[1], (now - pulseAt) / 1000);
    gl.uniform2f(U.mouse, mouse[0], mouse[1]);
    gl.uniform1f(U.surge, surge);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    if (animate() && !document.hidden) raf = requestAnimationFrame(frame);
  }
  const kick = () => { if (!raf && !dead && !lost) { last = performance.now(); raf = requestAnimationFrame(frame); } };
  const onMove = e => { mouseTarget = [e.clientX / innerWidth, 1 - e.clientY / innerHeight]; };
  document.addEventListener('visibilitychange', kick);
  window.addEventListener('resize', kick);
  if (interactive) window.addEventListener('mousemove', onMove);
  kick();
  return {
    set(newMode, hex) {
      mode = FX_MODES[newMode] ?? 0;
      from = color;
      to = hexToRgb(hex);
      blendStart = performance.now();
      kick();
    },
    /** Slide the scene sideways (parallax with the selected tile). */
    shift(v) { shiftTarget = v; kick(); },
    /** Send a ripple out from a screen point (CSS pixels). */
    pulse(x, y) { pulse = [x / innerWidth, 1 - y / innerHeight, 0]; pulseAt = performance.now(); kick(); },
    /** 0..1 brightness surge, used while FL is starting. */
    surge(v) { surgeTarget = v; kick(); },
    kick,
    destroy() {
      dead = true;
      cancelAnimationFrame(raf);
      document.removeEventListener('visibilitychange', kick);
      window.removeEventListener('mousemove', onMove);
      const x = gl.getExtension('WEBGL_lose_context');
      if (x) x.loseContext();
    },
  };
}
