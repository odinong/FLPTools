using System.Buffers.Binary;
using System.Text;

namespace FLPTools.Flp;

/// <summary>
/// Rewrites an FL Studio 2026 project so FL Studio 2025 can open it. Each step was found by
/// loading test files in FL 25.2.5 until it stopped reporting invalid data or crashing:
///   1. version/build events set to an FL 25 build;
///   2. "172" version-marker events removed (in the body the marker is followed by a stray
///      ChanEnabled event that FL 25 rejects as invalid data);
///   3. playlist clips shrunk from 88 to 80 bytes (88-byte clips crash FL 25's engine);
///   4. mixer padded back to FL 25's fixed 126 inserts (FL 26 only saves used inserts);
///   5. format flags (event 169) set to FL 25's value.
/// </summary>
public static class Fl25Converter
{
    public const string DefaultVersion = "25.2.5.5319";
    const int Fl25Inserts = 126;          // inserts 0..124 plus the "current" insert
    const int FirstInsertCode = 0x7000;   // mixer param channelData = 0x7000 + insert*64 + slot
    const int CurrentInsertCode = 0xED40;

    // An empty FL 25 insert: InsertBegin, InsertFlags, 10 slots, routing, IO flags, input/output.
    static readonly byte[] EmptyInsertEvents = Convert.FromHexString(
        "2a00ec0c000000004c00000000000000620000620100620200620300620400620500620600620700620800620900" +
        "eb0101a503000000a60100000031009affffffff93ffffffff");

    // The 32 default mixer-param records FL 25 writes for an empty insert (insert bits zeroed).
    static readonly byte[] EmptyInsertParams = Convert.FromHexString(
        "00000000001f00000100000000000000011f00000032000000000000001f01000100000000000000011f01000032" +
        "000000000000001f02000100000000000000011f02000032000000000000001f03000100000000000000011f0300" +
        "0032000000000000001f04000100000000000000011f04000032000000000000001f05000100000000000000011f" +
        "05000032000000000000001f06000100000000000000011f06000032000000000000001f07000100000000000000" +
        "011f07000032000000000000001f08000100000000000000011f08000032000000000000001f0900010000000000" +
        "0000011f09000032000000000000c01f00000032000000000000c11f00000000000000000000c21f000000000000" +
        "00000000d01f00000000000000000000d11f00000000000000000000d21f00000000000000000000d81f00009116" +
        "000000000000d91f00007981000000000000da1f000011da000000000000e01f00005c44000000000000e11f0000" +
        "5c44000000000000e21f00005c440000");

    public static List<string> Convert25(FlpProject p, string targetVersion = DefaultVersion)
    {
        if (p.IsLocked) throw new InvalidOperationException(FlpProject.LockedMessage);
        var log = new List<string>();
        bool fromFl26 = p.VersionMajor >= 26;
        if (!fromFl26) log.Add($"Note: project says FL {p.VersionString ?? "?"}, not FL 26; converting anyway.");

        SetVersion(p, targetVersion, log);
        RemoveVersionMarkers(p, log);
        ShrinkPlaylistClips(p, fromFl26, log);
        PadMixer(p, log);

        var flags = p.Events.FirstOrDefault(e => e.Id == FlpEventNames.FormatFlags && e.Data.Length == 4);
        if (flags != null && flags.Value != 7)
        {
            log.Add($"Format flags (event 169): {flags.Value} -> 7");
            flags.Data = BitConverter.GetBytes(7u);
        }
        p.Framing = "standard";
        return log;
    }

    static void SetVersion(FlpProject p, string version, List<string> log)
    {
        var parts = version.Split('.');
        if (parts.Length != 4 || !parts.All(x => uint.TryParse(x, out _)))
            throw new ArgumentException($"Version must look like 25.2.5.5319, got \"{version}\".");
        uint build = uint.Parse(parts[3]);
        var ver = p.Events.FirstOrDefault(e => e.Id == FlpEventNames.Version);
        if (ver == null) p.Events.Insert(0, ver = new FlpEvent(FlpEventNames.Version, Array.Empty<byte>()));
        log.Add($"Version: {p.VersionString} -> {version}");
        ver.Data = Encoding.ASCII.GetBytes(version + "\0");
        var b = p.Events.FirstOrDefault(e => e.Id == FlpEventNames.Build);
        if (b != null) b.Data = BitConverter.GetBytes(build);
        else p.Events.Insert(p.Events.IndexOf(ver) + 1, FlpEvent.FromValue(FlpEventNames.Build, build));
    }

