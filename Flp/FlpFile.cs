using System.Text;

namespace FLPTools.Flp;

/// <summary>One FLP event: an id byte plus its payload, exactly as stored.</summary>
public sealed class FlpEvent
{
    public byte Id;
    public byte[] Data;
    /// <summary>Bytes used by the length varint when it was not minimally encoded (0 = minimal).</summary>
    public int VarintBytes;

    public FlpEvent(byte id, byte[] data, int varintBytes = 0)
    {
        Id = id;
        Data = data;
        VarintBytes = varintBytes;
    }

    /// <summary>Payload size implied by the id range, or -1 for variable-length events.</summary>
    public static int FixedSize(byte id) => id < 64 ? 1 : id < 128 ? 2 : id < 192 ? 4 : -1;

    public uint Value => Data.Length switch
    {
        1 => Data[0],
        2 => BitConverter.ToUInt16(Data, 0),
        4 => BitConverter.ToUInt32(Data, 0),
        _ => throw new InvalidOperationException($"Event {Id} has no scalar value"),
    };

    public static FlpEvent FromValue(byte id, uint value) => new(id, FixedSize(id) switch
    {
        1 => new[] { (byte)value },
        2 => BitConverter.GetBytes((ushort)value),
        4 => BitConverter.GetBytes(value),
        _ => throw new ArgumentException($"Event {id} is variable-length"),
    });

    public override string ToString() => $"{Id} ({FlpEventNames.Get(Id)}) [{Data.Length}]";
}

/// <summary>An FL Studio project: the FLhd header plus the FLdt event stream.</summary>
public sealed class FlpProject
{
    public short Format;
    public ushort Channels;
    public ushort Ppq = 96;
    /// <summary>Header bytes beyond the standard 6 (normally empty).</summary>
    public byte[] HeaderExtra = Array.Empty<byte>();
    /// <summary>Event framing used to read the stream: "short172" (event 172 carries 3 bytes, FL 25.2.3+) or "standard".</summary>
    public string Framing = "standard";
    /// <summary>Set only when the FLdt length in the file did not match the actual data.</summary>
    public uint? DeclaredDataLength;
    /// <summary>Bytes after the FLdt chunk (normally empty).</summary>
    public byte[] Trailing = Array.Empty<byte>();
    public List<FlpEvent> Events = new();

    public string? VersionString
    {
        get
        {
            var e = Events.FirstOrDefault(x => x.Id == FlpEventNames.Version);
            return e == null ? null : Encoding.ASCII.GetString(e.Data).TrimEnd('\0');
        }
    }

    public int VersionMajor => int.TryParse(VersionString?.Split('.')[0], out var v) ? v : 0;

    /// <summary>
    /// FL marks projects saved by an unregistered copy (event 28 = 0) and scrambles their values so only a
    /// registered FL can open them. FLPTools does not unlock these; it only reads and copies them as they are.
    /// </summary>
    public bool IsLocked => Events.FirstOrDefault(e => e.Id == 28 && e.Data.Length == 1) is { } r && r.Data[0] == 0;

    public const string LockedMessage =
        "Can't open this project. It was saved with an unregistered FL Studio, so it's locked until a registered copy saves it again.";

    public static FlpProject Load(string path) => Parse(File.ReadAllBytes(path));

    public void Save(string path) => File.WriteAllBytes(path, ToBytes());

    public static FlpProject Parse(byte[] d)
    {
        if (d.Length < 22 || Encoding.ASCII.GetString(d, 0, 4) != "FLhd")
            throw new InvalidDataException("Not an FLP file (missing FLhd header).");
        var p = new FlpProject();
        int hlen = (int)BitConverter.ToUInt32(d, 4);
        if (hlen < 6 || 8 + hlen + 8 > d.Length) throw new InvalidDataException($"Bad FLhd length {hlen}.");
        p.Format = BitConverter.ToInt16(d, 8);
        p.Channels = BitConverter.ToUInt16(d, 10);
        p.Ppq = BitConverter.ToUInt16(d, 12);
        p.HeaderExtra = d[14..(8 + hlen)];
        int pos = 8 + hlen;
        if (Encoding.ASCII.GetString(d, pos, 4) != "FLdt") throw new InvalidDataException("Missing FLdt chunk.");
        uint dlen = BitConverter.ToUInt32(d, pos + 4);
        pos += 8;
        int end = (int)Math.Min((long)pos + dlen, d.Length);
        if ((long)pos + dlen != end) p.DeclaredDataLength = dlen;
        p.Trailing = d[end..];

        // FL 25.2.3+ writes event 172 with a 3-byte payload, which breaks the id-range rule.
        // Try that framing first and fall back to the plain rule if it does not land exactly on the end.
        Exception? last = null;
        foreach (var framing in new[] { "short172", "standard" })
        {
            try
            {
                p.Events = ReadEvents(d, pos, end, framing == "short172");
                p.Framing = framing;
                return p;
            }
            catch (InvalidDataException ex) { last = ex; }
        }
        throw last!;
    }

    static List<FlpEvent> ReadEvents(byte[] d, int i, int end, bool short172)
    {
        var list = new List<FlpEvent>();
        while (i < end)
        {
            byte id = d[i++];
            int n, vb = 0;
            if (short172 && id == 172) n = 3;
            else if (id < 192) n = FlpEvent.FixedSize(id);
            else
            {
                n = 0;
                int shift = 0, used = 0;
                while (true)
                {
                    if (i >= end) throw new InvalidDataException("Truncated length varint.");
                    byte b = d[i++];
                    used++;
                    n |= (b & 0x7f) << shift;
                    shift += 7;
                    if ((b & 0x80) == 0) break;
                    if (shift > 28) throw new InvalidDataException("Length varint too long.");
                }
                if (used != VarintSize(n)) vb = used;
            }
            if (n < 0 || i + n > end) throw new InvalidDataException($"Event {id} at 0x{i:X} overruns the data chunk.");
            list.Add(new FlpEvent(id, d[i..(i + n)], vb));
            i += n;
        }
        return list;
    }

    public static int VarintSize(int n)
    {
        int s = 1;
        while (n >= 0x80) { n >>= 7; s++; }
        return s;
    }

    public static void WriteEvent(Stream s, FlpEvent e)
    {
        s.WriteByte(e.Id);
        if (e.Id >= 192)
        {
            int n = e.Data.Length;
            int count = Math.Max(e.VarintBytes, VarintSize(n));
            for (int k = 0; k < count; k++)
            {
                byte b = (byte)(n & 0x7f);
                n >>= 7;
                s.WriteByte(k < count - 1 ? (byte)(b | 0x80) : b);
            }
        }
        s.Write(e.Data);
    }

    public byte[] ToBytes()
    {
        var body = new MemoryStream();
        foreach (var e in Events) WriteEvent(body, e);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("FLhd"));
        w.Write((uint)(6 + HeaderExtra.Length));
        w.Write(Format);
        w.Write(Channels);
        w.Write(Ppq);
        w.Write(HeaderExtra);
        w.Write(Encoding.ASCII.GetBytes("FLdt"));
        w.Write(DeclaredDataLength ?? (uint)body.Length);
        w.Write(body.ToArray());
        w.Write(Trailing);
        return ms.ToArray();
    }
}
