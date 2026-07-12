using System.Runtime.InteropServices;

namespace MonitorInputWizzard;

/// <summary>Enumerates physical monitors and drives DDC/CI VCP feature 0x60 (input source).</summary>
public sealed class MonitorController : IDisposable
{
    public readonly record struct Monitor(string Description, IntPtr Handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, IntPtr type, out uint current, out uint max);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    private readonly List<IntPtr> _handles = new();

    public List<Monitor> Enumerate()
    {
        Dispose(); // release previous handles before re-enumerating
        var result = new List<Monitor>();

        int index = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) && count > 0)
            {
                var phys = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMon, count, phys))
                {
                    var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                    string where = GetMonitorInfo(hMon, ref info)
                        ? $"{info.rcMonitor.right - info.rcMonitor.left}×{info.rcMonitor.bottom - info.rcMonitor.top}"
                          + $" @({info.rcMonitor.left},{info.rcMonitor.top})" + ((info.dwFlags & 1) != 0 ? " [primary]" : "")
                        : "";
                    foreach (var p in phys)
                    {
                        _handles.Add(p.hPhysicalMonitor);
                        result.Add(new Monitor($"#{++index} {p.szPhysicalMonitorDescription} — {where}", p.hPhysicalMonitor));
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public (uint cur, uint max)? GetVcp(IntPtr handle, byte code)
        => GetVCPFeatureAndVCPFeatureReply(handle, code, IntPtr.Zero, out uint cur, out uint max) ? (cur, max) : null;

    public bool SetVcp(IntPtr handle, byte code, uint value) => SetVCPFeature(handle, code, value);

    public void Dispose()
    {
        foreach (var h in _handles) DestroyPhysicalMonitor(h);
        _handles.Clear();
    }
}
