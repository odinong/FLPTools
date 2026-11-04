using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FLPTools.Flp;

/// <summary>
/// Lossless FLP &lt;-&gt; JSON conversion. Every event keeps its numeric id and order; payloads are
/// shown as numbers, text, decoded record tables (notes, playlist clips, mixer params) or hex/base64.
/// </summary>
public static class FlpJson
{
    public const string ToolName = "FLPTools";
    public const int SchemaVersion = 1;
    const int HexLimit = 256;

    enum F { U8, U16, U32, F32 }

    sealed record Layout(string Type, (string Name, F Type)[] Fields)
    {
        public int Size => Fields.Sum(f => f.Type switch { F.U8 => 1, F.U16 => 2, _ => 4 });
    }

    static readonly Layout Note = new("note", new[]
    {
        ("position", F.U32), ("flags", F.U16), ("channel", F.U16), ("length", F.U32), ("key", F.U16),
        ("group", F.U16), ("finePitch", F.U8), ("unknown1", F.U8), ("release", F.U8), ("midiChannel", F.U8),
        ("pan", F.U8), ("velocity", F.U8), ("modX", F.U8), ("modY", F.U8),
    });

    static readonly Layout PlaylistClip = new("playlistClip", new[]
    {
        ("position", F.U32), ("patternBase", F.U16), ("itemIndex", F.U16), ("length", F.U32),
        ("trackIndexReversed", F.U16), ("group", F.U16), ("unknown1", F.U16), ("flags", F.U16),
        ("unknown2", F.U32), ("startOffset", F.F32), ("endOffset", F.F32), ("clipId", F.U32),
    });

    static readonly Layout MixerParam = new("mixerParam", new[]
    {
        ("unknown1", F.U32), ("paramId", F.U8), ("unknown2", F.U8), ("channelData", F.U16), ("value", F.U32),
    });

    static readonly Layout[] Layouts = { Note, PlaylistClip, MixerParam };

    // ---------------------------------------------------------------- decompile

    public static string Decompile(FlpProject p)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            w.WriteStartObject();
            w.WriteString("tool", ToolName);
            w.WriteNumber("schema", SchemaVersion);
            if (p.VersionString != null) w.WriteString("flVersion", p.VersionString);
            w.WriteStartObject("header");
            w.WriteNumber("format", p.Format);
            w.WriteNumber("channels", p.Channels);
            w.WriteNumber("ppq", p.Ppq);
            if (p.HeaderExtra.Length > 0) w.WriteString("extra", Convert.ToHexString(p.HeaderExtra));
            w.WriteEndObject();
            w.WriteString("framing", p.Framing);
            if (p.DeclaredDataLength is uint dl) w.WriteNumber("declaredDataLength", dl);
            if (p.Trailing.Length > 0) w.WriteString("trailing", Convert.ToHexString(p.Trailing));
            w.WriteStartArray("events");
            int major = p.VersionMajor;
            foreach (var e in p.Events) WriteEvent(w, e, major);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static void WriteEvent(Utf8JsonWriter w, FlpEvent e, int major)
    {
        w.WriteStartObject();
        w.WriteNumber("id", e.Id);
        w.WriteString("name", FlpEventNames.Get(e.Id));
        if (e.VarintBytes != 0) w.WriteNumber("varintBytes", e.VarintBytes);
        int fixedSize = FlpEvent.FixedSize(e.Id);
        if (fixedSize > 0 && e.Data.Length == fixedSize)
            w.WriteNumber("value", e.Value);
        else if (fixedSize < 0 && TryText(e, out var text, out var ascii))
            w.WriteString(ascii ? "ascii" : "text", text);
        else if (fixedSize < 0 && TryRecords(e, major, out var layout, out int size))
            WriteRecords(w, e.Data, layout, size);
        else
            WriteBlob(w, e.Data);
        w.WriteEndObject();
    }

    static void WriteBlob(Utf8JsonWriter w, byte[] data)
    {
        if (data.Length <= HexLimit) w.WriteString("hex", Convert.ToHexString(data));
        else w.WriteString("base64", Convert.ToBase64String(data));
    }

    static bool TryText(FlpEvent e, out string text, out bool ascii)
    {
        text = "";
        ascii = false;
        var d = e.Data;
        if (e.Id == FlpEventNames.Version && TryAscii(d, out text)) { ascii = true; return true; }
        if (d.Length >= 2 && d.Length % 2 == 0 && d[^1] == 0 && d[^2] == 0)
        {
            var s = Encoding.Unicode.GetString(d, 0, d.Length - 2);
            if (s.All(IsTextChar) && !HasLoneSurrogate(s) && Encoding.Unicode.GetBytes(s + "\0").AsSpan().SequenceEqual(d))
            {
                text = s;
                return true;
            }
        }
        return false;
    }

