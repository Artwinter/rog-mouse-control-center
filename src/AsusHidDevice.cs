using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RogMouse
{
    /// <summary>
    /// One ASUS vendor HID collection (usage page 0xFF01) currently present on the machine.
    /// The vendor collection is the only interface that answers the ASUS control protocol.
    /// </summary>
    public sealed class AsusInterface
    {
        public string Path = "";
        public ushort VendorId;
        public ushort ProductId;
        public string ProductName = "";
    }

    public sealed class AsusHidDevice : IDisposable
    {
        public const int PacketSize = 64;
        private const int BufferSize = 65;
        private const int TimeoutMs = 400;

        private SafeFileHandle _handle;
        private readonly object _sync = new object();
        private bool _disposed;
        private readonly byte[] _outBuf = new byte[BufferSize];
        private readonly byte[] _inBuf = new byte[BufferSize];

        public string Path { get; private set; }
        public ushort VendorId { get; private set; }
        public ushort ProductId { get; private set; }
        public string ProductName { get; private set; }

        public static string Diag = "";

        private AsusHidDevice() { }

        /// <summary>
        /// Lists every ASUS (VID 0x0B05) vendor collection, with its product string.
        /// Enumeration does not talk to the mouse, so it keeps working while the mouse sleeps.
        /// </summary>
        public static List<AsusInterface> EnumerateVendorInterfaces()
        {
            var result = new List<AsusInterface>();
            Guid hidGuid;
            NativeMethods.HidD_GetHidGuid(out hidGuid);
            IntPtr set = NativeMethods.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return result;
            try
            {
                var did = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVICE_INTERFACE_DATA));
                for (int i = 0; ; i++)
                {
                    if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref did)) break;
                    int required = 0;
                    NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                    if (required <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref did, detail, required, ref required, IntPtr.Zero)) continue;
                        int pathOffset = IntPtr.Size == 8 ? 8 : 4;
                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + pathOffset));
                        if (string.IsNullOrEmpty(path)) continue;
                        ushort vid, pid;
                        if (!ParseVidPid(path, out vid, out pid)) continue;
                        if (vid != 0x0B05) continue;
                        var item = ProbeInterface(path, vid, pid);
                        if (item != null) result.Add(item);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { NativeMethods.SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        private static AsusInterface ProbeInterface(string path, ushort vid, ushort pid)
        {
            SafeFileHandle h = NativeMethods.CreateFile(path,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            try
            {
                IntPtr preparsed;
                if (!NativeMethods.HidD_GetPreparsedData(h, out preparsed)) return null;
                var caps = new NativeMethods.HIDP_CAPS();
                caps.Reserved = new ushort[17];
                try
                {
                    // HidP_GetCaps returns HIDP_STATUS_SUCCESS (0x00110000), not 0.
                    if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != 0x00110000) return null;
                }
                finally { NativeMethods.HidD_FreePreparsedData(preparsed); }
                if (caps.UsagePage != 0xFF01) return null;

                var item = new AsusInterface();
                item.Path = path;
                item.VendorId = vid;
                item.ProductId = pid;
                item.ProductName = ReadProductString(h);
                return item;
            }
            finally { h.Dispose(); }
        }

        private static string ReadProductString(SafeFileHandle h)
        {
            var buf = new byte[512];
            if (!NativeMethods.HidD_GetProductString(h, buf, buf.Length)) return "";
            string s = System.Text.Encoding.Unicode.GetString(buf);
            int z = s.IndexOf('\0');
            if (z >= 0) s = s.Substring(0, z);
            return s.Trim();
        }

        /// <summary>Backwards-compatible wrapper returning just the paths and product ids.</summary>
        public static List<string> EnumerateAsusInterfaces(out List<ushort> pids)
        {
            pids = new List<ushort>();
            var list = EnumerateVendorInterfaces();
            var paths = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                paths.Add(list[i].Path);
                pids.Add(list[i].ProductId);
            }
            return paths;
        }

        private static bool ParseVidPid(string path, out ushort vid, out ushort pid)
        {
            vid = 0; pid = 0;
            string lower = path.ToLowerInvariant();
            int vi = lower.IndexOf("vid_");
            int pi = lower.IndexOf("pid_");
            if (vi < 0 || pi < 0) return false;
            return ushort.TryParse(lower.Substring(vi + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vid)
                && ushort.TryParse(lower.Substring(pi + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out pid);
        }

        /// <summary>
        /// Opens the collection that actually answers the protocol. When more than one ASUS
        /// vendor collection is present (receiver plus a cabled mouse, for example) a read-only
        /// probe decides which one is live; the others are closed again.
        /// </summary>
        public static AsusHidDevice OpenFirst()
        {
            var list = EnumerateVendorInterfaces();
            if (list.Count == 0) return null;

            var opened = new List<AsusHidDevice>();
            for (int i = 0; i < list.Count; i++)
            {
                var d = Open(list[i].Path, list[i].ProductId, list[i].ProductName);
                if (d != null) opened.Add(d);
            }
            if (opened.Count == 0) return null;
            if (opened.Count == 1) return opened[0];

            for (int i = 0; i < opened.Count; i++)
            {
                byte[] r = opened[i].Query(AsusProtocol.CmdGetProfile, new byte[] { 0 });
                if (r != null && r[8] != 0)
                {
                    for (int j = 0; j < opened.Count; j++)
                        if (j != i) opened[j].Dispose();
                    return opened[i];
                }
            }
            for (int j = 1; j < opened.Count; j++) opened[j].Dispose();
            return opened[0];
        }

        public static AsusHidDevice Open(string path)
        {
            return Open(path, 0, "");
        }

        public static AsusHidDevice Open(string path, ushort pid, string productName)
        {
            // IMPORTANT: the ASUS vendor collection only answers on a SYNCHRONOUS handle.
            // Any FILE_FLAG_OVERLAPPED handle times out (verified by experiment).
            SafeFileHandle h = NativeMethods.CreateFile(path,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            var d = new AsusHidDevice();
            d._handle = h;
            d.Path = path;
            ushort vid, ipid;
            ParseVidPid(path, out vid, out ipid);
            d.VendorId = vid;
            d.ProductId = pid != 0 ? pid : ipid;
            d.ProductName = productName == null ? "" : productName;
            return d;
        }

        /// <summary>
        /// Sends one 64-byte ASUS request and returns the 64-byte response (report id stripped,
        /// so [0]/[1] hold the command echo and [4..] hold the payload).
        /// </summary>
        public byte[] Query(ushort cmd, byte[] parameters)
        {
            lock (_sync)
            {
                if (_disposed || _handle == null || _handle.IsInvalid) return null;

                Array.Clear(_outBuf, 0, _outBuf.Length);
                _outBuf[0] = 0x00;
                _outBuf[1] = (byte)(cmd & 0xFF);
                _outBuf[2] = (byte)((cmd >> 8) & 0xFF);
                if (parameters != null)
                    Array.Copy(parameters, 0, _outBuf, 3, Math.Min(parameters.Length, BufferSize - 3));

                // Watchdog: a synchronous ReadFile on this device can block forever when the
                // mouse is asleep, so cancel it from a timer thread after TimeoutMs.
                IntPtr threadHandle;
                if (!NativeMethods.DuplicateHandle(NativeMethods.GetCurrentProcess(), NativeMethods.GetCurrentThread(),
                        NativeMethods.GetCurrentProcess(), out threadHandle, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                    return null;

                var watchdog = new Canceller(threadHandle);

                try
                {
                    int written;
                    watchdog.Arm(TimeoutMs);
                    bool wrote = NativeMethods.WriteFile(_handle, _outBuf, BufferSize, out written, IntPtr.Zero);
                    int writeErr = wrote ? 0 : Marshal.GetLastWin32Error();
                    watchdog.Disarm();
                    if (!wrote) { Diag += "W_ERR" + writeErr + ";"; return null; }

                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        Array.Clear(_inBuf, 0, _inBuf.Length);
                        int read;
                        watchdog.Arm(TimeoutMs);
                        bool readOk = NativeMethods.ReadFile(_handle, _inBuf, BufferSize, out read, IntPtr.Zero);
                        int readErr = readOk ? 0 : Marshal.GetLastWin32Error();
                        watchdog.Disarm();

                        if (!readOk) { Diag += "R_ERR" + readErr + ";"; return null; }
                        if (read < 4) continue;
                        if (_inBuf[1] == 0xFF && _inBuf[2] == 0xAA) { Diag += "STATUS_ERROR;"; return null; }
                        if (_inBuf[1] == (byte)(cmd & 0xFF) && _inBuf[2] == (byte)((cmd >> 8) & 0xFF))
                        {
                            // Strip the leading report id so the caller sees the same layout
                            // libratbag uses: [0..1] command echo, [4..] payload.
                            var resp = new byte[PacketSize];
                            Array.Copy(_inBuf, 1, resp, 0, PacketSize);
                            return resp;
                        }
                    }
                    Diag += "NO_MATCH;";
                    return null;
                }
                finally
                {
                    watchdog.Dispose();
                    NativeMethods.CloseHandle(threadHandle);
                }
            }
        }

        /// <summary>Arms a cancel of the owning thread's synchronous I/O after a delay.</summary>
        private sealed class Canceller : IDisposable
        {
            private readonly IntPtr _thread;
            private Timer _timer;
            private int _fired;

            public Canceller(IntPtr thread) { _thread = thread; }

            public void Arm(int ms)
            {
                Interlocked.Exchange(ref _fired, 0);
                _timer = new Timer(OnTick, null, ms, Timeout.Infinite);
            }

            public void Disarm()
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
            }

            private void OnTick(object state)
            {
                Interlocked.Exchange(ref _fired, 1);
                NetDiag.Timeouts++;
                NativeMethods.CancelSynchronousIo(_thread);
            }

            public void Dispose() { Disarm(); }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                if (_handle != null && !_handle.IsInvalid) _handle.Dispose();
            }
        }
    }

    public static class NetDiag
    {
        public static int Timeouts;
    }
}