    static void RemoveVersionMarkers(FlpProject p, List<string> log)
    {
        for (int i = p.Events.Count - 1; i >= 0; i--)
        {
            if (p.Events[i].Id != FlpEventNames.VersionMarker) continue;
            int count = 1;
            if (i + 1 < p.Events.Count && p.Events[i + 1].Id is FlpEventNames.Name or FlpEventNames.ChanEnabled) count = 2;
            log.Add($"Removed version marker (event 172) at #{i}" + (count == 2 ? $" and its {FlpEventNames.Get(p.Events[i + 1].Id)} event" : ""));
            p.Events.RemoveRange(i, count);
        }
    }

    static void ShrinkPlaylistClips(FlpProject p, bool fromFl26, List<string> log)
    {
        foreach (var e in p.Events.Where(e => e.Id == FlpEventNames.PlaylistItems && e.Data.Length > 0))
        {
            int n = e.Data.Length;
            if (n % 88 != 0 || !AllClips(e.Data, 88) || (!fromFl26 && n % 80 == 0 && AllClips(e.Data, 80))) continue;
            var outp = new byte[n / 88 * 80];
            for (int j = 0, k = 0; j < n; j += 88, k += 80)
                Array.Copy(e.Data, j, outp, k, 72);   // bytes 72..79 stay zero, as FL 25 writes them
            e.Data = outp;
            log.Add($"Playlist: {n / 88} clips converted from 88 to 80 bytes");
        }
    }

    static bool AllClips(byte[] d, int size)
    {
        for (int j = 0; j < d.Length; j += size)
            if (BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(j + 4)) != 0x5000) return false;
        return true;
    }

    static void PadMixer(FlpProject p, List<string> log)
    {
        var ev = p.Events;
        int inserts = ev.Count(e => e.Id == FlpEventNames.InsertFlags);
        if (inserts == 0 || inserts >= Fl25Inserts) return;
        int pad = Fl25Inserts - inserts;
        int paramsIdx = ev.FindIndex(e => e.Id == FlpEventNames.MixerParams);
        int lastBegin = ev.FindLastIndex(paramsIdx < 0 ? ev.Count - 1 : paramsIdx, e => e.Id == FlpEventNames.InsertPrefix);
        if (lastBegin > 0 && ev[lastBegin - 1].Id == 149) lastBegin--;   // an insert's colour comes just before its 42
        if (lastBegin < 0)
        {
            log.Add("Mixer: could not find insert boundaries, left as is (FL 25 may reject it).");
            return;
        }

        if (paramsIdx >= 0)
        {
            var mp = ev[paramsIdx];
            var recs = Enumerable.Range(0, mp.Data.Length / 12).Select(k => mp.Data.AsSpan(k * 12, 12).ToArray()).ToList();
            int InsertOf(byte[] r)
            {
                int cd = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(6));
                return cd >= FirstInsertCode && cd < CurrentInsertCode ? (cd - FirstInsertCode) >> 6 : -1;
            }
            int last = recs.Select(InsertOf).DefaultIfEmpty(-1).Max();
            if (last != inserts - 2)
            {
                log.Add($"Mixer: parameter table ends at insert {last}, expected {inserts - 2}; left as is.");
                return;
            }
            int cut = recs.FindLastIndex(r => InsertOf(r) == last) + 1;
            var added = new List<byte[]>();
            for (int ins = last + 1; ins < Fl25Inserts - 1; ins++)
            {
                for (int k = 0; k < EmptyInsertParams.Length; k += 12)
                {
                    var r = EmptyInsertParams.AsSpan(k, 12).ToArray();
                    int slot = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(6));
                    BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(6), (ushort)(FirstInsertCode + (ins << 6) + slot));
                    added.Add(r);
                }
            }
            recs.InsertRange(cut, added);
            mp.Data = recs.SelectMany(r => r).ToArray();
        }

        var template = FlpProject.Parse(BuildStub(EmptyInsertEvents)).Events;
        var padding = Enumerable.Range(0, pad).SelectMany(_ => template.Select(t => new FlpEvent(t.Id, (byte[])t.Data.Clone())));
        ev.InsertRange(lastBegin, padding);

        var count = ev.FirstOrDefault(e => e.Id == FlpEventNames.MixerInsertCount && e.Data.Length == 2);
        if (count != null) count.Data = BitConverter.GetBytes((ushort)Fl25Inserts);
        log.Add($"Mixer: padded {inserts} -> {Fl25Inserts} inserts (FL 26 saves only used inserts)");
    }

    static byte[] BuildStub(byte[] events)
    {
        var p = new FlpProject();
        var bytes = p.ToBytes().Concat(events).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18), (uint)events.Length);
        return bytes;
    }
}
