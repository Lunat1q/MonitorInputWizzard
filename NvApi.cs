using System.Runtime.InteropServices;
using System.Text;

namespace MonitorInputWizzard;

/// <summary>
/// DDC/CI over NVIDIA NVAPI raw I2C — reaches the 0x50 service sidechannel that the dxva2 API can't,
/// which is the only way to switch input on LG monitors that ignore standard 0x60/0xF4 writes.
/// ponytail: fires at every connected display on every NVIDIA GPU. The LG acts on 0xF4; other
/// monitors ignore an unknown VCP. Per-display targeting (match by EDID) is the upgrade if that bites.
/// </summary>
public sealed class NvApi : IDisposable
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitDlg();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UnloadDlg();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumGpusDlg([Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDisplayIdsDlg(IntPtr gpu, [In, Out] NV_GPU_DISPLAYIDS[]? ids, ref uint count, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int I2cWriteDlg(IntPtr gpu, ref NV_I2C_INFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NV_GPU_DISPLAYIDS { public uint version, connectorType, displayId, flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NV_I2C_INFO
    {
        public uint version;
        public uint displayMask;
        public byte bIsDDCPort;
        public byte i2cDevAddress;
        public IntPtr pbI2cRegAddress;
        public uint regAddrSize;
        public IntPtr pbData;
        public uint cbSize;
        public uint i2cSpeed;
        public uint i2cSpeedKhz;
        public byte portId;
        public uint bIsPortIdSet;
    }

    private readonly InitDlg _init;
    private readonly UnloadDlg _unload;
    private readonly EnumGpusDlg _enum;
    private readonly GetDisplayIdsDlg _getIds;
    private readonly I2cWriteDlg _write;
    private bool _ready;

    private static T Fn<T>(uint id) => Marshal.GetDelegateForFunctionPointer<T>(QueryInterface(id));
    private static uint Ver<T>(uint v) => (uint)Marshal.SizeOf<T>() | (v << 16);

    public NvApi()
    {
        _init = Fn<InitDlg>(0x0150E828);
        _unload = Fn<UnloadDlg>(0xD22BDD7E);
        _enum = Fn<EnumGpusDlg>(0xE5AC921F);
        _getIds = Fn<GetDisplayIdsDlg>(0x0078DBA2);
        _write = Fn<I2cWriteDlg>(0xE812EB07);
        _ready = _init() == 0;
    }

    /// <summary>Sends a DDC/CI Set-VCP over raw I2C to every connected display. Returns a per-display status log.</summary>
    public string SetVcp(byte vcp, ushort value, byte sourceAddr)
    {
        if (!_ready) return "NVAPI init failed.";
        var gpus = new IntPtr[64];
        if (_enum(gpus, out int gpuCount) != 0 || gpuCount == 0) return "NVAPI: no GPUs.";

        byte hi = (byte)(value >> 8), lo = (byte)value, len = 0x84, cmd = 0x03;
        // DDC/CI host->display checksum seeds with the display write address (0x6E).
        byte chk = (byte)(0x6E ^ sourceAddr ^ len ^ cmd ^ vcp ^ hi ^ lo);
        byte[] reg = { sourceAddr };
        byte[] data = { len, cmd, vcp, hi, lo, chk };
        var log = new StringBuilder();

        for (int g = 0; g < gpuCount; g++)
        {
            uint cnt = 0;
            if (_getIds(gpus[g], null, ref cnt, 0) != 0 || cnt == 0) continue;
            var ids = new NV_GPU_DISPLAYIDS[cnt];
            for (int i = 0; i < cnt; i++) ids[i].version = Ver<NV_GPU_DISPLAYIDS>(3);
            if (_getIds(gpus[g], ids, ref cnt, 0) != 0) continue;

            foreach (var d in ids)
            {
                var regH = GCHandle.Alloc(reg, GCHandleType.Pinned);
                var dataH = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    var info = new NV_I2C_INFO
                    {
                        version = Ver<NV_I2C_INFO>(3),
                        displayMask = d.displayId,
                        bIsDDCPort = 1,
                        i2cDevAddress = 0x6E,
                        pbI2cRegAddress = regH.AddrOfPinnedObject(),
                        regAddrSize = (uint)reg.Length,
                        pbData = dataH.AddrOfPinnedObject(),
                        cbSize = (uint)data.Length,
                        i2cSpeed = 0xFFFF,
                        i2cSpeedKhz = 4, // NV_I2C_SPEED_100KHZ
                    };
                    int st = _write(gpus[g], ref info);
                    log.Append($"[0x{d.displayId:X8}:{(st == 0 ? "OK" : "err " + st)}] ");
                }
                finally { regH.Free(); dataH.Free(); }
            }
        }
        return log.Length == 0 ? "No connected displays." : log.ToString();
    }

    public void Dispose() { if (_ready) { try { _unload(); } catch { } _ready = false; } }
}