    static bool TryAscii(byte[] d, out string text)
    {
        text = "";
        if (d.Length < 1 || d[^1] != 0) return false;
        for (int i = 0; i < d.Length - 1; i++)
            if (d[i] < 0x20 || d[i] > 0x7e) return false;
        text = Encoding.ASCII.GetString(d, 0, d.Length - 1);
        return true;
    }

    static bool IsTextChar(char c) => c >= 0x20 || c == '\r' || c == '\n' || c == '\t';

    static bool HasLoneSurrogate(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return true;
                i++;
            }
            else if (char.IsLowSurrogate(s[i])) return true;
        }
        return false;
    }

    static bool TryRecords(FlpEvent e, int major, out Layout layout, out int size)
    {
        int n = e.Data.Length;
        layout = Note;
        size = 0;
        if (n == 0) return false;
        switch (e.Id)
        {
            case FlpEventNames.Notes when n % Note.Size == 0:
                layout = Note; size = Note.Size; return true;
            case FlpEventNames.MixerParams when n % MixerParam.Size == 0:
                layout = MixerParam; size = MixerParam.Size; return true;
            case FlpEventNames.PlaylistItems:
                layout = PlaylistClip;
                // FL 26 clips are 88 bytes, FL 21-25 clips 80; every clip has pattern base 0x5000.
                var sizes = major >= 26 ? new[] { 88, 80, 60 } : new[] { 80, 88, 60 };
                foreach (var s in sizes)
                {
                    if (n % s != 0) continue;
                    bool ok = true;
                    for (int j = 0; j < n && ok; j += s)
                        ok = BinaryPrimitives.ReadUInt16LittleEndian(e.Data.AsSpan(j + 4)) == 0x5000;
                    if (ok) { size = s; return true; }
                }
                return false;
        }
        return false;
    }

    static void WriteRecords(Utf8JsonWriter w, byte[] d, Layout layout, int size)
    {
        w.WriteString("recordType", layout.Type);
        w.WriteNumber("recordSize", size);
        w.WriteStartArray("records");
        for (int off = 0; off < d.Length; off += size)
        {
            var r = d.AsSpan(off, size);
            w.WriteStartObject();
            int k = 0;
            foreach (var (name, type) in layout.Fields)
            {
                switch (type)
                {
                    case F.U8: w.WriteNumber(name, r[k]); k += 1; break;
                    case F.U16: w.WriteNumber(name, BinaryPrimitives.ReadUInt16LittleEndian(r[k..])); k += 2; break;
                    case F.U32: w.WriteNumber(name, BinaryPrimitives.ReadUInt32LittleEndian(r[k..])); k += 4; break;
                    case F.F32:
                        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(r[k..]);
                        float f = BitConverter.UInt32BitsToSingle(bits);
                        if (float.IsFinite(f) && !(f == 0 && bits != 0) && FloatRoundTrips(f, bits)) w.WriteNumber(name, f);
                        else w.WriteString(name, $"0x{bits:X8}");
                        k += 4;
                        break;
                }
            }
            if (k < size) w.WriteString("extra", Convert.ToHexString(r[k..]));
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    static bool FloatRoundTrips(float f, uint bits) =>
        BitConverter.SingleToUInt32Bits(float.Parse(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture)) == bits;

    // ---------------------------------------------------------------- recompile

    public static FlpProject Recompile(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var p = new FlpProject();
        if (root.TryGetProperty("header", out var h))
        {
            if (h.TryGetProperty("format", out var v)) p.Format = v.GetInt16();
            if (h.TryGetProperty("channels", out v)) p.Channels = v.GetUInt16();
            if (h.TryGetProperty("ppq", out v)) p.Ppq = v.GetUInt16();
            if (h.TryGetProperty("extra", out v)) p.HeaderExtra = Convert.FromHexString(v.GetString()!);
        }
        if (root.TryGetProperty("framing", out var fr)) p.Framing = fr.GetString() ?? "standard";
        if (root.TryGetProperty("declaredDataLength", out var ddl)) p.DeclaredDataLength = ddl.GetUInt32();
        if (root.TryGetProperty("trailing", out var tr)) p.Trailing = Convert.FromHexString(tr.GetString()!);
        if (!root.TryGetProperty("events", out var events)) throw new InvalidDataException("JSON has no \"events\" array.");
        int index = 0;
        foreach (var ev in events.EnumerateArray())
        {
            try { p.Events.Add(ReadEvent(ev)); }
            catch (Exception ex) when (ex is not InvalidDataException)
            {
                throw new InvalidDataException($"Event #{index}: {ex.Message}", ex);
            }
            index++;
        }
        return p;
    }

    static FlpEvent ReadEvent(JsonElement ev)
    {
        int idv = ev.GetProperty("id").GetInt32();
        if (idv is < 0 or > 255) throw new InvalidDataException($"Event id {idv} is out of range 0-255.");
        byte id = (byte)idv;
        int vb = ev.TryGetProperty("varintBytes", out var vbe) ? vbe.GetInt32() : 0;
        byte[] data;
        if (ev.TryGetProperty("value", out var val))
        {
            if (FlpEvent.FixedSize(id) < 0) throw new InvalidDataException($"Event {id} is variable-length; use text/hex/base64.");
            return FlpEvent.FromValue(id, val.GetUInt32());
        }
        if (ev.TryGetProperty("text", out var t)) data = Encoding.Unicode.GetBytes(t.GetString() + "\0");
        else if (ev.TryGetProperty("ascii", out var a)) data = Encoding.Latin1.GetBytes(a.GetString() + "\0");
        else if (ev.TryGetProperty("hex", out var hx)) data = Convert.FromHexString(hx.GetString()!);
        else if (ev.TryGetProperty("base64", out var b64)) data = Convert.FromBase64String(b64.GetString()!);
        else if (ev.TryGetProperty("records", out var recs)) data = ReadRecords(ev, recs);
        else throw new InvalidDataException($"Event {id} has no payload (value/text/ascii/hex/base64/records).");
        if (id < 192 && data.Length != FlpEvent.FixedSize(id) && !(id == FlpEventNames.VersionMarker && data.Length == 3))
            throw new InvalidDataException($"Event {id} must carry {FlpEvent.FixedSize(id)} byte(s), got {data.Length}.");
        return new FlpEvent(id, data, vb);
    }

    static byte[] ReadRecords(JsonElement ev, JsonElement recs)
    {
        string type = ev.GetProperty("recordType").GetString()!;
        var layout = Layouts.FirstOrDefault(l => l.Type == type) ?? throw new InvalidDataException($"Unknown recordType \"{type}\".");
        int size = ev.TryGetProperty("recordSize", out var rs) ? rs.GetInt32() : layout.Size;
        if (size < layout.Size) throw new InvalidDataException($"recordSize {size} is smaller than {layout.Type} ({layout.Size}).");
        var ms = new MemoryStream();
        Span<byte> buf = stackalloc byte[4];
        foreach (var r in recs.EnumerateArray())
        {
            long start = ms.Length;
            foreach (var (name, type2) in layout.Fields)
            {
                var v = r.TryGetProperty(name, out var x) ? x : default;
                bool has = v.ValueKind != JsonValueKind.Undefined;
                switch (type2)
                {
                    case F.U8: ms.WriteByte(has ? v.GetByte() : (byte)0); break;
                    case F.U16: BinaryPrimitives.WriteUInt16LittleEndian(buf, has ? v.GetUInt16() : (ushort)0); ms.Write(buf[..2]); break;
                    case F.U32: BinaryPrimitives.WriteUInt32LittleEndian(buf, has ? v.GetUInt32() : 0); ms.Write(buf); break;
                    case F.F32:
                        uint bits = !has ? 0
                            : v.ValueKind == JsonValueKind.String ? Convert.ToUInt32(v.GetString()!, 16)
                            : BitConverter.SingleToUInt32Bits(v.GetSingle());
                        BinaryPrimitives.WriteUInt32LittleEndian(buf, bits);
                        ms.Write(buf);
                        break;
                }
            }
            if (r.TryGetProperty("extra", out var extra)) ms.Write(Convert.FromHexString(extra.GetString()!));
            long written = ms.Length - start;
            if (written > size) throw new InvalidDataException($"{layout.Type} record is {written} bytes, more than recordSize {size}.");
            for (; written < size; written++) ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Decompile then recompile in memory and confirm the bytes come back identical.</summary>
    public static (bool Ok, string Message) VerifyRoundTrip(byte[] original)
    {
        var p = FlpProject.Parse(original);
        var again = Recompile(Decompile(p)).ToBytes();
        if (again.AsSpan().SequenceEqual(original)) return (true, $"identical ({original.Length:N0} bytes, {p.Events.Count:N0} events)");
        int i = 0;
        while (i < Math.Min(again.Length, original.Length) && again[i] == original[i]) i++;
        return (false, $"differs at byte 0x{i:X} (original {original.Length:N0} bytes, rebuilt {again.Length:N0})");
    }
}
