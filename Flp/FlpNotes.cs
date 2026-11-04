using System.Buffers.Binary;
using System.Text;

namespace FLPTools.Flp;

public struct FlpNote
{
    public int Position, Length, Channel, Key, Velocity, Pan;
}

/// <summary>Pattern note editing (24-byte note records) and MIDI export.</summary>
public static class FlpNotes
{
    public const int Size = 24;

    public static int ClipSize(byte[] data, int major)
    {
        foreach (var s in major >= 26 ? new[] { 88, 80, 60 } : new[] { 80, 88, 60 })
        {
            if (data.Length % s != 0) continue;
            bool ok = true;
            for (int j = 0; j < data.Length && ok; j += s)
                ok = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(j + 4)) == 0x5000;
            if (ok) return s;
        }
        return 0;
    }

    public static List<FlpNote> Read(FlpPattern p)
    {
        var list = new List<FlpNote>();
        if (p.NotesEvent == null) return list;
        var d = p.NotesEvent.Data;
        for (int j = 0; j + Size <= d.Length; j += Size)
        {
            var r = d.AsSpan(j);
            list.Add(new FlpNote
            {
                Position = (int)BinaryPrimitives.ReadUInt32LittleEndian(r),
                Channel = BinaryPrimitives.ReadUInt16LittleEndian(r[6..]),
                Length = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[8..]),
                Key = BinaryPrimitives.ReadUInt16LittleEndian(r[12..]),
                Pan = r[20],
                Velocity = r[21],
            });
        }
        return list;
    }

    public sealed class Edit
    {
        public int Transpose;            // semitones
        public double VelocityScale = 1; // multiply
        public int VelocityAdd;          // then add
        public int QuantizeTicks;        // 0 = off
        public double QuantizeStrength = 1;
        public double LengthScale = 1;
        public int? OnlyChannel;         // null = every channel
    }

    /// <summary>Apply an edit to the notes of the given patterns. Returns the number of notes changed.</summary>
    public static int Apply(IEnumerable<FlpPattern> patterns, Edit e)
    {
        int changed = 0;
        foreach (var p in patterns)
        {
            if (p.NotesEvent == null) continue;
            var d = (byte[])p.NotesEvent.Data.Clone();
            for (int j = 0; j + Size <= d.Length; j += Size)
            {
                var r = d.AsSpan(j);
                int ch = BinaryPrimitives.ReadUInt16LittleEndian(r[6..]);
                if (e.OnlyChannel is int only && only != ch) continue;
                long pos = BinaryPrimitives.ReadUInt32LittleEndian(r);
                long len = BinaryPrimitives.ReadUInt32LittleEndian(r[8..]);
                int key = BinaryPrimitives.ReadUInt16LittleEndian(r[12..]);
                int vel = r[21];

                key = Math.Clamp(key + e.Transpose, 0, 131);
                vel = (int)Math.Clamp(Math.Round(vel * e.VelocityScale + e.VelocityAdd), 1, 128);
                if (e.QuantizeTicks > 0)
                {
                    long target = (long)Math.Round((double)pos / e.QuantizeTicks) * e.QuantizeTicks;
                    pos = Math.Max(0, (long)Math.Round(pos + (target - pos) * e.QuantizeStrength));
                }
                len = Math.Max(1, (long)Math.Round(len * e.LengthScale));

                BinaryPrimitives.WriteUInt32LittleEndian(r, (uint)pos);
                BinaryPrimitives.WriteUInt32LittleEndian(r[8..], (uint)Math.Min(len, uint.MaxValue));
                BinaryPrimitives.WriteUInt16LittleEndian(r[12..], (ushort)key);
                r[21] = (byte)vel;
                changed++;
            }
            p.NotesEvent.Data = d;
        }
        return changed;
    }

    // ------------------------------------------------------------ MIDI

    /// <summary>Write one pattern as a type-1 MIDI file, one track per channel.</summary>
    public static void ExportPattern(FlpModel m, FlpPattern p, string path) =>
        WriteMidi(m, Read(p).Select(n => (n, 0)).ToList(), path, FlpModel.Text(p.NameEvent));

    /// <summary>Write the playlist's pattern clips as one MIDI file (audio/automation clips are skipped).</summary>
    public static int ExportSong(FlpModel m, string path)
    {
        var all = new List<(FlpNote, int)>();
        var byIndex = m.Patterns.ToDictionary(p => p.Index);
        foreach (var clip in m.Clips().Where(c => c.IsPattern))
        {
            if (!byIndex.TryGetValue(clip.PatternIndex, out var p)) continue;
            int start = clip.StartOffset >= 0 ? (int)clip.StartOffset : 0;
            int end = start + clip.Length;
            foreach (var n in Read(p))
            {
                if (n.Position < start || n.Position >= end) continue;
                var cut = n;
                cut.Length = Math.Min(n.Length, end - n.Position);
                all.Add((cut, clip.Position - start));
            }
        }
        WriteMidi(m, all, path, m.Title);
        return all.Count;
    }

    static void WriteMidi(FlpModel m, List<(FlpNote Note, int Offset)> notes, string path, string title)
    {
        var tracks = new List<byte[]>();
        var meta = new List<(long, byte[])>();
        int uspq = (int)Math.Round(60_000_000 / m.Tempo);
        meta.Add((0, new byte[] { 0xFF, 0x51, 3, (byte)(uspq >> 16), (byte)(uspq >> 8), (byte)uspq }));
        int den = (int)Math.Round(Math.Log2(Math.Max(1, m.TimeSigBeat)));
        meta.Add((0, new byte[] { 0xFF, 0x58, 4, (byte)m.TimeSigNum, (byte)den, 24, 8 }));
        if (title.Length > 0) meta.Insert(0, (0, MetaText(3, title)));
        tracks.Add(Track(meta));

        int midiChannel = 0;
        foreach (var g in notes.GroupBy(n => n.Note.Channel).OrderBy(g => g.Key))
        {
            var chan = m.Channels.FirstOrDefault(c => c.Index == g.Key);
            string name = chan != null ? FlpModel.Text(chan.NameEvent) : $"Channel {g.Key}";
            int mc = midiChannel == 9 ? ++midiChannel : midiChannel;   // keep off the GM drum channel
            midiChannel = (midiChannel + 1) % 16;
            var evs = new List<(long, byte[])> { (0, MetaText(3, name.Length > 0 ? name : $"Channel {g.Key}")) };
            foreach (var (n, off) in g)
            {
                long at = (long)n.Position + off;
                int key = Math.Clamp(n.Key, 0, 127);
                int vel = Math.Clamp(n.Velocity * 127 / 128, 1, 127);
                evs.Add((at, new byte[] { (byte)(0x90 | mc), (byte)key, (byte)vel }));
                evs.Add((at + Math.Max(1, n.Length), new byte[] { (byte)(0x80 | mc), (byte)key, 0 }));
            }
            tracks.Add(Track(evs));
        }

        using var f = File.Create(path);
        var w = new BinaryWriter(f);
        w.Write(Encoding.ASCII.GetBytes("MThd"));
        WriteBE(w, 6, 4);
        WriteBE(w, 1, 2);
        WriteBE(w, tracks.Count, 2);
        WriteBE(w, m.Ppq, 2);
        foreach (var t in tracks)
        {
            w.Write(Encoding.ASCII.GetBytes("MTrk"));
            WriteBE(w, t.Length, 4);
            w.Write(t);
        }
    }

    static byte[] MetaText(byte type, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        var v = Vlq(b.Length);
        return new byte[] { 0xFF, type }.Concat(v).Concat(b).ToArray();
    }

    static byte[] Track(List<(long At, byte[] Data)> evs)
    {
        // note-offs sort before note-ons at the same tick so repeated notes retrigger
        var sorted = evs.Select((e, i) => (e.At, e.Data, i))
            .OrderBy(e => e.At).ThenBy(e => (e.Data[0] & 0xF0) == 0x90 ? 1 : 0).ThenBy(e => e.i);
        var ms = new MemoryStream();
        long last = 0;
        foreach (var (at, data, _) in sorted)
        {
            ms.Write(Vlq((int)(at - last)));
            ms.Write(data);
            last = at;
        }
        ms.Write(new byte[] { 0, 0xFF, 0x2F, 0 });
        return ms.ToArray();
    }

    static byte[] Vlq(int v)
    {
        var bytes = new Stack<byte>();
        bytes.Push((byte)(v & 0x7F));
        while ((v >>= 7) > 0) bytes.Push((byte)((v & 0x7F) | 0x80));
        return bytes.ToArray();
    }

    static void WriteBE(BinaryWriter w, int v, int n)
    {
        for (int i = n - 1; i >= 0; i--) w.Write((byte)(v >> (8 * i)));
    }
}
