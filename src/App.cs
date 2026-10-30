using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: AssemblyTitle("Peripheral Instrument")]
[assembly: AssemblyProduct("外设仪表台")]
[assembly: AssemblyDescription("Local peripheral control and telemetry")]
[assembly: AssemblyVersion("1.6.1.0")]
[assembly: AssemblyFileVersion("1.6.1.0")]
[assembly: AssemblyInformationalVersion("1.6.1")]

namespace RogMouse
{
    public static class App
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Dev helper: render the window offscreen to a PNG and exit.
            if (args.Length >= 2 && args[0] == "--shot")
            {
                Shot(args[1], false);
                return;
            }
            if (args.Length >= 2 && args[0] == "--shot-live")
            {
                Shot(args[1], true);
                return;
            }
            if (args.Length >= 2 && args[0] == "--shot-headset")
            {
                Shot(args[1], true, true);
                return;
            }
            // Dev helper: like --shot-live/--shot-headset but with synthetic values when the
            // hardware is asleep, so the populated layout can still be reviewed. Pass "headset"
            // as a third argument for the headset page. Never reachable from the windowed app.
            if (args.Length >= 2 && args[0] == "--shot-demo")
            {
                Demo = true;
                bool hs = args.Length >= 3 && args[2] == "headset";
                // Optional "WxH" (third or fourth token) renders the clamped minimum size so the
                // tight-layout review covers the 1040x680 case, not only the 1200x780 design.
                for (int i = 2; i < args.Length; i++)
                {
                    int xi = args[i].IndexOf('x');
                    int w, h;
                    if (xi > 0 && int.TryParse(args[i].Substring(0, xi), out w)
                        && int.TryParse(args[i].Substring(xi + 1), out h))
                    {
                        DemoWidth = w;
                        DemoHeight = h;
                    }
                }
                Shot(args[1], true, hs);
                return;
            }
            // Dev helper: export the tray icons themselves, scaled up for inspection.
            if (args.Length >= 2 && args[0] == "--icon")
            {
                IconStrip(args[1]);
                return;
            }

