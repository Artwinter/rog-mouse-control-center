using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RogMouse
{
    public sealed class RazerHidInterface
    {
        public string Path = "";
        public ushort VendorId;
        public ushort ProductId;
        public string ProductName = "";
        public ushort UsagePage;
        public ushort Usage;
        public ushort InputLength;
        public ushort OutputLength;
        public ushort FeatureLength;
    }

    /// <summary>
    /// Read-only transport for the BlackShark V2 Pro (2023) 2.4 GHz receiver. The receiver
    /// is a normal HID device, but it speaks Razer's private "PA" protocol on its 0xFF00
    /// vendor collection. It is deliberately shared: Synapse and the audio driver may hold
    /// the same collection, and an exclusive open returns Windows error 32.
    /// </summary>
    public sealed class RazerHidDevice : IDisposable
    {
        private const int WriteTimeoutMs = 400;
        private const int ReplyWindowMs = 1400;
        private const int ReadSliceMs = 40;

        public const ushort RazerVendorId = 0x1532;
        public const ushort BlackSharkV2ProPid = 0x0555;
        public const ushort VendorUsagePage = 0xFF00;
        public const string ProductNeedle = "BlackShark V2 Pro";

        private SafeFileHandle _handle;
        private readonly object _sync = new object();
        private readonly byte[] _out = new byte[RazerBlackSharkProtocol.PacketSize];
        private readonly byte[] _in = new byte[RazerBlackSharkProtocol.PacketSize];
        private bool _disposed;
        public string Path { get; private set; }
        public string ProductName { get; private set; }
        public RazerHidInterface Interface { get; private set; }

        private RazerHidDevice() { }

        public static RazerHidInterface Select(RazerHidInterface[] all, ushort vendorId,
            ushort productId, string productNeedle, ushort usagePage)
        {
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                if (it.VendorId != vendorId || it.ProductId != productId) continue;
                if (it.UsagePage != usagePage) continue;
                string name = it.ProductName ?? "";
                if (name.IndexOf(productNeedle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                return it;
            }
            return null;
        }

        /// <summary>
        /// Caps include the leading report ID in their byte lengths. A candidate whose
        /// output collection cannot carry the 64-byte PA frame would accept the open and
        /// then fail every write, so it is rejected before any device traffic.
        /// </summary>
        public static bool HasProtocolLengths(RazerHidInterface item)
        {
            if (item == null) return false;
            return item.InputLength >= RazerBlackSharkProtocol.PacketSize
                && item.OutputLength >= RazerBlackSharkProtocol.PacketSize;
        }

        /// <summary>
        /// Enumerates every present HID collection, preserving the descriptor fields needed
        /// to distinguish the headset control collection from its sibling consumer-control
        /// collection and from unrelated Razer peripherals.
        /// </summary>
        public static List<RazerHidInterface> Enumerate()
        {
            var result = new List<RazerHidInterface>();
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
                    if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref did))
                        break;
                    int required = 0;
                    NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0,
                        ref required, IntPtr.Zero);
                    if (required <= 0) continue;
                    IntPtr mem = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(mem, IntPtr.Size == 8 ? 8 : 6);
                        if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(set, ref did, mem, required,
                            ref required, IntPtr.Zero)) continue;
                        int pathOffset = IntPtr.Size == 8 ? 8 : 4;
                        string path = Marshal.PtrToStringUni(new IntPtr(mem.ToInt64() + pathOffset));
                        if (string.IsNullOrEmpty(path)) continue;
                        var item = ReadInterface(path);
                        if (item != null) result.Add(item);
                    }
                    finally { Marshal.FreeHGlobal(mem); }
                }
            }
            finally { NativeMethods.SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        private static RazerHidInterface ReadInterface(string path)
        {
            SafeFileHandle h = NativeMethods.CreateFile(path,
                NativeMethods.GENERIC_READ, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            try
            {
                var attrs = new NativeMethods.HIDD_ATTRIBUTES();
                attrs.Size = Marshal.SizeOf(typeof(NativeMethods.HIDD_ATTRIBUTES));
                if (!NativeMethods.HidD_GetAttributes(h, ref attrs)) return null;

                IntPtr preparsed;
                if (!NativeMethods.HidD_GetPreparsedData(h, out preparsed)) return null;
                var caps = new NativeMethods.HIDP_CAPS();
                caps.Reserved = new ushort[17];
                try
                {
                    if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != 0x00110000) return null;
                }
                finally { NativeMethods.HidD_FreePreparsedData(preparsed); }

                var item = new RazerHidInterface();
                item.Path = path;
                item.VendorId = attrs.VendorID;
                item.ProductId = attrs.ProductID;
                item.ProductName = ReadProductString(h);
                item.UsagePage = caps.UsagePage;
                item.Usage = caps.Usage;
                item.InputLength = caps.InputReportByteLength;
                item.OutputLength = caps.OutputReportByteLength;
                item.FeatureLength = caps.FeatureReportByteLength;
                return item;
            }
            finally { h.Dispose(); }
        }

        private static string ReadProductString(SafeFileHandle h)
        {
            var buf = new byte[512];
            if (!NativeMethods.HidD_GetProductString(h, buf, buf.Length)) return "";
            string s = Encoding.Unicode.GetString(buf);
            int z = s.IndexOf('\0');
            if (z >= 0) s = s.Substring(0, z);
            return s.Trim();
        }

        public static RazerHidDevice OpenFirst(ushort vendorId, ushort productId,
            string productNeedle, out string error)
        {
            error = null;
            var all = Enumerate();
            var wanted = Select(all.ToArray(), vendorId, productId, productNeedle, VendorUsagePage);
            if (wanted == null)
            {
                error = "未找到 Razer BlackShark V2 Pro 2.4 GHz 接收器";
                return null;
            }
            if (!HasProtocolLengths(wanted))
            {
                error = "耳机控制集合不支持 64 字节报告";
                return null;
            }

            SafeFileHandle h = NativeMethods.CreateFile(wanted.Path,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();
                error = "无法打开耳机控制集合 (Win32 " + code + ")";
                h.Dispose();
                return null;
            }

            var device = new RazerHidDevice();
            device._handle = h;
            device.Path = wanted.Path;
            device.ProductName = wanted.ProductName;
            device.Interface = wanted;
            return device;
        }

        private static string Describe(RazerHidInterface item)
        {
            return "usage=" + item.UsagePage.ToString("X4") + ":" + item.Usage.ToString("X4")
                + " in=" + item.InputLength + " out=" + item.OutputLength
                + " product=" + item.ProductName;
        }

        public string DescribeCollection()
        {
            if (Interface == null) return "";
            return "usagePage=0x" + Interface.UsagePage.ToString("X4")
                + " usage=0x" + Interface.Usage.ToString("X4")
                + " input=" + Interface.InputLength
                + " output=" + Interface.OutputLength
                + " product=" + Interface.ProductName;
        }

        /// <summary>
        /// One complete read-only transaction. The radio may swallow the first frame as it
        /// wakes, so the OpenRazer/HaloBattery sequence is preserved: drain, remote-on,
        /// query with bounded retries, and remote-off in finally. No other command can be
        /// built by this API.
        /// </summary>
        public byte[] Query(byte command, int timeoutMs, out string error)
        {
            error = null;
            lock (_sync)
            {
                if (_disposed || _handle == null || _handle.IsInvalid)
                {
                    error = "耳机句柄已关闭";
                    return null;
                }

                try
                {
                    // A previous command's ACK must never satisfy this query. The receiver
                    // normally has a status report queued, but an idle/off headset may have none;
                    // this drain is therefore bounded rather than waiting for data.
                    DrainInput();
                    if (!WriteFrame(RazerBlackSharkProtocol.BuildRemote(true), out error)) return null;
                    Thread.Sleep(35);

                    // Reject anything except the two read-only query commands here as well as in
                    // the frame builder, so a future call site cannot turn this into a writer.
                    if (command != RazerBlackSharkProtocol.CommandBattery
                        && command != RazerBlackSharkProtocol.CommandCharging)
                    {
                        error = "不支持的耳机查询命令";
                        return null;
                    }
                    if (!WriteFrame(RazerBlackSharkProtocol.BuildQuery(command), out error)) return null;

                    long deadline = Environment.TickCount + Math.Max(timeoutMs, ReplyWindowMs);
                    for (int i = 0; i < 40; i++)
                    {
                        long left = deadline - Environment.TickCount;
                        if (left <= 0) break;
                        int read;
                        if (!ReadFrame(Math.Min(ReadSliceMs, (int)left), out read, out error)) return null;
                        if (read < 4) continue;
                        byte[] payload;
                        if (RazerBlackSharkProtocol.TryReadPayload(_in, command, out payload))
                            return payload;
                    }

                    // Retry once: after USB selective suspend the dongle can consume the first
                    // query while its radio wakes. This second pass still uses the same
                    // read-only command and never opens a second collection.
                    var plan = new HeadsetTransactionPlan();
                    plan.Start();
                    if (plan.NextPass())
                    {
                        if (!WriteFrame(RazerBlackSharkProtocol.BuildRemote(true), out error)) return null;
                        Thread.Sleep(35);
                        if (!WriteFrame(RazerBlackSharkProtocol.BuildQuery(command), out error)) return null;
                        deadline = Environment.TickCount + ReplyWindowMs;
                        for (int i = 0; i < 40; i++)
                        {
                            long left = deadline - Environment.TickCount;
                            if (left <= 0) break;
                            int read;
                            if (!ReadFrame(Math.Min(ReadSliceMs, (int)left), out read, out error)) return null;
                            if (read < 4) continue;
                            byte[] payload;
                            if (RazerBlackSharkProtocol.TryReadPayload(_in, command, out payload))
                                return payload;
                        }
                    }

                    error = "耳机无响应（接收器在线）";
                    return null;
                }
                finally
                {
                    // Remote-off is best effort; it must not block the worker after a timeout.
                    string ignored;
                    WriteFrame(RazerBlackSharkProtocol.BuildRemote(false), out ignored);
                }
            }
        }

        private void DrainInput()
        {
            for (int i = 0; i < 4; i++)
            {
                int read;
                string ignored;
                if (!ReadFrame(0, out read, out ignored)) return;
                if (read <= 0) return;
            }
        }

        /// <summary>
        /// A no-wait read is for queue draining only. If Windows would block, stop draining
        /// instead of pinning the worker; a queued report on this collection returns at once.
        /// </summary>
        private bool ReadImmediate(out int read)
        {
            read = 0;
            IntPtr threadHandle;
            if (!NativeMethods.DuplicateHandle(NativeMethods.GetCurrentProcess(),
                    NativeMethods.GetCurrentThread(), NativeMethods.GetCurrentProcess(),
                    out threadHandle, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
                return false;
            var watchdog = new Canceller(threadHandle);
            try
            {
                watchdog.Arm(1);
                if (!NativeMethods.ReadFile(_handle, _in, _in.Length, out read, IntPtr.Zero))
                {
                    read = 0;
                    return true;
                }
                return true;
            }
            finally
            {
                watchdog.Dispose();
                NativeMethods.CloseHandle(threadHandle);
            }
        }

        private bool WriteFrame(byte[] frame, out string error)
        {
            error = null;
            Array.Copy(frame, _out, RazerBlackSharkProtocol.PacketSize);
            int written;
            if (!NativeMethods.WriteFile(_handle, _out, _out.Length, out written, IntPtr.Zero))
            {
                error = "写入耳机失败 (Win32 " + Marshal.GetLastWin32Error() + ")";
                return false;
            }
            return true;
        }

        private bool ReadFrame(int timeoutMs, out int read, out string error)
        {
            read = 0;
            error = null;
            if (timeoutMs <= 0)
            {
                return ReadImmediate(out read);
            }

            // The receiver is a synchronous HID collection. A direct blocking ReadFile can
            // wait forever when the headset goes offline, so use the same CancelSynchronousIo
            // watchdog as the mouse transport around each bounded read.
            IntPtr threadHandle;
            if (!NativeMethods.DuplicateHandle(NativeMethods.GetCurrentProcess(),
                    NativeMethods.GetCurrentThread(), NativeMethods.GetCurrentProcess(),
                    out threadHandle, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
            {
                error = "无法建立耳机读取超时上下文";
                return false;
            }
            var watchdog = new Canceller(threadHandle);
            try
            {
                watchdog.Arm(timeoutMs);
                bool ok = NativeMethods.ReadFile(_handle, _in, _in.Length, out read, IntPtr.Zero);
                int err = ok ? 0 : Marshal.GetLastWin32Error();
                watchdog.Disarm();
                if (!ok)
                {
                    if (watchdog.Fired || err == NativeMethods.ERROR_SEM_TIMEOUT
                        || err == NativeMethods.ERROR_OPERATION_ABORTED
                        || err == NativeMethods.ERROR_INVALID_FUNCTION)
                    {
                        read = 0;
                        return true;
                    }
                    error = "读取耳机失败 (Win32 " + err + ")";
                    return false;
                }
                return true;
            }
            finally
            {
                watchdog.Dispose();
                NativeMethods.CloseHandle(threadHandle);
            }
        }

        private sealed class Canceller : IDisposable
        {
            private readonly IntPtr _thread;
            private Timer _timer;
            private int _fired;

            public Canceller(IntPtr thread) { _thread = thread; }
            public bool Fired { get { return Interlocked.CompareExchange(ref _fired, 0, 0) != 0; } }

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
                _handle = null;
            }
        }
    }
}
