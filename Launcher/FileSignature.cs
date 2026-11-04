using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace FLPTools.Launcher;

/// <summary>
/// Checks whether an install's main program files still carry Image-Line's trusted Authenticode signature,
/// using Windows' own WinVerifyTrust (no network revocation check, so it stays fast and offline).
/// </summary>
public static class FileSignature
{
    public sealed record Result(string Status, string Detail);   // Status: "signed", "modified" or "unknown"

    static readonly Dictionary<string, (DateTime Stamp, Result Result)> cache = new(StringComparer.OrdinalIgnoreCase);

    public static Result CheckInstall(string dir)
    {
        var files = new[] { "FL64.exe", "FLEngine_x64.dll" }.Select(f => Path.Combine(dir, f)).Where(File.Exists).ToList();
        if (files.Count == 0) return new("unknown", "Program files not found");
        var results = files.Select(Check).ToList();
        var bad = results.FirstOrDefault(r => r.Status == "modified");
        if (bad != null) return bad;
        var unknown = results.FirstOrDefault(r => r.Status == "unknown");
        return unknown ?? new("signed", "Signed by Image-Line");
    }

    public static Result Check(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        lock (cache)
            if (cache.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Result;
        var r = Verify(path);
        lock (cache) cache[path] = (stamp, r);
        return r;
    }

    static Result Verify(string path)
    {
        string name = Path.GetFileName(path);
        uint code = WinVerify(path);
        string signer = "";
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            signer = cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch { }
        bool imageLine = signer.Replace("-", " ").Contains("Image Line", StringComparison.OrdinalIgnoreCase);
        return code switch
        {
            0 when imageLine => new("signed", "Signed by Image-Line"),
            0 => new("modified", $"{name} is signed by \"{signer}\", not Image-Line"),
            0x800B0100 => new("modified", $"{name} has no signature"),
            0x80096010 => new("modified", $"{name} was changed after it was signed"),
            0x800B0109 or 0x800B010A or 0x800B0111 or 0x80096019 =>
                new("modified", $"{name} is signed by a certificate Windows doesn't trust{(signer.Length > 0 ? $" (calls itself \"{signer}\")" : "")}"),
            _ => new("unknown", $"{name}: Windows couldn't check the signature (0x{code:X8})"),
        };
    }

    // ------------------------------------------------------------------ WinVerifyTrust

    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;           // 2 = WTD_UI_NONE
        public uint fdwRevocationChecks;  // 0 = WTD_REVOKE_NONE
        public uint dwUnionChoice;        // 1 = WTD_CHOICE_FILE
        public IntPtr pFile;
        public uint dwStateAction;        // 1 = VERIFY, 2 = CLOSE
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;          // 0x40 = WTD_CACHE_ONLY_URL_RETRIEVAL (stay offline)
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

    static uint WinVerify(string path)
    {
        var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2, fdwRevocationChecks = 0,
                dwUnionChoice = 1, pFile = pFile, dwStateAction = 1, dwProvFlags = 0x40,
            };
            Marshal.StructureToPtr(data, pData, false);
            uint result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);
            data = Marshal.PtrToStructure<WINTRUST_DATA>(pData);
            data.dwStateAction = 2;
            Marshal.StructureToPtr(data, pData, true);
            WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
            Marshal.FreeHGlobal(pFile);
        }
    }
}
