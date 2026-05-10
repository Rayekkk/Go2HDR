using System.Runtime.InteropServices;

namespace Go2HDR.Services;

#region P/Invoke structures

[StructLayout(LayoutKind.Sequential)]
struct LUID { public uint LowPart; public int HighPart; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID adapterId; public uint id; public uint modeInfoIdx;
    public uint outputTechnology; public int rotation; public int scaling;
    public DISPLAYCONFIG_RATIONAL refreshRate; public int scanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_MODE_INFO { public int infoType; public uint id; public LUID adapterId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] modeInfoData; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_SET_SDR_WHITE_LEVEL { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint SDRWhiteLevel; public byte finalValue; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint value; public int colorEncoding; public int bitsPerColorChannel; }

#endregion

public class DisplayConfigService
{
    const uint QDC_ONLY_ACTIVE_PATHS    = 0x00000002;
    const int  DC_SET_SDR_WHITE_LEVEL   = unchecked((int)0xFFFFFFEE);
    const int  DC_GET_ADVANCED_COLOR_INFO = 9;
    const uint OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint pc, out uint mc);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pc, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint mc, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr tid);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_SDR_WHITE_LEVEL r);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO r);

    private static readonly uint SizeGetAdvancedColorInfo = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
    private static readonly uint SizeSetSdrWhiteLevel     = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_SDR_WHITE_LEVEL>();

    // Cache display paths for up to 4 s to avoid repeated kernel calls on every poll and SDR apply.
    // Accessed from both the UI thread (poll timer) and WMI thread (brightness watcher).
    private readonly object _cacheLock = new();
    private DISPLAYCONFIG_PATH_INFO[]? _pathCache;
    private long _pathCacheTick;                    // Environment.TickCount64
    private const long PathCacheTtlMs = 4_000;

    public void InvalidateCache()
    {
        lock (_cacheLock) { _pathCache = null; }
    }

    private bool GetPaths(out DISPLAYCONFIG_PATH_INFO[] paths)
    {
        lock (_cacheLock)
        {
            if (_pathCache != null && Environment.TickCount64 - _pathCacheTick < PathCacheTtlMs)
            {
                paths = _pathCache;
                return true;
            }
        }

        paths = [];
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pc, out uint mc) != 0) return false;
        var pa = new DISPLAYCONFIG_PATH_INFO[pc];
        var ma = new DISPLAYCONFIG_MODE_INFO[mc];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pc, pa, ref mc, ma, IntPtr.Zero) != 0) return false;

        lock (_cacheLock) { _pathCache = pa; _pathCacheTick = Environment.TickCount64; }
        paths = pa;
        return true;
    }

    public bool IsBuiltInHdrActive()
    {
        try
        {
            if (!GetPaths(out var paths)) return false;
            foreach (var path in paths)
            {
                if (path.targetInfo.outputTechnology != OUTPUT_TECHNOLOGY_INTERNAL) continue;
                var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type       = DC_GET_ADVANCED_COLOR_INFO,
                        size       = SizeGetAdvancedColorInfo,
                        adapterId  = path.targetInfo.adapterId,
                        id         = path.targetInfo.id
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref info) == 0 && (info.value & 0x2) != 0)
                    return true;
            }
        }
        catch { }
        return false;
    }

    public void SetSdrWhiteLevel(int sdrValue)
    {
        try
        {
            uint raw = (uint)((80 + sdrValue * 4) * 1000 / 80);
            if (!GetPaths(out var paths)) return;
            foreach (var path in paths)
            {
                var packet = new DISPLAYCONFIG_SET_SDR_WHITE_LEVEL
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type      = DC_SET_SDR_WHITE_LEVEL,
                        size      = SizeSetSdrWhiteLevel,
                        adapterId = path.targetInfo.adapterId,
                        id        = path.targetInfo.id
                    },
                    SDRWhiteLevel = raw,
                    finalValue    = 1
                };
                DisplayConfigSetDeviceInfo(ref packet);
            }
        }
        catch { }
    }
}
