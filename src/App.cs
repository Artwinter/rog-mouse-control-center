using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            StartShowWindowWatcher(win);
            app.Run(win);
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

        /// <summary>The product shot ships as its own resource so the art can be swapped without touching layout.</summary>
        private static void ApplyHeroImage(Window win)
        {
            try
            {
                var host = win.FindName("HeroImage") as System.Windows.Controls.Image;
                if (host == null) return;
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui.hero.png");
                if (stream == null) return;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                host.Source = bmp;
            }
            catch { }
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
            ApplyHeroImage(win);
            var controller = new MainController(win);
            controller.Headless = headless;
            controller.NoPoll = NoPoll;
            controller.NoDevice = NoDevice;
            controller.Initialize();
            Controller = controller;
            return win;
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
                    using (var ico = TrayIcon.BuildIcon(levels[i], out handle))
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

        private static void Shot(string path, bool live)
        {
            // Build the visual tree without ever showing a window, then render it.
            var win = LoadWindow(true);
            var content = (FrameworkElement)win.Content;
            if (content == null) throw new InvalidOperationException("窗口没有内容");

            if (live) Controller.RefreshSyncForPreview();

            const int W = 1000, H = 700;
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
    }
}