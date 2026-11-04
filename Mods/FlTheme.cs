using System.Drawing;
using System.Globalization;
using System.Text;

namespace FLPTools.Mods;

/// <summary>
/// An FL Studio .flstheme file: Key=Value lines (UTF-8 with BOM). Colour values are signed
/// 32-bit ARGB integers; -1 means "use the default".
/// </summary>
public sealed class FlTheme
{
    public readonly List<KeyValuePair<string, string>> Values = new();

    public static readonly string[] Sliders = { "Hue", "Saturation", "Lightness", "Contrast", "Text" };

    public static bool IsColorKey(string key) =>
        key is "Selected" or "Highlight" or "Mute" or "Option" or "StepEven" or "StepOdd" or "TextColor" or "BackColor"
        || key.StartsWith("Meter") || key.StartsWith("WaveClr") || key.StartsWith("NoteColor");

    public string? this[string key]
    {
        get => Values.FirstOrDefault(v => v.Key == key).Value;
        set
        {
            int i = Values.FindIndex(v => v.Key == key);
            if (value == null) { if (i >= 0) Values.RemoveAt(i); return; }
            if (i >= 0) Values[i] = new(key, value);
            else Values.Add(new(key, value));
        }
    }

    public static FlTheme Load(string path)
    {
        var t = new FlTheme();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) t.Values.Add(new(line[..eq].Trim('﻿', ' '), line[(eq + 1)..]));
        }
        return t;
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in Values) sb.Append(k).Append('=').Append(v).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static Color? ToColor(string? v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i != -1
            ? Color.FromArgb(255, Color.FromArgb(i)) : null;

    public static string FromColor(Color c) => unchecked((int)(0xFF000000u | (uint)(c.R << 16 | c.G << 8 | c.B))).ToString(CultureInfo.InvariantCulture);

    public static FlTheme Default()
    {
        var t = new FlTheme();
        string[] keys =
        {
            "Hue=0", "Saturation=0", "Lightness=0", "Contrast=0", "Text=255", "Selected=-1", "Highlight=-1", "Mute=-1",
            "Option=-1", "StepEven=-1", "StepOdd=-1", "Lightmode=1", "OverrideClips=0", "TextColor=-1",
            "Meter0=-1", "Meter1=-1", "Meter2=-1", "Meter3=-1", "Meter4=-1", "Meter5=-1",
            "PRGridback=5129268", "PRGridCustom=0", "PRGridContrast=100", "PLGridback=5129268", "PLGridCustom=0",
            "PLGridContrast=100", "EEGridback=5129268", "EEGridCustom=0", "EEGridContrast=100", "BackMode=0", "BackColor=-1",
        };
        foreach (var kv in keys) { var p = kv.Split('='); t.Values.Add(new(p[0], p[1])); }
        return t;
    }

    // ------------------------------------------------------------ generation

    public enum Scheme { Monochrome, Complementary, Analogous, Neon, Pastel, Random }

    /// <summary>Fill every colour slot from a base colour. The UI hue/sat sliders follow the base colour too.</summary>
    public static FlTheme Generate(Color baseColor, Scheme scheme, bool dark, int seed)
    {
        var rnd = new Random(seed);
        var t = Default();
        float h = baseColor.GetHue(), s = baseColor.GetSaturation();
        if (scheme == Scheme.Random) { h = rnd.Next(360); s = 0.4f + (float)rnd.NextDouble() * 0.6f; }

        // FL's sliders: hue rotates the stock blue-grey UI (about 200 degrees); saturation/lightness -256..256
        int hue = (int)Math.Round(((h - 200 + 540) % 360) - 180);
        t["Hue"] = hue.ToString();
        t["Saturation"] = ((int)Math.Round((s - 0.35) * 300)).ToString();
        t["Lightness"] = (dark ? -180 - rnd.Next(60) : 60 + rnd.Next(80)).ToString();
        t["Contrast"] = (dark ? -10 : 10).ToString();

        float accentH = scheme switch
        {
            Scheme.Complementary => (h + 180) % 360,
            Scheme.Analogous => (h + 30) % 360,
            _ => h,
        };
        float sat = scheme switch { Scheme.Pastel => 0.35f, Scheme.Neon => 1f, Scheme.Monochrome => 0.25f, _ => Math.Max(0.5f, s) };
        float lum = scheme switch { Scheme.Pastel => 0.8f, Scheme.Neon => 0.55f, _ => 0.6f };

        t["Selected"] = FromColor(Hsl(accentH, sat, lum));
        t["Highlight"] = FromColor(Hsl((accentH + 20) % 360, sat, lum + 0.05f));
        t["Mute"] = FromColor(Hsl(h, 0.15f, dark ? 0.35f : 0.65f));
        t["Option"] = FromColor(Hsl((accentH + 330) % 360, sat, lum));
        t["StepEven"] = FromColor(Hsl(h, sat * 0.5f, dark ? 0.32f : 0.75f));
        t["StepOdd"] = FromColor(Hsl(h, sat * 0.5f, dark ? 0.22f : 0.65f));
        t["TextColor"] = FromColor(dark ? Hsl(h, 0.2f, 0.9f) : Hsl(h, 0.3f, 0.12f));
        t["OverrideClips"] = "1";
        for (int i = 0; i < 6; i++)
            t[$"Meter{i}"] = FromColor(Hsl((accentH + i * 25) % 360, sat, 0.45f + i * 0.05f));
        float[] spots = { 0, 0.2f, 0.4f, 0.6f, 0.85f, 1 };
        for (int i = 0; i < 6; i++)
        {
            t[$"WaveClr{i}"] = FromColor(Hsl((h + i * 12) % 360, sat, 0.35f + i * 0.09f));
            t[$"WaveSpc{i}"] = spots[i].ToString(CultureInfo.InvariantCulture);
        }
        var notes = NotePalette(scheme == Scheme.Random ? Scheme.Neon : scheme, baseColor, rnd);
        for (int i = 0; i < 16; i++) t[$"NoteColor{i}"] = FromColor(notes[i]);
        t["BackColor"] = FromColor(Hsl(h, s * 0.4f, dark ? 0.08f : 0.9f));
        return t;
    }

    public static Color[] NotePalette(Scheme scheme, Color baseColor, Random rnd)
    {
        float h = baseColor.GetHue();
        var c = new Color[16];
        for (int i = 0; i < 16; i++)
        {
            c[i] = scheme switch
            {
                Scheme.Monochrome => Hsl(h, 0.5f, 0.25f + i * 0.04f),
                Scheme.Complementary => Hsl((h + (i % 2) * 180 + i * 4) % 360, 0.7f, 0.5f + (i / 2) * 0.03f),
                Scheme.Analogous => Hsl((h - 40 + i * 5.33f + 360) % 360, 0.7f, 0.55f),
                Scheme.Pastel => Hsl((h + i * 22.5f) % 360, 0.6f, 0.8f),
                Scheme.Neon => Hsl((h + i * 22.5f) % 360, 1f, 0.55f),
                _ => Hsl(rnd.Next(360), 0.5f + (float)rnd.NextDouble() * 0.5f, 0.4f + (float)rnd.NextDouble() * 0.35f),
            };
        }
        return c;
    }

    /// <summary>Write a piano roll note colour preset (.ncp): ColorN=AARRGGBB.</summary>
    public static void SaveNotePalette(string path, Color[] colors)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < colors.Length; i++) sb.Append($"Color{i}=FF{colors[i].R:X2}{colors[i].G:X2}{colors[i].B:X2}\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static Color Hsl(float h, float s, float l)
    {
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        float m = l - c / 2;
        (float r, float g, float b) = (h % 360) switch
        {
            < 60 => (c, x, 0f), < 120 => (x, c, 0f), < 180 => (0f, c, x),
            < 240 => (0f, x, c), < 300 => (x, 0f, c), _ => (c, 0f, x),
        };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
}
