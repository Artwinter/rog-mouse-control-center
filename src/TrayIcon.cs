using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace RogMouse
{
    /// <summary>
    /// Tray presence built directly on Shell_NotifyIcon instead of WinForms.NotifyIcon.
    /// The WinForms wrapper swallows the shell's return value, so a failed registration
    /// looks exactly like a working one; this version records the outcome and retries
    /// after an explorer restart (the TaskbarCreated broadcast).
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 1;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        private const uint NIF_MESSAGE = 0x01;
        private const uint NIF_ICON = 0x02;
        private const uint NIF_TIP = 0x04;
        private const uint NIF_INFO = 0x10;
        private const uint NIF_SHOWTIP = 0x80;

        private const uint NIM_ADD = 0x00;
        private const uint NIM_MODIFY = 0x01;
        private const uint NIM_DELETE = 0x02;
        private const uint NIM_SETVERSION = 0x04;
        private const uint NOTIFYICON_VERSION_4 = 4;

        private const uint NIIF_INFO = 0x01;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        // Returns S_OK only when the shell really holds the icon; this is the one way to tell a
        // working registration apart from one the shell silently dropped.
        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string name);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int needed);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr text);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LocalFree(IntPtr block);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private readonly Window _win;
        private readonly Action _refresh;
        private readonly Action _quit;
        private readonly WinForms.ContextMenuStrip _menu;
        private readonly WinForms.ToolStripMenuItem _statusItem;
        private readonly uint _taskbarCreated;
        private readonly WinForms.ToolStripMenuItem _autostartItem;

        /// <summary>Raised after the tray autostart item changes, so the in-app switch stays in sync.</summary>
        public event Action AutostartChanged;

        private void RaiseAutostartChanged()
        {
            var handler = AutostartChanged;
            if (handler != null) { try { handler(); } catch { } }
        }

        /// <summary>Repaints the tray checkbox from the registry without triggering a write.</summary>
        public void SyncAutostartItem()
        {
            if (_autostartItem != null) _autostartItem.Checked = AutoStart.IsEnabled();
        }

        private IntPtr _hwnd;
        private HwndSource _source;
        private IntPtr _iconHandle;
        private bool _registered;
        private bool _tipShown;
        private bool _disposed;

        // The shell can refuse the very first NIM_ADD right after logon (explorer still
        // building its tray), so a refused add is retried a few times before giving up.
        private const int MaxAddAttempts = 6;
        private System.Windows.Threading.DispatcherTimer _retry;
        private int _attempts;

        public TrayIcon(Window win, Action refresh, Action quit)
        {
            _win = win;
            _refresh = refresh;
            _quit = quit;

            _menu = new WinForms.ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.BackColor = Color.FromArgb(24, 24, 30);
            _menu.ForeColor = Color.FromArgb(242, 242, 245);

            _statusItem = new WinForms.ToolStripMenuItem("正在读取电量…");
            _statusItem.Enabled = false;
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new WinForms.ToolStripSeparator());

            var show = new WinForms.ToolStripMenuItem("显示主窗口");
            show.Click += delegate { ShowWindow(); };
            _menu.Items.Add(show);

            var reread = new WinForms.ToolStripMenuItem("重新读取");
            reread.Click += delegate { if (_refresh != null) _refresh(); };
            _menu.Items.Add(reread);

            // Launch-at-logon mirrors the in-app switch; both call the same AutoStart.Set.
            _autostartItem = new WinForms.ToolStripMenuItem("开机自启动");
            _autostartItem.CheckOnClick = true;
            _autostartItem.Checked = AutoStart.IsEnabled();
            _autostartItem.Click += delegate
            {
                string error = AutoStart.Set(_autostartItem.Checked);
                if (error != null)
                {
                    // Roll the checkmark back so it never claims a state the registry rejects.
                    _autostartItem.Checked = false;
                    Log("autostart failed: " + error);
                }
                RaiseAutostartChanged();
            };
            _menu.Items.Add(_autostartItem);

            _menu.Items.Add(new WinForms.ToolStripSeparator());
            var quitItem = new WinForms.ToolStripMenuItem("退出");
            quitItem.Click += delegate { if (_quit != null) _quit(); };
            _menu.Items.Add(quitItem);

            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            Log("TrayIcon created, taskbarCreated=" + _taskbarCreated);

            // The window handle only exists once the window is realized, so the icon is
            // registered from SourceInitialized rather than from the constructor's caller.
            var helper = new WindowInteropHelper(win);
            _hwnd = helper.Handle;
            if (_hwnd != IntPtr.Zero) Attach();
            else win.SourceInitialized += delegate
            {
                _hwnd = new WindowInteropHelper(_win).Handle;
                Attach();
            };
        }

        private void Attach()
        {
            Log("Attach: hwnd=0x" + _hwnd.ToInt64().ToString("X") + " pid=" + System.Diagnostics.Process.GetCurrentProcess().Id
                + " integrity=" + DescribeIntegrity());
            if (_hwnd == IntPtr.Zero) return;
            _source = HwndSource.FromHwnd(_hwnd);
            if (_source != null) _source.AddHook(WndProc);

            IntPtr handle;
            Icon initial = BuildIcon(-1, out handle);
            _iconHandle = handle;
            if (!AddIcon(handle, "ROG 月刃 控制中心")) StartRetry();
        }

        /// <summary>Retries a refused registration; raises <see cref="RegisteredChanged"/> when it settles.</summary>
        private void StartRetry()
        {
            if (_retry != null || _disposed) return;
            _retry = new System.Windows.Threading.DispatcherTimer();
            _retry.Interval = TimeSpan.FromSeconds(1.5);
            _retry.Tick += delegate
            {
                _attempts++;
                if (AddIcon(_iconHandle, _lastTip))
                {
                    StopRetry();
                    RaiseRegisteredChanged();
                    return;
                }
                if (_attempts >= MaxAddAttempts)
                {
                    Log("giving up after " + _attempts + " attempts, win32=" + LastError);
                    StopRetry();
                    RaiseRegisteredChanged();
                }
            };
            _retry.Start();
        }

        private void StopRetry()
        {
            if (_retry == null) return;
            _retry.Stop();
            _retry = null;
        }

        private void RaiseRegisteredChanged()
        {
            var handler = RegisteredChanged;
            if (handler != null) { try { handler(); } catch { } }
        }

        /// <summary>
        /// Registers (or re-registers) the icon with the shell and logs every step.
        /// NIF_SHOWTIP only means something once the icon speaks NOTIFYICON_VERSION_4, so it
        /// is applied in a follow-up NIM_MODIFY rather than bundled into NIM_ADD.
        /// </summary>
        private bool AddIcon(IntPtr hicon, string tip)
        {
            if (_hwnd == IntPtr.Zero) { Log("AddIcon skipped: hwnd is null"); return false; }
            Attempted = true;

            var data = NewData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = WM_TRAYICON;
            data.hIcon = hicon;
            data.szTip = Truncate(tip);

            _registered = Shell_NotifyIcon(NIM_ADD, ref data);
            if (!_registered)
            {
                int err = Marshal.GetLastWin32Error();
                LastError = err;
                Log("NIM_ADD failed, win32=" + err + " (" + DescribeError(err) + ") integrity=" + DescribeIntegrity());
                return false;
            }

            var version = NewData();
            version.uVersion = NOTIFYICON_VERSION_4;
            if (!Shell_NotifyIcon(NIM_SETVERSION, ref version))
                Log("NIM_SETVERSION failed, win32=" + Marshal.GetLastWin32Error());

            // Tooltip on hover needs the V4 struct; a failure here is cosmetic only.
            var showTip = NewData();
            showTip.uFlags = NIF_ICON | NIF_TIP | NIF_SHOWTIP;
            showTip.hIcon = hicon;
            showTip.szTip = Truncate(tip);
            if (!Shell_NotifyIcon(NIM_MODIFY, ref showTip))
                Log("NIM_MODIFY(SHOWTIP) failed, win32=" + Marshal.GetLastWin32Error());

            Log("NIM_ADD ok");
            LogRect("after add");
            return true;
        }

        /// <summary>
        /// Asks the shell where our icon ended up. A successful call proves the icon exists;
        /// the rectangle tells whether it sits on the taskbar or inside the overflow flyout.
        /// </summary>
        public bool TryGetRect(out System.Drawing.Rectangle rect)
        {
            rect = System.Drawing.Rectangle.Empty;
            if (_hwnd == IntPtr.Zero) return false;
            try
            {
                var id = new NOTIFYICONIDENTIFIER();
                id.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                id.hWnd = _hwnd;
                id.uID = 1;
                RECT r;
                if (Shell_NotifyIconGetRect(ref id, out r) != 0) return false;
                rect = System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                return true;
            }
            catch { return false; }
        }

        private void LogRect(string when)
        {
            System.Drawing.Rectangle r;
            if (TryGetRect(out r))
                Log("rect " + when + ": " + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
            else
                Log("rect " + when + ": shell does not know the icon");
        }

        /// <summary>
        /// Names this process's integrity level. The shell refuses NIM_ADD with
        /// ERROR_ACCESS_DENIED when the caller runs below the taskbar's level, and the only
        /// visible symptom is a missing icon, so the level has to be recorded to tell that
        /// case apart from Windows simply parking the icon in the overflow flyout.
        /// </summary>
        public static string DescribeIntegrity()
        {
            const uint TOKEN_QUERY = 0x0008;
            const int TokenIntegrityLevel = 25;
            const int TokenElevation = 20;

            IntPtr token = IntPtr.Zero;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token))
                return "unknown (OpenProcessToken win32=" + Marshal.GetLastWin32Error() + ")";

            IntPtr buf = IntPtr.Zero;
            try
            {
                // TOKEN_MANDATORY_LABEL is 8 bytes on x86 and 16 on x64; the SID it points at
                // lives in the same buffer, so the whole thing is read in one call.
                int size = IntPtr.Size * 2 + 0x40;
                buf = Marshal.AllocHGlobal(size);
                int needed;
                if (!GetTokenInformation(token, TokenIntegrityLevel, buf, size, out needed))
                    return "unknown (GetTokenInformation win32=" + Marshal.GetLastWin32Error() + ")";

                IntPtr sid = Marshal.ReadIntPtr(buf, 0);
                if (sid == IntPtr.Zero) return "unknown (null sid)";

                int count = Marshal.ReadByte(sid, 1);
                if (count <= 0) return "unknown (no subauthority)";
                int rid = Marshal.ReadInt32(sid, 8 + 4 * (count - 1));

                string name;
                if (rid == 0x0000) name = "Untrusted";
                else if (rid == 0x1000) name = "Low";
                else if (rid == 0x2000) name = "Medium";
                else if (rid == 0x3000) name = "High";
                else if (rid == 0x4000) name = "System";
                else name = "Other";

                string elevated = "";
                if (GetTokenInformation(token, TokenElevation, buf, size, out needed))
                {
                    int isElevated = Marshal.ReadInt32(buf, 0);
                    elevated = isElevated != 0 ? ", elevated" : "";
                }
                return name + " (0x" + rid.ToString("X") + elevated + ")";
            }
            catch (Exception ex) { return "unknown (" + ex.Message + ")"; }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                CloseHandle(token);
            }
        }

        /// <summary>
        /// True when this process runs below Medium integrity. A Low/Untrusted process may not
        /// add tray icons: Shell_NotifyIcon fails with ERROR_ACCESS_DENIED and the only symptom
        /// is a missing icon, so callers use this to explain the failure instead of retrying.
        /// </summary>
        public static bool IsBelowMedium()
        {
            string level = DescribeIntegrity();
            return level.StartsWith("Low", StringComparison.Ordinal)
                || level.StartsWith("Untrusted", StringComparison.Ordinal);
        }

        /// <summary>Turns the shell's error code into something readable in tray.log.</summary>
        private static string DescribeError(int code)
        {
            if (code == 5) return "ERROR_ACCESS_DENIED: 进程完整性级别低于任务栏，被 UIPI 拦截";
            if (code == 87) return "ERROR_INVALID_PARAMETER: 结构体或标志不被该 shell 接受";
            if (code == 0) return "shell 返回 false 但未设置错误码";
            return "win32 " + code;
        }

        private NOTIFYICONDATA NewData()
        {
            var data = new NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            data.hWnd = _hwnd;
            data.uID = 1;
            data.szTip = "";
            data.szInfo = "";
            data.szInfoTitle = "";
            return data;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
            {
                // Explorer restarted: every icon in the tray is gone, ours included.
                Log("TaskbarCreated -> re-adding icon");
                AddIcon(_iconHandle, _lastTip);
                return IntPtr.Zero;
            }

            if (msg != WM_TRAYICON) return IntPtr.Zero;

            // NOTIFYICON_VERSION_4: lParam's low word carries the mouse message.
            int mouse = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouse == WM_LBUTTONUP || mouse == WM_LBUTTONDBLCLK)
            {
                ShowWindow();
                handled = true;
            }
            else if (mouse == WM_RBUTTONUP || mouse == WM_CONTEXTMENU)
            {
                ShowMenu();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ShowMenu()
        {
            try
            {
                // Without foreground the menu stays on screen after a click elsewhere.
                SetForegroundWindow(_hwnd);
                POINT p;
                if (GetCursorPos(out p)) _menu.Show(new System.Drawing.Point(p.X, p.Y));
                else _menu.Show(WinForms.Cursor.Position);
            }
            catch (Exception ex) { Log("menu failed: " + ex.Message); }
        }

        /// <summary>Mirrors the window's own Hide so both share one entry point.</summary>
        private void ShowWindow()
        {
            Log("window show requested: tray click");
            _win.Show();
            if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
            _win.Activate();
            _win.Topmost = true;
            _win.Topmost = false;
        }

        /// <summary>
        /// False when the shell refused NIM_ADD. The window must then stay reachable, because
        /// hiding it with no tray icon would leave the app running with no way back.
        /// </summary>
        public bool IsRegistered { get { return _registered; } }

        /// <summary>True once the shell has actually been asked to take the icon.</summary>
        public bool Attempted { get; private set; }

        /// <summary>Win32 error from the last failed Shell_NotifyIcon call, 0 when it worked.</summary>
        public int LastError { get; private set; }

        /// <summary>Raised once the registration either succeeded after retrying or was given up.</summary>
        public event Action RegisteredChanged;

        private string _lastTip = "";
        private int _lastPercent = int.MinValue;
        private string _lastTransport;

        public void Update(int percent, string transport)
        {
            if (_disposed) return;
            // Unchanged readings skip the icon rebuild and the NIM_MODIFY round trip:
            // the poll fires even when nothing moved, and every rebuild is GDI churn.
            if (percent == _lastPercent && string.Equals(transport, _lastTransport, StringComparison.Ordinal)) return;
            _lastPercent = percent;
            _lastTransport = transport;

            string body = percent >= 0 ? "电量 " + percent + "%" : "电量未知";
            string tip = "ROG 月刃 · " + body;
            if (!string.IsNullOrEmpty(transport) && percent >= 0) tip = "ROG 月刃 · " + transport + " · " + percent + "%";
            tip = Truncate(tip);
            _lastTip = tip;

            if (_statusItem != null)
                _statusItem.Text = percent >= 0 ? (body + "  " + (transport == null ? "" : transport)) : "电量未知";

            IntPtr handle;
            Icon next = BuildIcon(percent, out handle);
            SetIcon(next, handle, tip);
        }

        /// <summary>One-time balloon after the user closes the window the first time.</summary>
        public void NotifyHidden()
        {
            if (_disposed || _tipShown || !_registered) return;
            _tipShown = true;
            try
            {
                var data = NewData();
                data.uFlags = NIF_INFO;
                data.szInfoTitle = "仍在后台运行";
                data.szInfo = "控制中心已最小化到托盘，双击图标可重新打开，右键可退出。";
                data.dwInfoFlags = NIIF_INFO;
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
            catch { }
        }

        /// <summary>
        /// Installs a freshly built icon. Icon.FromHandle does not own the HICON, so the
        /// previous handle has to be destroyed by hand or the process leaks GDI objects.
        /// </summary>
        private void SetIcon(Icon next, IntPtr nextHandle, string tip)
        {
            if (_registered && _hwnd != IntPtr.Zero)
            {
                var data = NewData();
                data.uFlags = NIF_ICON | NIF_TIP | NIF_SHOWTIP;
                data.hIcon = nextHandle;
                data.szTip = tip;
                bool ok = Shell_NotifyIcon(NIM_MODIFY, ref data);
                if (!ok) Log("NIM_MODIFY failed, win32=" + Marshal.GetLastWin32Error());
                else Log("NIM_MODIFY ok: tip=" + tip);
            }
            else
            {
                Log("SetIcon skipped: registered=" + _registered + " hwnd=" + (_hwnd == IntPtr.Zero ? "null" : "ok"));
            }

            IntPtr previous = _iconHandle;
            _iconHandle = nextHandle;
            if (next != null) { try { next.Dispose(); } catch { } }
            if (previous != IntPtr.Zero && previous != nextHandle) DestroyIcon(previous);
        }

        /// <summary>
        /// Draws the battery ring on the opaque dark tile Windows expects.
        /// The taskbar bitmap is composited without per-pixel alpha, so a transparent
        /// icon renders as a hollow outline; the tile makes the glyph readable.
        /// </summary>
        public static Icon BuildIcon(int percent, out IntPtr handle)
        {
            var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var tile = new SolidBrush(Color.FromArgb(255, 22, 22, 28)))
                {
                    g.FillEllipse(tile, new RectangleF(0.5f, 0.5f, 31f, 31f));
                }

                var rect = new RectangleF(6.5f, 6.5f, 19f, 19f);
                using (var track = new Pen(Color.FromArgb(90, 255, 255, 255), 3f))
                    g.DrawEllipse(track, rect);

                if (percent >= 0)
                {
                    float sweep = Math.Min(percent, 100) * 3.6f;
                    if (sweep > 0.1f)
                    {
                        // A 100% ring is drawn just short of a full circle: DrawArc with a
                        // 360 degree sweep paints nothing on some GDI+ versions.
                        if (sweep > 350f) sweep = 350f;
                        Color c;
                        if (percent >= 50) c = Color.FromArgb(61, 220, 132);
                        else if (percent >= 20) c = Color.FromArgb(255, 194, 77);
                        else c = Color.FromArgb(255, 77, 77);

                        using (var pen = new Pen(c, 3f))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawArc(pen, rect, -90f, sweep);
                        }
                    }
                }
            }

            handle = bmp.GetHicon();
            bmp.Dispose();
            return Icon.FromHandle(handle);
        }

        /// <summary>Frees a handle handed out by BuildIcon once its Icon is disposed.</summary>
        public static void ReleaseIconHandle(IntPtr handle)
        {
            if (handle != IntPtr.Zero) DestroyIcon(handle);
        }

        private static string Truncate(string s)
        {
            if (s == null) return "";
            // NotifyIcon tips are capped at 128 characters in the V4 struct.
            return s.Length <= 127 ? s : s.Substring(0, 124) + "…";
        }

        /// <summary>
        /// Diagnostics go under %LOCALAPPDATA% first. Writing next to the exe used to be the
        /// default, but the shipped copy lives in a read-only-ish folder and the log grew in
        /// the deliverable, so the app folder is only a fallback now. A silently swallowed
        /// write makes a tray failure look exactly like a success, so every step is tried.
        /// </summary>
        internal static void Log(string s)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine;
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(root))
                {
                    string dir = Path.Combine(root, "ROGMouse");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "tray.log"), line);
                    return;
                }
            }
            catch { }
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(dir))
                {
                    File.AppendAllText(Path.Combine(dir, "tray.log"), line);
                    return;
                }
            }
            catch { }
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "rogmouse-tray.log"), line); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_registered && _hwnd != IntPtr.Zero)
            {
                var data = NewData();
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _registered = false;
            }

            StopRetry();

            if (_source != null)
            {
                try { _source.RemoveHook(WndProc); } catch { }
                _source = null;
            }
            if (_menu != null) { try { _menu.Dispose(); } catch { } }

            if (_iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }
    }
}
