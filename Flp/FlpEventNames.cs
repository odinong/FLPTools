namespace FLPTools.Flp;

/// <summary>
/// Readable names for FLP event ids. Names are informational only: the numeric id is
/// what gets recompiled. Ids without a known meaning are shown as Byte_N/Word_N/DWord_N/Data_N.
/// </summary>
public static class FlpEventNames
{
    public const byte ChanEnabled = 0;
    public const byte InsertPrefix = 42;
    public const byte SlotIndex = 98;
    public const byte MixerInsertCount = 103;
    public const byte FormatFlags = 169;
    public const byte VersionMarker = 172;
    public const byte Build = 159;
    public const byte Name = 192;
    public const byte Version = 199;
    public const byte Notes = 224;
    public const byte MixerParams = 225;
    public const byte PlaylistItems = 233;
    public const byte InsertFlags = 236;

    static readonly Dictionary<byte, string> Names = new()
    {
        [0] = "ChanEnabled", [2] = "ChanVolOld", [3] = "ChanPanOld", [4] = "MidiChannel", [5] = "MidiNote",
        [6] = "MidiPatch", [7] = "MidiBank", [9] = "LoopActive", [10] = "ShowInfo", [11] = "Shuffle",
        [12] = "MainVolOld", [13] = "FitToSteps", [14] = "Pitchable", [15] = "Zipped", [16] = "DelayFlags",
        [17] = "TimeSigNum", [18] = "TimeSigBeat", [19] = "UseLoopPoints", [20] = "LoopType", [21] = "ChanType",
        [22] = "TargetInsert", [23] = "PanLaw", [28] = "Registered", [32] = "ChanLocked",
        [42] = "InsertBegin",
        [64] = "NewChannel", [65] = "NewPattern", [66] = "TempoOld", [67] = "CurrentPattern",
        [69] = "ChanFx", [70] = "ChanFxFlags", [71] = "ChanCutoff", [74] = "ChanPreamp", [75] = "ChanDecay",
        [76] = "ChanAttack", [80] = "MainPitch", [83] = "ChanResonance", [85] = "ChanStereoDelay", [86] = "ChanFx3",
        [89] = "ChanShiftTime", [93] = "TempoFine", [95] = "InsertIcon", [97] = "ChanSwingMix",
        [98] = "SlotIndex", [99] = "NewArrangement", [100] = "CurrentArrangement", [103] = "MixerInsertCount",
        [128] = "PluginColor", [131] = "ChanFxSine", [132] = "ChanCutGroup", [133] = "WindowHeight",
        [135] = "ChanMiddleNote", [138] = "ChanDelayModXY", [139] = "ChanReverb", [143] = "ChanSampleFlags",
        [144] = "ChanLayerFlags", [145] = "ChanFilterGroup", [146] = "CurrentFilterGroup", [147] = "InsertOutput",
        [149] = "InsertColor", [150] = "PatternColor", [154] = "InsertInput", [155] = "PluginIcon",
        [156] = "Tempo", [159] = "Build", [169] = "FormatFlags", [172] = "VersionMarker",
        [192] = "Name", [193] = "PatternName", [194] = "Title", [195] = "Comments", [196] = "SampleFileName",
        [197] = "Url", [198] = "CommentsRtf", [199] = "Version", [200] = "Licensee", [201] = "DefaultPluginName",
        [202] = "DataPath", [203] = "PluginName", [204] = "InsertName", [205] = "TimeMarkerName", [206] = "Genre",
        [207] = "Artists", [209] = "ChanDelay", [212] = "PluginInfo", [213] = "PluginState",
        [215] = "ChanParams", [216] = "AutomationChannels", [218] = "ChanEnvelope", [219] = "ChanLevels",
        [220] = "ChanFilter", [221] = "ChanPolyphony", [223] = "PatternControllers", [224] = "PatternNotes",
        [225] = "MixerParams", [227] = "RemoteControl", [228] = "ChanTracking", [229] = "ChanLevelOffsets",
        [231] = "FilterGroupName", [233] = "PlaylistItems", [234] = "AutomationData", [235] = "InsertRouting",
        [236] = "InsertFlags", [237] = "Timestamp", [238] = "TrackData", [239] = "TrackName",
        [241] = "ArrangementName",
    };

    public static string Get(byte id) => Names.TryGetValue(id, out var n) ? n : id switch
    {
        < 64 => $"Byte_{id}",
        < 128 => $"Word_{id}",
        < 192 => $"DWord_{id}",
        _ => $"Data_{id}",
    };
}