            // Diagnostic switch: run everything except the background poll, so a
            // stutter report can be bisected between "handle + UI + tray" and
            // "the periodic device traffic".
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--no-poll") NoPoll = true;
                if (args[i] == "--no-device") NoDevice = true;
                if (args[i] == "--startup") Startup = true;
            }

            // Single instance: a second launch must not fork another poller onto the
            // pointer's vendor HID link (doubled device traffic is exactly the stutter
            // class v1.2 removed). The new process signals the running one to show its
            // window and exits immediately.
            if (!AcquireSingleInstance())
            {
                SignalExistingInstance();
                return;
            }

            var app = new Application();
            // The close button parks the window in the tray instead of ending the process,
            // so shutdown has to be explicit: only the tray menu's Quit reaches it.
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Window win;
            try
            {
                win = LoadWindow();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "界面加载失败");
                return;
            }

            app.MainWindow = win;
            if (Startup)
            {
                // Create the HWND (which registers the tray icon) without ever showing the
                // layered window. Showing and hiding it would allocate a full-screen render
                // surface just to park the app.
                win.ShowInTaskbar = false;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.Left = -32000;
                win.Top = -32000;
                new System.Windows.Interop.WindowInteropHelper(win).EnsureHandle();
                win.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    // HideForStartup also performs the first device read. SourceInitialized
                    // has already registered the tray icon by this point; Loaded will not fire
                    // for this never-shown window.
                    if (Controller != null) Controller.HideForStartup();
                }));
            }
            StartShowWindowWatcher(win);
            // Run(Window) calls Show() itself. The startup path owns a hidden HWND and
            // needs the dispatcher loop without changing that visibility.
            if (Startup) app.Run();
            else app.Run(win);
        }

        private static System.Threading.Mutex _instanceMutex;
        private static System.Threading.EventWaitHandle _showEvent;

        /// <summary>
        /// True when this process owns the single-instance slot. An abandoned mutex (the
        /// previous instance was killed) still means the slot is free, so that exception
        /// counts as acquired.
        /// </summary>
        private static bool AcquireSingleInstance()
        {
            if (NoPoll && NoDevice) return true; // diagnostic runs are exempt
            _instanceMutex = new System.Threading.Mutex(false, "Local\\RogMouseSingleInstance");
            bool acquired;
            try { acquired = _instanceMutex.WaitOne(0, false); }
            catch (System.Threading.AbandonedMutexException) { acquired = true; }
            if (!acquired) return false;
            try
            {
                _showEvent = new System.Threading.EventWaitHandle(false,
                    System.Threading.EventResetMode.AutoReset, "Local\\RogMouseShowWindow");
            }
            catch { }
            return true;
        }

        /// <summary>Asks the already-running instance to surface its window.</summary>
        private static void SignalExistingInstance()
        {
            try
            {
                using (var ev = System.Threading.EventWaitHandle.OpenExisting("Local\\RogMouseShowWindow"))
                    ev.Set();
            }
            catch { }
        }

        /// <summary>
        /// Wakes the window whenever a second launch signals the event. Background thread,
        /// dispatcher hop for the UI work; the loop dies with the process at shutdown.
        /// </summary>
        private static void StartShowWindowWatcher(Window win)
        {
            if (_showEvent == null) return;
            var showEvent = _showEvent;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                while (showEvent.WaitOne())
                {
                    try
                    {
                        win.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (Controller != null) Controller.ActivateWindow();
                            else
                            {
                                win.Show();
                                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                                win.Activate();
                            }
                        }));
                    }
                    catch { }
                }
            });
        }

        public static MainController Controller;

        /// <summary>Set by --no-poll: keeps the tray and the UI, drops the periodic refresh.</summary>
        public static bool NoPoll;

        /// <summary>
        /// Set by --no-device: never opens the vendor HID collection at all. Together with
        /// --no-poll this splits a stutter report into three cases: no device handle, handle
        /// but no traffic, and the normal build.
        /// </summary>
        public static bool NoDevice;

        /// <summary>Set by --startup: create the window and tray, but keep the window hidden.</summary>
        public static bool Startup;

        /// <summary>Set by --shot-demo: the two screenshot hooks may substitute synthetic values.</summary>
        public static bool Demo;

        /// <summary>Optional forced canvas size for --shot-demo; zero keeps the fitted window size.</summary>
        public static int DemoWidth;
        public static int DemoHeight;

        /// <summary>窗口/任务栏图标直接复用 exe 自带的 win32 图标，不再额外内嵌一份。</summary>
        private static ImageSource LoadAppIcon()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                {
                    if (ico == null) return null;
                    return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { return null; }
        }

        public static Window LoadWindow()
        {
            return LoadWindow(false);
        }

        public static Window LoadWindow(bool headless)
        {
            var asm = Assembly.GetExecutingAssembly();
            var stream = asm.GetManifestResourceStream("ui.MainWindow.xaml");
            if (stream == null) throw new InvalidOperationException("找不到内嵌资源 ui.MainWindow.xaml");
            string xaml;
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                xaml = reader.ReadToEnd();

            var win = (Window)XamlReader.Parse(xaml);
            win.Icon = LoadAppIcon();
            FitWindowToWorkArea(win);
            var controller = new MainController(win);
            controller.Headless = headless;
            controller.NoPoll = NoPoll;
            controller.NoDevice = NoDevice;
            controller.Initialize();
            Controller = controller;
            return win;
        }

        /// <summary>
        /// Keeps the 1120x760 design inside the current desktop. A 1366x768 work area is
        /// shorter than the design once the taskbar is visible, so fit before first show.
        /// </summary>
        public static void FitWindowToWorkArea(Window win)
        {
            double w, h;
            WindowFit.Calculate(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height,
                out w, out h);
            win.Width = w;
            win.Height = h;
        }

        /// <summary>Renders the tray icon at a few charge levels into one strip.</summary>
        private static void IconStrip(string path)
        {
            int[] levels = { -1, 98, 62, 34, 12 };
            const int cell = 128, pad = 24;
            using (var bmp = new System.Drawing.Bitmap(levels.Length * (cell + pad) + pad, cell + pad * 2))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(20, 20, 26));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                for (int i = 0; i < levels.Length; i++)
                {
                    IntPtr handle;
                    using (var ico = TrayIcon.BuildIcon(levels[i], levels[(i + 2) % levels.Length], out handle))
                    using (var src = ico.ToBitmap())
                    {
                        g.DrawImage(src, pad + i * (cell + pad), pad, cell, cell);
                        TrayIcon.ReleaseIconHandle(handle);
                    }
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("saved " + path);
        }

        private static void Shot(string path, bool live, bool headset)
        {
            // Build the visual tree without ever showing a window, then render it.
            var win = LoadWindow(true);
            var content = (FrameworkElement)win.Content;
            if (content == null) throw new InvalidOperationException("窗口没有内容");

            if (live)
            {
                if (headset) Controller.ShowHeadsetPageForPreview();
                else Controller.RefreshSyncForPreview();
            }

            int W = (int)Math.Round(win.Width);
            int H = (int)Math.Round(win.Height);
            // --shot-demo may force the clamped minimum so the tight layout gets reviewed too.
            if (DemoWidth > 0) { W = DemoWidth; win.Width = W; }
            if (DemoHeight > 0) { H = DemoHeight; win.Height = H; }
            content.Width = W;
            content.Height = H;
            content.Measure(new Size(W, H));
            content.Arrange(new Rect(0, 0, W, H));
            content.UpdateLayout();

            var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) encoder.Save(fs);
            Console.WriteLine("saved " + path);
        }

        private static void Shot(string path, bool live)
        {
            Shot(path, live, false);
        }

    }
}
