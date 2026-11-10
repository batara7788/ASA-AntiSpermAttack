using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Asa.App.Services;

public static class SignatureVerifier
{
    private static readonly Guid GenericVerifyAction = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private static readonly ConcurrentDictionary<string, (DateTime Modified, bool Valid)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private const uint UiNone = 2;
    private const uint ChoiceFile = 1;
    private const uint RevokeNone = 0;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint CacheOnlyUrlRetrieval = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFileInfo
    {
        public uint StructSize;
        public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    public static bool? HasValidSignature(string path)
    {
        try
        {
            DateTime modified = File.GetLastWriteTimeUtc(path);
            if (Cache.TryGetValue(path, out var cached) && cached.Modified == modified)
            {
                return cached.Valid;
            }

            bool valid = Verify(path);
            Cache[path] = (modified, valid);
            return valid;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static bool Verify(string path)
    {
        var fileInfo = new TrustFileInfo { StructSize = (uint)Marshal.SizeOf<TrustFileInfo>(), FilePath = path };
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var data = new TrustData
            {
                StructSize = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = StateActionVerify,
                ProviderFlags = CacheOnlyUrlRetrieval
            };

            Guid action = GenericVerifyAction;
            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.StateAction = StateActionClose;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.DestroyStructure<TrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }
}
