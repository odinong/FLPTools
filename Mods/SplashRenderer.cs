using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FLPTools.Mods;

/// <summary>
/// Draws FL Studio startup animations: a horizontal strip of frames that FL plays on its loading screen
/// (theme key SplashAnim / registry SplashImage). The last frame is held while FL finishes loading,
/// so every style ends on the icon at rest.
/// </summary>
public static class SplashRenderer
{
    public const int Frames = 60, W = 400, H = 569;

    public static readonly (string Id, string Label)[] Styles =
    {
        ("bloom", "Bloom"), ("waveform", "Waveform"), ("equalizer", "Equalizer"), ("neon", "Neon"),
        ("assemble", "Assemble"), ("pulse", "Pulse"), ("glitch", "Glitch"),
    };

    static float C01(float v) => Math.Clamp(v, 0f, 1f);
    static float Seg(float t, float a, float b) => C01((t - a) / (b - a));
    static float Out3(float t) => 1 - MathF.Pow(1 - C01(t), 3);
    static float InOut(float t) { t = C01(t); return t * t * (3 - 2 * t); }
    static float Back(float t, float k = 1.7f) { t = C01(t); return 1 + (k + 1) * MathF.Pow(t - 1, 3) + k * MathF.Pow(t - 1, 2); }
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)Lerp(a.R, b.R, C01(t)), (int)Lerp(a.G, b.G, C01(t)), (int)Lerp(a.B, b.B, C01(t)));
    static Color A(Color c, float alpha) => Color.FromArgb((int)(255 * C01(alpha)), c);

    sealed class Frame
    {
        public Graphics G = null!;
        public Bitmap Icon = null!;
        public float Cx, Cy, S, T, Size;
        public Color Accent;
        public Random Rnd = null!;

        /// <summary>Soft round glow with a smooth falloff.</summary>
        public void Glow(float r, float alpha, Color c, float x = float.NaN, float y = float.NaN)
        {
            if (r < 1 || alpha < 0.005f) return;
            x = float.IsNaN(x) ? Cx : x; y = float.IsNaN(y) ? Cy : y;
            using var path = new GraphicsPath();
            path.AddEllipse(x - r, y - r, r * 2, r * 2);
            using var br = new PathGradientBrush(path)
            {
                CenterColor = A(c, alpha),
                SurroundColors = new[] { A(c, 0) },
                Blend = new Blend { Positions = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, Factors = new[] { 0f, 0.12f, 0.4f, 0.78f, 1f } },
            };
            G.FillEllipse(br, x - r, y - r, r * 2, r * 2);
        }

        /// <summary>A ring with a neon-style glow: wide faint strokes under a thin bright one.</summary>
        public void Ring(float r, float width, float alpha, Color c, float sweepDeg = 360, float startDeg = -90)
        {
            if (r < 1 || alpha < 0.005f || sweepDeg <= 0) return;
            var rect = new RectangleF(Cx - r, Cy - r, r * 2, r * 2);
            foreach (var (w, a) in new[] { (width * 6, 0.08f), (width * 3, 0.18f), (width * 1.6f, 0.45f), (width, 1f) })
            {
                using var pen = new Pen(A(w == width ? Mix(c, Color.White, 0.55f) : c, alpha * a), Math.Max(1, w)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                if (sweepDeg >= 360) G.DrawEllipse(pen, rect); else G.DrawArc(pen, rect, startDeg, sweepDeg);
            }
        }

        /// <summary>The FL icon. whiten blends it toward white; slice draws only a horizontal band of it.</summary>
        public void DrawIcon(float size, float alpha, float angle = 0, float dx = 0, float dy = 0, float whiten = 0,
            float[]? channels = null, RectangleF? srcBand = null)
        {
            if (size < 1 || alpha < 0.01f) return;
            var st = G.Save();
            G.TranslateTransform(Cx + dx, Cy + dy);
            if (angle != 0) G.RotateTransform(angle);
            float k = 1 - C01(whiten);
            var m = new ColorMatrix(new[]
            {
                new[] { k * (channels?[0] ?? 1), 0f, 0f, 0f, 0f },
                new[] { 0f, k * (channels?[1] ?? 1), 0f, 0f, 0f },
                new[] { 0f, 0f, k * (channels?[2] ?? 1), 0f, 0f },
                new[] { 0f, 0f, 0f, C01(alpha), 0f },
                new[] { whiten, whiten, whiten, 0f, 1f },
            });
            using var ia = new ImageAttributes();
            ia.SetColorMatrix(m);
            if (srcBand is RectangleF band)
            {
                float sx = size / Icon.Width;
                var dst = new Rectangle((int)(-size / 2 + band.X * sx), (int)(-size / 2 + band.Y * sx), (int)Math.Ceiling(band.Width * sx), (int)Math.Ceiling(band.Height * sx));
                G.DrawImage(Icon, dst, band.X, band.Y, band.Width, band.Height, GraphicsUnit.Pixel, ia);
            }
            else G.DrawImage(Icon, new Rectangle((int)(-size / 2), (int)(-size / 2), (int)size, (int)size), 0, 0, Icon.Width, Icon.Height, GraphicsUnit.Pixel, ia);
            G.Restore(st);
        }

        /// <summary>A diagonal light band sweeping across the round icon.</summary>
        public void Glint(float size, float p)
        {
            if (p <= 0 || p >= 1) return;
            var st = G.Save();
            using var clip = new GraphicsPath();
            float r = size * 0.47f;
            clip.AddEllipse(Cx - r, Cy - r, r * 2, r * 2);
            G.SetClip(clip, CombineMode.Intersect);
            G.TranslateTransform(Cx, Cy);
            G.RotateTransform(25);
            float x = Lerp(-size, size, p);
            var band = new RectangleF(x - size * 0.18f, -size, size * 0.36f, size * 2);
            using var br = new LinearGradientBrush(band, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend { Colors = new[] { A(Color.White, 0), A(Color.White, 0.55f), A(Color.White, 0) }, Positions = new[] { 0f, 0.5f, 1f } },
            };
            G.FillRectangle(br, band);
            G.Restore(st);
        }

        /// <summary>The resting state every style ends on.</summary>
        public void Rest(float amount)
        {
            Glow(Size * 0.95f, 0.22f * amount, Accent);
            DrawIcon(Size, amount);
        }
    }

    public static Bitmap Render(string style, Color accent, Bitmap icon, float scale)
    {
        int fw = (int)(W * scale), fh = (int)(H * scale);
        var strip = new Bitmap(fw * Frames, fh, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(strip);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        // per-style random data that must stay the same across frames
        var seed = new Random(1234);
        var tiles = Enumerable.Range(0, 36).Select(_ => (dir: seed.NextDouble() * Math.PI * 2, dist: 0.8 + seed.NextDouble() * 0.8, rot: (seed.NextDouble() - 0.5) * 160, delay: seed.NextDouble() * 0.22)).ToArray();
        var bars = Enumerable.Range(0, 11).Select(_ => (f1: 9 + seed.NextDouble() * 8, f2: 13 + seed.NextDouble() * 10, ph: seed.NextDouble() * 6.28)).ToArray();

        for (int i = 0; i < Frames; i++)
        {
            var f = new Frame
            {
                G = g, Icon = icon, Accent = accent, S = scale, T = i / (float)(Frames - 1),
                Cx = i * fw + fw / 2f, Cy = fh / 2f, Size = 220 * scale, Rnd = new Random(i * 7919 + 13),
            };
            g.SetClip(new RectangleF(i * fw, 0, fw, fh));
            switch (style)
            {
                case "waveform": Waveform(f); break;
                case "equalizer": Equalizer(f, bars); break;
                case "neon": Neon(f); break;
                case "assemble": Assemble(f, tiles); break;
                case "pulse": Pulse(f); break;
                case "glitch": Glitch(f); break;
                default: Bloom(f); break;
            }
            g.ResetClip();
        }
        return strip;
    }

    // ------------------------------------------------------------------ styles

    static void Bloom(Frame f)
    {
        float t = f.T, s = f.S;
        float core = Out3(Seg(t, 0, 0.32f));
        float fade = 1 - Seg(t, 0.45f, 0.8f);
        f.Glow(Lerp(6, 190, core) * s, 0.85f * fade + 0.0f, Mix(Color.White, f.Accent, Seg(t, 0.1f, 0.5f)));
        // rays
        float rays = Seg(t, 0.08f, 0.35f) * (1 - Seg(t, 0.45f, 0.75f));
        if (rays > 0)
        {
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * MathF.PI * 2 + t * 0.9f;
                float len = (120 + (k % 3) * 40) * s * Out3(Seg(t, 0.05f, 0.4f));
                using var pen = new Pen(A(k % 2 == 0 ? Color.White : f.Accent, rays * 0.5f), (k % 3 == 0 ? 2.2f : 1.2f) * s) { EndCap = LineCap.Round };
                f.G.DrawLine(pen, f.Cx + MathF.Cos(a) * 20 * s, f.Cy + MathF.Sin(a) * 20 * s, f.Cx + MathF.Cos(a) * len, f.Cy + MathF.Sin(a) * len);
            }
        }
        float show = Seg(t, 0.22f, 0.55f);
        f.Glow(f.Size * 0.95f, 0.22f * Seg(t, 0.5f, 0.8f), f.Accent);
        f.DrawIcon(f.Size * Lerp(0.8f, 1f, Out3(show)), show, 0, 0, 0, 1 - Seg(t, 0.3f, 0.72f));
        f.Glint(f.Size, Seg(t, 0.74f, 0.94f));
    }

    static void Waveform(Frame f)
    {
        float t = f.T, s = f.S;
        float draw = Out3(Seg(t, 0, 0.42f));
        float collapse = InOut(Seg(t, 0.42f, 0.62f));
        float lineAlpha = 1 - Seg(t, 0.58f, 0.68f);
        if (lineAlpha > 0)
        {
            float half = (W / 2f - 24) * s * (1 - collapse * 0.97f);
            int n = 140;
            var pts = new List<PointF>();
            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n;                       // 0..1 across
                if (u > draw) break;
                float x = f.Cx - half + u * half * 2;
                float env = MathF.Sin(u * MathF.PI) * (1 - collapse);
                float wave = MathF.Sin(u * 23 + t * 18) * 0.55f + MathF.Sin(u * 61 - t * 25) * 0.3f + MathF.Sin(u * 9 + t * 6) * 0.25f;
                pts.Add(new PointF(x, f.Cy + wave * env * 70 * s));
            }
            if (pts.Count > 1)
            {
                var arr = pts.ToArray();
                foreach (var (w, a) in new[] { (12f, 0.07f), (6f, 0.18f), (2.4f, 0.9f) })
                {
                    using var pen = new Pen(A(w < 3 ? Mix(f.Accent, Color.White, 0.5f) : f.Accent, a * lineAlpha), w * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                    f.G.DrawLines(pen, arr);
                }
                var head = arr[^1];
                if (draw < 1) f.Glow(16 * s, 0.9f, Color.White, head.X, head.Y);
            }
        }
        float pop = Seg(t, 0.58f, 0.8f);
        f.Glow(Lerp(10, 160, Out3(Seg(t, 0.55f, 0.7f))) * s, 0.7f * (1 - Seg(t, 0.68f, 0.9f)), Color.White);
        float rp = Seg(t, 0.6f, 0.9f);
        if (rp > 0 && rp < 1) f.Ring(f.Size * Lerp(0.5f, 0.95f, Out3(rp)), 2.2f * s, 1 - rp, f.Accent);
        f.Glow(f.Size * 0.95f, 0.22f * Seg(t, 0.65f, 0.9f), f.Accent);
        f.DrawIcon(f.Size * Back(pop), Seg(t, 0.58f, 0.66f));
    }

    static void Equalizer(Frame f, (double f1, double f2, double ph)[] bars)
    {
        float t = f.T, s = f.S;
        float gather = InOut(Seg(t, 0.5f, 0.68f));
        float barsAlpha = 1 - Seg(t, 0.62f, 0.72f);
        float bw = 14 * s, gap = 9 * s;
        float total = bars.Length * bw + (bars.Length - 1) * gap;
        for (int k = 0; k < bars.Length; k++)
        {
            if (barsAlpha <= 0) break;
            var b = bars[k];
            float rise = Out3(Seg(t, 0.02f + k * 0.012f, 0.25f + k * 0.012f));
            float level = (float)(0.35 + 0.65 * Math.Abs(Math.Sin(t * b.f1 + b.ph) * 0.7 + Math.Sin(t * b.f2) * 0.3));
            float shape = 1 - MathF.Abs(k - (bars.Length - 1) / 2f) / bars.Length;            // taller in the middle
            float hgt = (40 + 150 * level * (0.5f + shape)) * s * rise * (1 - gather * 0.92f);
            float x0 = f.Cx - total / 2 + k * (bw + gap);
            float x = Lerp(x0, f.Cx - bw / 2, gather);
            var rect = new RectangleF(x, f.Cy - hgt / 2, bw, Math.Max(bw, hgt));
            using var path = RoundRect(rect, bw / 2);
            using var br = new LinearGradientBrush(new RectangleF(rect.X, rect.Y - 1, rect.Width, rect.Height + 2), A(Color.White, barsAlpha), A(f.Accent, barsAlpha), LinearGradientMode.Vertical);
            f.Glow(bw * 1.6f, 0.12f * barsAlpha, f.Accent, x + bw / 2, f.Cy);
            f.G.FillPath(br, path);
        }
        float pop = Seg(t, 0.62f, 0.84f);
        f.Glow(Lerp(10, 150, Out3(Seg(t, 0.6f, 0.72f))) * s, 0.6f * (1 - Seg(t, 0.7f, 0.92f)), Color.White);
        f.Glow(f.Size * 0.95f, 0.22f * Seg(t, 0.7f, 0.92f), f.Accent);
        f.DrawIcon(f.Size * Back(pop), Seg(t, 0.62f, 0.7f));
        f.Glint(f.Size, Seg(t, 0.84f, 0.98f));
    }

    static void Neon(Frame f)
    {
        float t = f.T, s = f.S;
        float r = f.Size * 0.66f;
        float sweep = 360 * InOut(Seg(t, 0.02f, 0.45f));
        // flicker: on/off pattern right after the ring closes, then steady
        float flick = t < 0.45f ? 1 : t < 0.58f ? (new[] { 1f, 0.15f, 0.9f, 0.3f, 1f, 0.6f, 1f, 1f })[(int)((t - 0.45f) / 0.13f * 8) % 8] : 1;
        f.Ring(r, 2.6f * s, flick, f.Accent, sweep, -90 + t * 120);
        if (t < 0.45f && sweep > 1)
        {
            float a = (-90 + t * 120 + sweep) * MathF.PI / 180;
            f.Glow(18 * s, 0.95f, Color.White, f.Cx + MathF.Cos(a) * r, f.Cy + MathF.Sin(a) * r);
        }
        float icon = Seg(t, 0.52f, 0.7f);
        float iconFlick = t < 0.62f ? (((int)(t * 120) % 3 == 0) ? 0.35f : 1f) : 1f;
        f.Glow(f.Size * 0.95f, 0.25f * Seg(t, 0.55f, 0.85f) * flick, f.Accent);
        f.DrawIcon(f.Size * Lerp(0.94f, 1f, Out3(icon)), icon * iconFlick, 0, 0, 0, 0.6f * (1 - Seg(t, 0.55f, 0.8f)));
        f.Glint(f.Size, Seg(t, 0.8f, 0.97f));
    }

    static void Assemble(Frame f, (double dir, double dist, double rot, double delay)[] tiles)
    {
        float t = f.T, s = f.S;
        int n = 6;
        float cell = f.Icon.Width / (float)n;
        float dcell = f.Size / n;
        f.Glow(f.Size * 0.95f, 0.22f * Seg(t, 0.55f, 0.85f), f.Accent);
        for (int k = 0; k < n * n; k++)
        {
            var tl = tiles[k];
            float p = Out3(Seg(t, 0.05f + (float)tl.delay, 0.5f + (float)tl.delay * 0.6f));
            if (p <= 0) continue;
            int cx = k % n, cy = k / n;
            float hx = -f.Size / 2 + (cx + 0.5f) * dcell, hy = -f.Size / 2 + (cy + 0.5f) * dcell;      // home offset
            float far = (float)tl.dist * 260 * s;
            float ox = hx + (float)Math.Cos(tl.dir) * far * (1 - p), oy = hy + (float)Math.Sin(tl.dir) * far * (1 - p);
            var st = f.G.Save();
            f.G.TranslateTransform(f.Cx + ox, f.Cy + oy);
            f.G.RotateTransform((float)tl.rot * (1 - p));
            var m = new ColorMatrix { Matrix33 = C01(p * 1.4f) };
            using var ia = new ImageAttributes();
            ia.SetColorMatrix(m);
            f.G.DrawImage(f.Icon, new Rectangle((int)(-dcell / 2) - 1, (int)(-dcell / 2) - 1, (int)dcell + 2, (int)dcell + 2), cx * cell, cy * cell, cell, cell, GraphicsUnit.Pixel, ia);
            f.G.Restore(st);
        }
        // once settled, draw the whole icon over the seams
        f.DrawIcon(f.Size, Seg(t, 0.66f, 0.74f));
        f.Glint(f.Size, Seg(t, 0.74f, 0.94f));
    }

    static void Pulse(Frame f)
    {
        float t = f.T, s = f.S;
        float[] beats = { 0.22f, 0.37f, 0.52f, 0.67f };
        float bump = 0, flash = 0;
        foreach (var b in beats)
        {
            float d = t - b;
            if (d >= 0)
            {
                float e = MathF.Exp(-d * 28);
                bump += e * 0.09f;
                flash += e;
                float rp = Seg(d, 0, 0.3f);
                if (rp < 1) f.Ring(f.Size * Lerp(0.5f, 1.15f, Out3(rp)), 2 * s, (1 - rp) * 0.9f, f.Accent);
            }
        }
        float show = Seg(t, 0.04f, 0.2f);
        f.Glow(f.Size * (0.9f + flash * 0.25f), (0.16f + C01(flash) * 0.3f) * show, f.Accent);
        f.DrawIcon(f.Size * Lerp(0.85f, 1f, Out3(show)) * (1 + bump), show, 0, 0, 0, C01(flash) * 0.25f);
        f.Glint(f.Size, Seg(t, 0.8f, 0.97f));
    }

    static void Glitch(Frame f)
    {
        float t = f.T, s = f.S;
        float settle = Seg(t, 0.12f, 0.7f);
        float chaos = 1 - settle;
        float alpha = Seg(t, 0, 0.08f);
        bool blank = t < 0.55f && f.Rnd.NextDouble() < 0.18 * chaos;
        if (blank) return;
        float jit = chaos * 26 * s;
        f.Glow(f.Size * 0.95f, 0.2f * settle, f.Accent);
        // colour-split ghosts
        f.DrawIcon(f.Size, alpha * 0.8f * chaos, 0, -jit * (0.4f + (float)f.Rnd.NextDouble()), 0, 0, new[] { 1f, 0.15f, 0.15f });
        f.DrawIcon(f.Size, alpha * 0.8f * chaos, 0, jit * (0.4f + (float)f.Rnd.NextDouble()), 0, 0, new[] { 0.1f, 0.7f, 1f });
        // the icon in horizontal slices, each shoved sideways
        int slices = 9;
        float bandH = f.Icon.Height / (float)slices;
        for (int k = 0; k < slices; k++)
        {
            float shove = f.Rnd.NextDouble() < 0.45 * chaos ? (float)(f.Rnd.NextDouble() - 0.5) * 90 * s * chaos : 0;
            f.DrawIcon(f.Size, alpha * (0.45f + 0.55f * settle), 0, shove, 0, 0, null, new RectangleF(0, k * bandH, f.Icon.Width, bandH + 1));
        }
        // scanline bars
        for (int k = 0; k < (int)(6 * chaos); k++)
        {
            float y = f.Cy + (float)(f.Rnd.NextDouble() - 0.5) * f.Size * 1.2f;
            using var br = new SolidBrush(A(k % 2 == 0 ? f.Accent : Color.White, 0.5f * chaos));
            f.G.FillRectangle(br, f.Cx - f.Size * 0.7f + (float)(f.Rnd.NextDouble() - 0.5) * 60 * s, y, f.Size * 1.4f, (1 + (float)f.Rnd.NextDouble() * 3) * s);
        }
        f.Glint(f.Size, Seg(t, 0.78f, 0.96f));
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
