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

[StructLayout(LayoutKind.Explicit, Size = 64)]
struct DISPLAYCONFIG_MODE_INFO_UNION { }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_MODE_INFO { public int infoType; public uint id; public LUID adapterId; public DISPLAYCONFIG_MODE_INFO_UNION modeInfo; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_SET_SDR_WHITE_LEVEL { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint SDRWhiteLevel; public byte finalValue; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_SDR_WHITE_LEVEL { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint SDRWhiteLevel; }

[StructLayout(LayoutKind.Sequential)]
struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint value;
    public int colorEncoding;
    public uint bitsPerColorChannel;
    public int activeColorMode;
}

#endregion

public enum BuiltInHdrState
{
    Unavailable,
    Inactive,
    Active
}

public class DisplayConfigService
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int DC_SET_SDR_WHITE_LEVEL = unchecked((int)0xFFFFFFEE);
    private const int DC_GET_SDR_WHITE_LEVEL = 11;
    private const int DC_GET_ADVANCED_COLOR_INFO_2 = 15;
    private const int ADVANCED_COLOR_MODE_HDR = 2;

    private const uint OUTPUT_TECHNOLOGY_LVDS = 6;
    private const uint OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
    private const uint OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;
    private const uint OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pc, out uint mc);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pc, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint mc, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr tid);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_SDR_WHITE_LEVEL request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL request);

    private static readonly uint SizeGetAdvancedColorInfo2 = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>();
    private static readonly uint SizeGetSdrWhiteLevel = (uint)Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>();
    private static readonly uint SizeSetSdrWhiteLevel = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_SDR_WHITE_LEVEL>();

    private readonly object _cacheLock = new();
    private DISPLAYCONFIG_PATH_INFO[]? _pathCache;
    private DisplayTarget[] _activeHdrTargets = [];
    private long _pathCacheTick;
    private const long PathCacheTtlMs = 4_000;

    private readonly record struct DisplayTarget(LUID AdapterId, uint Id);

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _pathCache = null;
            _activeHdrTargets = [];
        }
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
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (result != ERROR_SUCCESS)
            {
                AppLog.Write($"GetDisplayConfigBufferSizes failed with Win32 error {result}.");
                return false;
            }

            var pathBuffer = new DISPLAYCONFIG_PATH_INFO[checked((int)pathCount)];
            var modeBuffer = new DISPLAYCONFIG_MODE_INFO[checked((int)modeCount)];
            result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, pathBuffer,
                ref modeCount, modeBuffer, IntPtr.Zero);

            if (result == ERROR_INSUFFICIENT_BUFFER)
                continue;

            if (result != ERROR_SUCCESS)
            {
                AppLog.Write($"QueryDisplayConfig failed with Win32 error {result}.");
                return false;
            }

            if (pathCount != pathBuffer.Length)
                Array.Resize(ref pathBuffer, checked((int)pathCount));

            lock (_cacheLock)
            {
                _pathCache = pathBuffer;
                _pathCacheTick = Environment.TickCount64;
            }

            paths = pathBuffer;
            return true;
        }

        AppLog.Write("QueryDisplayConfig topology changed repeatedly while it was being read.");
        return false;
    }

    public BuiltInHdrState GetBuiltInHdrState()
    {
        try
        {
            if (!GetPaths(out var paths))
                return BuiltInHdrState.Unavailable;

            var activeTargets = new HashSet<DisplayTarget>();
            bool queriedBuiltInTarget = false;
            bool failedBuiltInTarget = false;

            foreach (var path in paths)
            {
                if (!IsBuiltIn(path.targetInfo.outputTechnology))
                    continue;

                var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DC_GET_ADVANCED_COLOR_INFO_2,
                        size = SizeGetAdvancedColorInfo2,
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                int result = DisplayConfigGetDeviceInfo(ref info);
                if (result != ERROR_SUCCESS)
                {
                    AppLog.Write($"GET_ADVANCED_COLOR_INFO_2 failed for built-in target {path.targetInfo.id} " +
                                 $"with Win32 error {result}.");
                    failedBuiltInTarget = true;
                    continue;
                }

                queriedBuiltInTarget = true;
                if (ClassifyAdvancedColorInfo2(info.value, info.activeColorMode) == BuiltInHdrState.Active)
                    activeTargets.Add(new DisplayTarget(path.targetInfo.adapterId, path.targetInfo.id));
            }

            if (activeTargets.Count > 0)
            {
                lock (_cacheLock)
                    _activeHdrTargets = [.. activeTargets];
                return BuiltInHdrState.Active;
            }

            if (failedBuiltInTarget)
                return BuiltInHdrState.Unavailable;

            lock (_cacheLock)
                _activeHdrTargets = [];

            return queriedBuiltInTarget ? BuiltInHdrState.Inactive : BuiltInHdrState.Unavailable;
        }
        catch (Exception ex)
        {
            AppLog.Write("HDR state detection failed.", ex);
            return BuiltInHdrState.Unavailable;
        }
    }

    internal static BuiltInHdrState ClassifyAdvancedColorInfo2(uint flags, int activeColorMode)
    {
        const uint highDynamicRangeSupported = 1u << 4;

        if (activeColorMode == ADVANCED_COLOR_MODE_HDR)
            return BuiltInHdrState.Active;

        return (flags & highDynamicRangeSupported) != 0
            ? BuiltInHdrState.Inactive
            : BuiltInHdrState.Unavailable;
    }

    public bool SetSdrWhiteLevel(int sdrValue)
    {
        try
        {
            uint raw = SdrValueToRaw(sdrValue);

            DisplayTarget[] targets;
            lock (_cacheLock)
                targets = [.. _activeHdrTargets];

            if (targets.Length == 0)
            {
                AppLog.Write("Skipped SDR white-level update because no active built-in HDR target is known.");
                return false;
            }

            bool allSucceeded = true;
            foreach (var target in targets)
            {
                var packet = new DISPLAYCONFIG_SET_SDR_WHITE_LEVEL
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DC_SET_SDR_WHITE_LEVEL,
                        size = SizeSetSdrWhiteLevel,
                        adapterId = target.AdapterId,
                        id = target.Id
                    },
                    SDRWhiteLevel = raw,
                    finalValue = 1
                };

                int result = DisplayConfigSetDeviceInfo(ref packet);
                if (result != ERROR_SUCCESS)
                {
                    allSucceeded = false;
                    AppLog.Write($"Setting SDR white level on built-in target {target.Id} failed with Win32 error {result}.");
                }
                else if (!VerifySdrWhiteLevel(target, raw))
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }
        catch (Exception ex)
        {
            AppLog.Write("Setting SDR white level failed.", ex);
            return false;
        }
    }

    internal static uint SdrValueToRaw(int sdrValue)
    {
        sdrValue = Math.Clamp(sdrValue, 0, 100);
        return (uint)((80 + sdrValue * 4) * 1000 / 80);
    }

    private static bool VerifySdrWhiteLevel(DisplayTarget target, uint expectedRaw)
    {
        var request = new DISPLAYCONFIG_SDR_WHITE_LEVEL
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DC_GET_SDR_WHITE_LEVEL,
                size = SizeGetSdrWhiteLevel,
                adapterId = target.AdapterId,
                id = target.Id
            }
        };

        int result = DisplayConfigGetDeviceInfo(ref request);
        if (result != ERROR_SUCCESS)
        {
            AppLog.Write($"Reading back SDR white level from built-in target {target.Id} failed with Win32 error {result}.");
            return false;
        }

        if (request.SDRWhiteLevel == expectedRaw)
            return true;

        AppLog.Write($"SDR white-level readback mismatch on built-in target {target.Id}: " +
                     $"expected {expectedRaw}, received {request.SDRWhiteLevel}.");
        return false;
    }

    private static bool IsBuiltIn(uint outputTechnology) => outputTechnology is
        OUTPUT_TECHNOLOGY_LVDS or
        OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED or
        OUTPUT_TECHNOLOGY_UDI_EMBEDDED or
        OUTPUT_TECHNOLOGY_INTERNAL;
}
