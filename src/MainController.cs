using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RogMouse
{
    /// <summary>
    /// Wires the XAML window to the HID protocol layer. All device access happens on a
    /// background thread; the UI thread only applies the results.
    /// </summary>
    public sealed class MainController
    {
        private readonly Window _win;
        private AsusProtocol _api;
        private MouseState _state = new MouseState();

        private int _selectedPreset;
        private bool _suppressSlider;

        private Slider _slider;
        private TextBlock _dpiValue;
        private TextBlock _batteryValue;
        private TextBlock _batteryHint;
        private Border _batteryFill;
        private Panel _stagePanel;
        private Panel _ratePanel;
        private Panel _debouncePanel;
        private ToggleButton _snapToggle;
        private ToggleButton _autostartToggle;
        private TextBlock _footStatus;
        private Border _statusDot;
        private TextBlock _connText;
        private TextBlock _deviceName;
        private TextBlock _headerDevice;
        private TextBlock _headerLink;
        private TextBlock _headerEyebrow;
        private TextBlock _tabMouseStatus;
        private TextBlock _tabHeadsetStatus;
        private TextBlock _chipMouse;
        private TextBlock _chipHeadset;
        private Border _chipMouseDot;
        private Border _chipHeadsetDot;
        /// <summary>Last mouse transaction result, used by the small status tags.</summary>
        private bool _mouseOnline;
        /// <summary>True once a transaction or an enumeration has actually been attempted.</summary>
        private bool _mouseProbed;
        private TextBlock _infoFirmware;
        private TextBlock _specPreset, _specRate, _specDebounce, _specConn, _specCharge;

        private TextBlock _saveHint;
        private System.Windows.Threading.DispatcherTimer _autoSave;

        private TrayIcon _tray;
        private HeadsetController _headset;
        private HeadsetService _headsetService;
        private bool _trayFailed;
        private ConnectionInfo _conn;
        private System.Windows.Threading.DispatcherTimer _poll;
        private System.Windows.Threading.DispatcherTimer _watch;
        private System.Windows.Threading.DispatcherTimer _trayPoll;
        private System.Windows.Threading.DispatcherTimer _headsetTrayPoll;
        private int _quietTicks;
        private bool _allowClose;
        private bool _hiddenNotice;

        /// <summary>Set before Initialize() by the offscreen renderer: no tray, no timer.</summary>
        public bool Headless;

        /// <summary>Set by --no-poll: keep the tray and the window, but never touch the device again after the first read.</summary>
        public bool NoPoll;

        /// <summary>Set by --no-device: leave the vendor collection unopened for the whole run.</summary>
        public bool NoDevice;

        public MainController(Window win) { _win = win; }

        public void Initialize()
        {
            _slider = (Slider)_win.FindName("DpiSlider");
            _dpiValue = (TextBlock)_win.FindName("DpiValue");
            _batteryValue = (TextBlock)_win.FindName("BatteryValue");
            _batteryHint = (TextBlock)_win.FindName("BatteryHint");
            _batteryFill = (Border)_win.FindName("BatteryFill");
            _stagePanel = (Panel)_win.FindName("StagePanel");
            _ratePanel = (Panel)_win.FindName("RatePanel");
            _debouncePanel = (Panel)_win.FindName("DebouncePanel");
            _infoFirmware = (TextBlock)_win.FindName("InfoFirmware");
            _specPreset = (TextBlock)_win.FindName("SpecPreset");
            _specRate = (TextBlock)_win.FindName("SpecRate");
            _specDebounce = (TextBlock)_win.FindName("SpecDebounce");
            _specConn = (TextBlock)_win.FindName("SpecConn");
            _specCharge = (TextBlock)_win.FindName("SpecCharge");
            _saveHint = (TextBlock)_win.FindName("SaveHint");
            _snapToggle = (ToggleButton)_win.FindName("SnapToggle");
            _autostartToggle = (ToggleButton)_win.FindName("AutostartToggle");
            _footStatus = (TextBlock)_win.FindName("FootStatus");
            _statusDot = (Border)_win.FindName("StatusDot");
            _connText = (TextBlock)_win.FindName("ConnText");
            _deviceName = (TextBlock)_win.FindName("DeviceName");
            _headerDevice = (TextBlock)_win.FindName("HeaderDevice");
            _headerLink = (TextBlock)_win.FindName("HeaderLink");
            _headerEyebrow = (TextBlock)_win.FindName("HeaderEyebrow");
            _tabMouseStatus = (TextBlock)_win.FindName("TabMouseStatus");
            _tabHeadsetStatus = (TextBlock)_win.FindName("TabHeadsetStatus");
            _chipMouse = (TextBlock)_win.FindName("ChipMouse");
            _chipHeadset = (TextBlock)_win.FindName("ChipHeadset");
            _chipMouseDot = (Border)_win.FindName("ChipMouseDot");
            _chipHeadsetDot = (Border)_win.FindName("ChipHeadsetDot");

            // Every edit now applies immediately, so persisting to firmware is debounced:
            // a drag would otherwise fire a save per 50 DPI step.
            _autoSave = new System.Windows.Threading.DispatcherTimer();
            _autoSave.Interval = TimeSpan.FromMilliseconds(1500);
            _autoSave.Tick += delegate { OnAutoSaveTick(); };

            HookChrome();
            HookDeviceTabs();
            HookDpi();
            HookExtras();
            BuildChips();

            _slider.Value = 800;
            ShowDpiHero("--");

            HookAutostart();
            DrawBattery(-1);
            // Paint the idle wording before the first transaction so nothing on screen claims a
            // transport or a link state that has not been measured yet.
            PaintDeviceTags();

            _win.Loaded += delegate { BeginRefresh(); };
            _win.Loaded += delegate { _headset.ReadOnceForTrayStartup(); };
            _win.Loaded += delegate { UpdateTrayFromHeadset(); };
            _headsetService = HeadsetService.CreateDefault();
            _headsetService.Log = TrayIcon.Log;
            _headset = new HeadsetController(_win, _headsetService, NoDevice, NoPoll);
            _headset.ReadingChanged = delegate(HeadsetReading reading) { PushTray(); };
            // The rail row has to follow every repaint, including the ones that keep the last
            // reading when the page is not selected.
            _headset.Rendered = delegate { PaintHeadsetTags(); };
            _headset.Initialize();

            // The offscreen renderer builds this same tree just to grab a PNG; a tray icon
            // and a polling timer would outlive that process for no reason.
            if (Headless) return;

            _tray = new TrayIcon(_win, RefreshBothFromTray, Quit, ShowDeviceFromTray);
            _tray.RegisteredChanged += delegate { ApplyTrayStatus(); };
            // Tray checkbox changed -> repaint the in-app switch from the registry.
            _tray.AutostartChanged += delegate
            {
                if (_autostartToggle != null) _autostartToggle.IsChecked = AutoStart.IsEnabled();
            };
            // The first attempt may already have succeeded during construction, before the
            // event had a subscriber, so the state is sampled again once the window is real.
            _win.Loaded += delegate { ApplyTrayStatus(); };
            _win.Closing += OnClosing;

            // The window is hidden on purpose (close button, Esc, tray) or minimized to the
            // taskbar. Either way nobody can see a reading, so the app stops touching the
            // device completely: no timer, and the vendor handle is released. The same
            // suspension is applied while a fullscreen app owns the screen (see ShouldSuspend),
            // which is exactly the case the reported stutter described.
            _win.IsVisibleChanged += delegate { SyncDeviceAccess(); };
            _win.StateChanged += delegate { SyncDeviceAccess(); };

            // A fullscreen game does not raise any event this window could listen to, so the
            // state is re-checked on a slow timer. The check is one GetForegroundWindow plus
            // a rectangle and a style read: it never touches the device, which is what makes
            // it safe to run even while the device is suspended.
            _watch = new System.Windows.Threading.DispatcherTimer();
            _watch.Interval = TimeSpan.FromSeconds(3);
            _watch.Tick += delegate { SyncDeviceAccess(); };
            _watch.Start();

            // Keeps the tray percentage honest while the window sits in the tray, and
            // doubles as the retry path after the mouse wakes up from sleep.
            // --no-poll skips this entirely so a stutter report can be bisected between
            // "open handle + UI + tray" and "the periodic device traffic".
            if (!NoPoll)
            {
                _poll = new System.Windows.Threading.DispatcherTimer();
                _poll.Interval = TimeSpan.FromSeconds(15);
                _poll.Tick += delegate { RefreshQuiet(); };
                _poll.Start();

                // Tray keep-alive: while the window is parked the main poll is silent
                // (ShouldSuspend), so the tray percentage would freeze at the pre-hide
                // reading. This slow poll refreshes it every 5 minutes: fullscreen check
                // first, one battery command, the handle released immediately — a
                // fullscreen game never sees device traffic from it.
                _trayPoll = new System.Windows.Threading.DispatcherTimer();
                _trayPoll.Interval = TimeSpan.FromSeconds(300);
                _trayPoll.Tick += delegate { TrayPollTick(); };
                _trayPoll.Start();

                // The headset has its own slow keep-alive so the tray still has a fresh
                // value after the window is parked. Fullscreen and --no-* modes skip it.
                _headsetTrayPoll = new System.Windows.Threading.DispatcherTimer();
                _headsetTrayPoll.Interval = TimeSpan.FromSeconds(300);
                _headsetTrayPoll.Tick += delegate { HeadsetTrayTick(); };
                _headsetTrayPoll.Start();
            }
        }

        private void HookChrome()
        {
            var titleBar = (FrameworkElement)_win.FindName("TitleBar");
            var btnMin = (Button)_win.FindName("BtnMin");
            var btnClose = (Button)_win.FindName("BtnClose");

            if (titleBar != null)
            {
                titleBar.MouseLeftButtonDown += delegate
                {
                    try { _win.DragMove(); }
                    catch { }
                };
            }
            if (btnMin != null) btnMin.Click += delegate { _win.WindowState = WindowState.Minimized; };
            // The close button parks the app in the tray; only the tray menu really quits.
            if (btnClose != null) btnClose.Click += delegate { HideToTray(); };

            var btnRefresh = (Button)_win.FindName("BtnRefresh");
            if (btnRefresh != null) btnRefresh.Click += delegate { BeginRefresh(); };

            var btnTrayDiag = (Button)_win.FindName("BtnTrayDiag");
            if (btnTrayDiag != null) btnTrayDiag.Click += delegate { ShowTrayDiagnostics(); };
        }

        private void HookDeviceTabs()
        {
            var tabMouse = (Button)_win.FindName("TabMouse");
            var tabHeadset = (Button)_win.FindName("TabHeadset");
            var mousePage = (Grid)_win.FindName("MousePage");
            var headsetPage = (Border)_win.FindName("HeadsetPage");
            if (tabMouse == null || tabHeadset == null || mousePage == null || headsetPage == null) return;

            tabMouse.Click += delegate
            {
                SwitchDevice(false, true);
            };
            tabHeadset.Click += delegate
            {
                SwitchDevice(true, true);
            };
        }

        private void SwitchDevice(bool headset, bool startRead)
        {
            var tabMouse = (Button)_win.FindName("TabMouse");
            var tabHeadset = (Button)_win.FindName("TabHeadset");
            var mousePage = (Grid)_win.FindName("MousePage");
            var headsetPage = (Border)_win.FindName("HeadsetPage");
            tabMouse.Tag = headset ? "off" : "on";
            tabHeadset.Tag = headset ? "on" : "off";
            mousePage.Visibility = headset ? Visibility.Collapsed : Visibility.Visible;
            headsetPage.Visibility = headset ? Visibility.Visible : Visibility.Collapsed;
            if (_headset != null)
            {
                _headset.SelectPage(headset);
                _headset.SyncAccess(ShouldSuspend);
            }
        }

        /// <summary>Offscreen screenshot hook: use the same tab switch, then show one real reading.</summary>
        public void ShowHeadsetPageForPreview()
        {
            if (_headset == null) return;
            SwitchDevice(true, false);
            var reading = _headsetService != null ? _headsetService.ReadOnce() : null;
            if (App.Demo && (reading == null || reading.Status != HeadsetStatus.Ok))
            {
                reading = new HeadsetReading();
                reading.Status = HeadsetStatus.Ok;
                reading.BatteryPercent = 74;
                reading.ChargingKnown = true;
                reading.ExternalPower = false;
                reading.ReadAt = DateTime.Now;
            }
            _headset.Preview(reading);
        }

        /// <summary>
        /// Reports what the shell actually did with our icon. A missing icon is otherwise
        /// ambiguous: it can mean a refused registration, or simply that Windows parked it in
        /// the overflow flyout, and only the shell can tell those apart.
        /// </summary>
        private void ShowTrayDiagnostics()
        {
            if (_tray == null) { _footStatus.Text = "托盘未初始化（离屏渲染模式）"; return; }
            if (TrayIcon.IsBelowMedium())
            {
                _footStatus.Text = "托盘：程序完整性级别 " + TrayIcon.DescribeIntegrity()
                    + "，低于任务栏要求的 Medium，系统会拒绝添加图标";
                TrayIcon.Log("diagnose: integrity below medium, " + TrayIcon.DescribeIntegrity());
                return;
            }

            string detail;
            System.Drawing.Rectangle rect;
            if (!_tray.Attempted) detail = "尚未向任务栏注册（窗口句柄还没建立）";
            else if (!_tray.IsRegistered)
                detail = "注册失败，win32 " + _tray.LastError + "，进程完整性 " + TrayIcon.DescribeIntegrity();
            else if (_tray.TryGetRect(out rect))
                detail = "已注册，图标位置 " + rect.Left + "," + rect.Top + " " + rect.Width + "x" + rect.Height;
            else detail = "已注册，但系统查不到图标位置（可能被收进折叠区）";

            _footStatus.Text = "托盘：" + detail;
            TrayIcon.Log("diagnose: " + detail);
        }

        private void HookDpi()
        {
            _slider.ValueChanged += delegate
            {
                if (_suppressSlider) return;
                int dpi = Snap(_slider.Value);
                ShowDpiHero(dpi.ToString("N0", CultureInfo.InvariantCulture));
                // The readout updates on every step, but the write is debounced so a drag
                // does not hammer the HID pipe.
                ScheduleApply();
            };

            // Releasing the thumb commits right away instead of waiting out the debounce.
            _slider.AddHandler(Thumb.DragCompletedEvent,
                new DragCompletedEventHandler(delegate { ApplyNow(); }));
            _slider.PreviewMouseLeftButtonUp += delegate { ApplyNow(); };
        }

        private void HookExtras()
        {
            var btnMinus = (Button)_win.FindName("BtnMinus");
            var btnPlus = (Button)_win.FindName("BtnPlus");
            if (btnMinus != null) btnMinus.Click += delegate { Step(-50); };
            if (btnPlus != null) btnPlus.Click += delegate { Step(50); };

            _slider.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                Step(e.Delta > 0 ? 50 : -50);
                e.Handled = true;
            };

            _snapToggle.Click += delegate
            {
                if (_api == null) return;
                bool want = _snapToggle.IsChecked == true;
                Run("设置直线修正…", delegate
                {
                    if (!_api.SetAngleSnapping(want)) return "设置失败：" + _api.LastError;
                    if (!_api.Save()) return "写入固件失败：" + _api.LastError;
                    return null;
                }, "已应用直线修正 · " + (want ? "开" : "关"));
            };

            HookQuickDpi("Quick400", 400);
            HookQuickDpi("Quick800", 800);
            HookQuickDpi("Quick1600", 1600);
            HookQuickDpi("Quick2400", 2400);
            HookQuickDpi("Quick3200", 3200);

            _win.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) HideToTray();
            };
        }

        /// <summary>
        /// Wires the in-app launch-at-logon switch and keeps it in sync with the tray
        /// checkbox. Both surfaces call the same AutoStart.Set, so neither can disagree
        /// with the registry for longer than one click.
        /// </summary>
        private void HookAutostart()
        {
            if (_autostartToggle == null) return;
            // The switch is host-side state, not device state: it reads and writes the
            // registry and must not wait for a device answer.
            bool enabled = AutoStart.IsEnabled();
            // Upgrade or migrate an older entry in place, so an enabled startup item keeps
            // working after the executable or registry value name changes.
            if (!enabled && AutoStart.NeedsUpgrade() && AutoStart.Enable() == null) enabled = true;
            _autostartToggle.IsChecked = enabled;
            _autostartToggle.Click += delegate
            {
                bool want = _autostartToggle.IsChecked == true;
                string error = AutoStart.Set(want);
                if (error != null)
                {
                    _autostartToggle.IsChecked = !want;
                    _footStatus.Text = error;
                    // A registry failure says nothing about the mouse link, so the status dot
                    // and the device tags keep whatever the last transaction proved.
                    TrayIcon.Log("autostart failed: " + error);
                    return;
                }
                if (_tray != null) _tray.SyncAutostartItem();
                _footStatus.Text = want ? "已开启开机自启动（当前用户登录时运行）" : "已关闭开机自启动";
            };
        }

        private void HookQuickDpi(string name, int dpi)
        {
            var b = (Button)_win.FindName(name);
            if (b == null) return;
            b.Click += delegate { SetDpi(dpi); };
        }

        private void Step(int delta)
        {
            int next = Snap(_slider.Value) + delta;
            if (next < _slider.Minimum) next = (int)_slider.Minimum;
            if (next > _slider.Maximum) next = (int)_slider.Maximum;
            _slider.Value = next;
            ApplyNow();
        }

        /// <summary>Clamps a preset index into the range the mouse actually reports.</summary>
        private static int ClampPreset(int index, int count)
        {
            if (count <= 0) return 0;
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }

        private static int Snap(double v)
        {
            int dpi = (int)Math.Round(v / 50.0) * 50;
            if (dpi < 100) dpi = 100;
            if (dpi > 36000) dpi = 36000;
            return dpi;
        }

        private void BuildChips()
        {
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var b = new Button();
                b.Style = (Style)_win.FindResource("StageChip");
                b.Content = (i + 1) + " · --";
                b.ToolTip = "档位 " + (i + 1);
                b.Tag = "off";
                b.Click += delegate { SelectPreset(index); };
                _stagePanel.Children.Add(b);
            }

            int[] rates = AsusProtocol.PollingRates;
            for (int i = 0; i < rates.Length; i++)
            {
                int hz = rates[i];
                var b = new Button();
                b.Style = (Style)_win.FindResource("RateChip");
                b.Content = hz + " Hz";
                b.Tag = "off";
                b.Click += delegate { SetRate(hz); };
                _ratePanel.Children.Add(b);
            }

            int[] deb = AsusProtocol.DebounceTimes;
            for (int i = 0; i < deb.Length; i++)
            {
                int ms = deb[i];
                var b = new Button();
                // Bare numbers in a compact segmented strip: eight of these have to fit on
                // one line next to the label instead of wrapping into a ragged block.
                b.Style = (Style)_win.FindResource("SegChip");
                b.Content = ms.ToString(CultureInfo.InvariantCulture);
                b.Tag = "off";
                b.Click += delegate { SetDebounce(ms); };
                _debouncePanel.Children.Add(b);
            }
        }

        private void MarkActive(Panel panel, string match)
        {
            foreach (UIElement el in panel.Children)
            {
                var b = el as Button;
                if (b == null) continue;
                string text = b.Content as string;
                b.Tag = (text == match) ? "on" : "off";
                // Re-apply the template so the Style trigger re-evaluates.
                b.Style = (Style)_win.FindResource("RateChip");
            }
        }

        /// <summary>
        /// Marks the debounce segment at <paramref name="index"/>; the segments show bare
        /// numbers, so text matching from MarkActive no longer applies.
        /// </summary>
        private void MarkActiveIndex(Panel panel, int index)
        {
            for (int i = 0; i < panel.Children.Count; i++)
            {
                var b = panel.Children[i] as Button;
                if (b == null) continue;
                b.Tag = (i == index) ? "on" : "off";
                b.Style = (Style)_win.FindResource("SegChip");
            }
        }

        /// <summary>
        /// Marks one stage chip as current. Indexed, because UpdateStageLabels rewrites the
        /// chip text to "档位 N · 1500" and text matching would stop working.
        /// </summary>
        private void MarkActiveStage(int index)
        {
            for (int i = 0; i < _stagePanel.Children.Count; i++)
            {
                var b = _stagePanel.Children[i] as Button;
                if (b == null) continue;
                b.Tag = (i == index) ? "on" : "off";
                b.Style = (Style)_win.FindResource("StageChip");
            }
        }

        /// <summary>Refreshes the "档位 N" chips with the real DPI values.</summary>
        private void UpdateStageLabels(MouseState st)
        {
            if (st.DpiX == null) return;
            for (int i = 0; i < _stagePanel.Children.Count && i < st.DpiX.Length; i++)
            {
                var b = (Button)_stagePanel.Children[i];
                b.Content = (i + 1) + " · " + st.DpiX[i].ToString("N0", CultureInfo.InvariantCulture);
                b.ToolTip = "档位 " + (i + 1);
            }
        }

        /// <summary>
        /// Surfaces a refused tray registration in the footer. Without it the only symptom is a
        /// missing icon, which is indistinguishable from Windows hiding the icon in the
        /// overflow flyout, so the reason has to be shown somewhere visible.
        /// </summary>
        private void ApplyTrayStatus()
        {
            if (_tray == null || _footStatus == null) return;
            // Before the window handle exists nothing has been attempted yet, so a missing
            // registration at that point means nothing.
            if (!_tray.Attempted) return;
            bool failed = !_tray.IsRegistered;
            if (failed)
                _footStatus.Text = TrayIcon.IsBelowMedium()
                    ? "托盘图标被系统拒绝：程序运行在低完整性级别，把 exe 放到桌面等普通目录再启动"
                    : "托盘图标创建失败 (win32 " + _tray.LastError + ")，关闭窗口不会隐藏";
            else if (_trayFailed)
                _footStatus.Text = "托盘图标已就绪，关闭窗口会收进托盘";
            _trayFailed = failed;
        }

        /// <summary>
        /// Parks the window in the tray. When the shell refused to create the icon there is no
        /// way back, so hiding is skipped and the user is told instead of losing the window.
        /// </summary>
        private void HideToTray()
        {
            if (_tray != null && !_tray.IsRegistered)
            {
                _footStatus.Text = TrayIcon.IsBelowMedium()
                    ? "托盘图标被系统拒绝：程序运行在低完整性级别，把 exe 放到桌面等普通目录再启动"
                    : "托盘图标创建失败 (win32 " + _tray.LastError + ")，窗口保持显示";
                return;
            }
            TrayIcon.Log("window hidden: parked to tray");
            _win.Hide();
            if (!_hiddenNotice && _tray != null)
            {
                _hiddenNotice = true;
                _tray.NotifyHidden();
            }
        }

        /// <summary>
        /// Surfaces the window on behalf of a second launch. Same path as the tray's
        /// ShowWindow, and device access resumes through the existing IsVisibleChanged
        /// hook, so no extra device traffic is introduced here.
        /// </summary>
        public void ActivateWindow()
        {
            TrayIcon.Log("window show requested: single-instance activation");
            _win.ShowInTaskbar = true;
            if (!_win.IsVisible)
            {
                _win.Left = (SystemParameters.PrimaryScreenWidth - _win.Width) / 2;
                _win.Top = (SystemParameters.PrimaryScreenHeight - _win.Height) / 2;
            }
            _win.Show();
            if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
            _win.Activate();
            _win.Topmost = true;
            _win.Topmost = false;
        }

        /// <summary>
        /// Leaves a logon launch parked in the tray after the hidden host window has been
        /// realized. If the shell refused the icon, keep the window visible instead; hiding
        /// it would leave no way to reach the app.
        /// </summary>
        public void HideForStartup()
        {
            if (_tray == null || !_tray.IsRegistered)
            {
                _win.ShowInTaskbar = true;
                _win.Left = (SystemParameters.PrimaryScreenWidth - _win.Width) / 2;
                _win.Top = (SystemParameters.PrimaryScreenHeight - _win.Height) / 2;
                _footStatus.Text = "开机启动时未能创建托盘图标，主窗口保持显示";
                _win.Show();
                return;
            }
            TrayIcon.Log("window hidden: startup");
            _hiddenNotice = true;
        }

        /// <summary>
        /// Parked means the window is hidden in the tray or minimized: nothing it shows can
        /// be read, so there is no reason to hold the device open or to keep polling it.
        /// </summary>
        private bool Parked
        {
            get { return !_win.IsVisible || _win.WindowState == WindowState.Minimized; }
        }

        /// <summary>
        /// True when the foreground window is a game that owns the whole monitor: it covers
        /// the entire primary screen and has no caption, i.e. exclusive or borderless
        /// fullscreen. A maximized desktop window keeps WS_CAPTION and is not matched, so
        /// ordinary windowed work never suspends the device.
        /// </summary>
        private bool FullScreenAppRunning()
        {
            try
            {
                IntPtr fg = NativeMethods.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                // This window is never the game we are looking for; without the guard a
                // borderless owner-drawn window of our own would look fullscreen.
                var self = new System.Windows.Interop.WindowInteropHelper(_win);
                if (fg == self.Handle) return false;

                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(fg, out r)) return false;
                int w = NativeMethods.GetSystemMetrics(0);
                int h = NativeMethods.GetSystemMetrics(1);
                if (r.Right - r.Left < w || r.Bottom - r.Top < h) return false;

                int style = NativeMethods.GetWindowLong(fg, NativeMethods.GWL_STYLE);
                return (style & NativeMethods.WS_CAPTION) == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Why device traffic stops. Both reasons describe a moment when the reading on
        /// screen cannot change or cannot be seen: the window is parked, or a fullscreen app
        /// owns the screen. In both cases the app keeps holding an open handle to the vendor
        /// collection and talks to it periodically, which is the only thing it still does on
        /// the pointer's own HID link.
        /// </summary>
        private bool ShouldSuspend
        {
            get { return Parked || FullScreenAppRunning(); }
        }

        private void SyncDeviceAccess()
        {
            if (ShouldSuspend) SuspendDevice();
            else ResumeDevice();
            if (_headset != null) _headset.SyncAccess(ShouldSuspend);
        }

        /// <summary>True between a suspension and the next resume; keeps the first resume from re-reading.</summary>
        private bool _suspended;

        private void SuspendDevice()
        {
            if (_suspended) return;
            _suspended = true;
            if (_poll != null) _poll.Stop();
            ReleaseApi();
        }

        /// <summary>
        /// Closes the vendor collection. Dispose waits for an in-flight transaction to
        /// finish, so it never runs on the UI thread where it could freeze the window for
        /// up to one command timeout.
        /// </summary>
        private void ReleaseApi()
        {
            var api = _api;
            _api = null;
            if (api == null) return;
            TrayIcon.Log("device access suspended: releasing vendor handle");
            Task.Factory.StartNew(delegate { try { api.Dispose(); } catch { } });
        }

        private void ResumeDevice()
        {
            // The very first show also lands here; nothing was suspended yet, and Loaded
            // already kicks off the initial read.
            if (!_suspended) return;
            _suspended = false;
            TrayIcon.Log("device access resumed: reopening vendor handle");
            _quietTicks = 0;
            if (_poll != null && !_poll.IsEnabled) _poll.Start();
            BeginRefresh();
        }

        /// <summary>Only the tray menu's Quit sets the flag; everything else parks in the tray.</summary>
        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            HideToTray();
        }

        private void Quit()
        {
            _allowClose = true;
            if (_watch != null) _watch.Stop();
            if (_poll != null) _poll.Stop();
            if (_trayPoll != null) _trayPoll.Stop();
            if (_headsetTrayPoll != null) _headsetTrayPoll.Stop();
            if (_headset != null) _headset.Shutdown();
            if (_tray != null) { _tray.Dispose(); _tray = null; }
            if (_api != null) { _api.Dispose(); _api = null; }
            System.Windows.Application.Current.Shutdown();
        }

        // ---------------- device work ----------------

        /// <summary>
        /// Synchronous, read-only refresh used by the offscreen renderer: pulls the live
        /// state from the mouse and paints it without touching the UI thread queue.
        /// </summary>
        public void RefreshSyncForPreview()
        {
            try
            {
                _conn = ConnectionInfo.Detect();
                ApplyConnection();
                if (_api == null)
                {
                    var dev = AsusHidDevice.OpenFirst();
                    if (dev == null)
                    {
                        if (App.Demo)
                        {
                            var demo = DemoMouseState();
                            _state = demo;
                            ApplyState(demo);
                            SetDot(true);
                            _footStatus.Text = "已连接（演示数据）";
                            return;
                        }
                        _footStatus.Text = "未找到 ROG 鼠标";
                        SetDot(false);
                        return;
                    }
                    _api = new AsusProtocol(dev);
                }
                var st = _api.ReadAll();
                // Screenshot review must not change just because the physical mouse happened to
                // wake for a partial battery read. Demo mode always renders the complete sample.
                if (App.Demo) st = DemoMouseState();
                if (st.BatteryPercent < 0 && st.DpiX[0] <= 50)
                {
                    if (!App.Demo) { SetDot(false); return; }
                    st = DemoMouseState();
                }
                if (st.ActivePreset >= 0)
                    _selectedPreset = ClampPreset(st.ActivePreset, st.DpiX.Length);
                _state = st;
                ApplyState(st);
                SetDot(true);
                _footStatus.Text = "已连接 · " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                _footStatus.Text = "读取失败：" + ex;
                SetDot(false);
                if (App.Demo)
                {
                    var st = DemoMouseState();
                    _state = st;
                    ApplyState(st);
                    SetDot(true);
                    _footStatus.Text = "已连接（演示数据） · " + DateTime.Now.ToString("HH:mm:ss");
                }
            }
        }

        /// <summary>
        /// Synthetic mouse reading for the screenshot hooks only. The hardware is asleep most of
        /// the time this tool is developed against, so the populated layout still needs a way to
        /// be reviewed. Reachable exclusively through --shot-demo.
        /// </summary>
        private static MouseState DemoMouseState()
        {
            var st = new MouseState();
            st.Valid = true;
            st.Connected = true;
            st.BatteryPercent = 78;
            st.DpiX = new int[] { 400, 800, 1200, 3200 };
            st.DpiY = new int[] { 400, 800, 1200, 3200 };
            st.ActivePreset = 2;
            st.PollingRate = 1000;
            st.Debounce = 12;
            st.AngleSnapping = false;
            st.FirmwarePrimary = "4.0.1";
            return st;
        }

        private void BeginRefresh()
        {
            if (NoDevice)
            {
                _footStatus.Text = "诊断模式：未打开设备句柄";
                return;
            }

            Run("正在读取设备…", delegate
            {
                _conn = ConnectionInfo.Detect();

                if (_api == null)
                {
                    var dev = AsusHidDevice.OpenFirst();
                    if (dev == null) return "未找到 ROG 鼠标（请确认 2.4G 接收器已插好）";
                    _api = new AsusProtocol(dev);
                }
                var st = _api.ReadAll();
                if (!st.Connected || (!st.Valid && st.BatteryPercent < 0))
                    return "设备无响应，请晃动鼠标唤醒后重试";

                _state = st;
                ApplyStateSafe(st);
                // A refresh can also be started from the tray menu while the window is
                // parked; the handle is not allowed to stay open then.
                if (_suspended) ReleaseApi();
                return null;
            });
        }

        /// <summary>
        /// Read-only refresh driven by the tray timer: never touches the foot status and
        /// never reports failure, because a sleeping mouse is the normal case here. The
        /// next tick simply tries again.
        /// </summary>
        private void RefreshQuiet()
        {
            if (_busy || NoDevice || ShouldSuspend) return;
            _busy = true;
            Task.Factory.StartNew(delegate
            {
                MouseState st = null;
                ConnectionInfo conn = null;
                int level = -1;
                bool levelOk = false;
                try
                {
                    // A full read is only needed when something the level cannot tell us may
                    // have changed: before the first good frame, and once every few ticks as a
                    // safety net for onboard-button edits. The steady state is one battery
                    // command instead of the four-command burst, because every one of those is a
                    // synchronous vendor transaction on the same device the pointer lives on.
                    bool full = _state == null || !_state.Valid || (_quietTicks % 5) == 0;
                    _quietTicks++;

                    // Enumerating the interfaces is a second set of opens on the same vendor
                    // collection; it rides along with the full read instead of every tick.
                    if (full || _conn == null) conn = ConnectionInfo.Detect();

                    if (_api == null)
                    {
                        var dev = AsusHidDevice.OpenFirst();
                        if (dev != null) _api = new AsusProtocol(dev);
                    }
                    if (_api != null)
                    {
                        if (full)
                        {
                            var read = _api.ReadAll();
                            // Partial reads are fine here: the protocol layer keeps the rejected
                            // sections at their sentinel values, and ApplyState only repaints the
                            // parts that actually arrived.
                            if (read.Connected)
                            {
                                _state = read;
                                st = read;
                            }
                        }
                        else
                        {
                            int p; bool c; int raw;
                            // A sleeping mouse still answers, but with every byte zero; that is
                            // the same signature the profile and settings readers already reject,
                            // and painting it would claim an empty battery.
                            if (_api.ReadBattery(out p, out c, out raw) && !(p == 0 && raw == 0))
                            {
                                level = p;
                                levelOk = true;
                            }
                            else
                            {
                                // No level came back: the mouse is asleep or the link dropped.
                                // Run the full read here so the wake-up path behaves as before.
                                var read = _api.ReadAll();
                                if (read.Connected)
                                {
                                    _state = read;
                                    st = read;
                                }
                            }
                        }
                    }
                }
                catch { }

                MouseState applied = st;
                ConnectionInfo detected = conn;
                int levelRead = level;
                bool haveLevelOnly = levelOk;
                _win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (detected != null) { _conn = detected; ApplyConnection(); }
                    if (applied != null) ApplyState(applied);
                    else if (haveLevelOnly) ApplyLevel(levelRead);
                    PushTray();
                    // Poll hard while the mouse is asleep or the level is still unknown so the
                    // tray catches the first successful read, then back off to once a minute.
                    bool haveLevel = _state != null && _state.BatteryPercent >= 0;
                    if (_poll != null) _poll.Interval = TimeSpan.FromSeconds(haveLevel ? 60 : 15);
                    _busy = false;
                }));
            });
        }

        /// <summary>
        /// One short device transaction per 5 minutes while the window is parked, so the
        /// tray battery ring stays honest. Runs its own open-and-release cycle instead of
        /// touching _api/_suspended — the suspension state machine stays untouched, and the
        /// vendor handle is never held between ticks. Skipped entirely while a fullscreen
        /// app owns the screen, which keeps the v1.2 stutter guarantee intact.
        /// </summary>
        private void TrayPollTick()
        {
            if (_busy || NoDevice || !Parked || FullScreenAppRunning()) return;
            _busy = true;
            Task.Factory.StartNew(delegate
            {
                int percent = -1;
                try
                {
                    var dev = AsusHidDevice.OpenFirst();
                    if (dev != null)
                    {
                        var api = new AsusProtocol(dev);
                        int p; bool c; int raw;
                        // A sleeping mouse answers with every byte zero; that signature is
                        // rejected here just like in RefreshQuiet.
                        if (api.ReadBattery(out p, out c, out raw) && !(p == 0 && raw == 0))
                            percent = p;
                        api.Dispose();
                    }
                }
                catch { }

                int level = percent;
                _win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (level >= 0)
                    {
                        if (_state != null) _state.BatteryPercent = level;
                        PushTray();
                        TrayIcon.Log("tray keep-alive: battery " + level + "%");
                    }
                    _busy = false;
                }));
            });
        }

        /// <summary>
        /// Slow headset keep-alive for the tray. It never runs while the app is hidden,
        /// minimized, fullscreen, or in a diagnostic --no-* mode.
        /// </summary>
        private void HeadsetTrayTick()
        {
            if (NoDevice || NoPoll || FullScreenAppRunning()) return;
            if (_headset == null) return;
            _headset.RefreshForTray(delegate(HeadsetReading reading) { PushTray(); });
        }

        private void RefreshBothFromTray()
        {
            BeginRefresh();
            if (_headset != null)
                _headset.RefreshForTray(delegate(HeadsetReading reading) { PushTray(); });
        }

        /// <summary>Tray menu shortcut: open the window on the requested device page.</summary>
        public void ShowDeviceFromTray(bool headset)
        {
            ActivateWindow();
            SwitchDevice(headset, true);
        }

        /// <summary>
        /// Selects a DPI stage and makes it the one the mouse actually runs at, so clicking a
        /// stage is a real switch and not just a pick of which slot to edit. The mouse keeps
        /// the choice across power cycles once SAVE lands.
        /// </summary>
        private void SelectPreset(int index)
        {
            if (_state == null || _state.DpiX == null) return;
            index = ClampPreset(index, _state.DpiX.Length);
            int dpi = _state.DpiX[index];

            // Paint the new selection immediately; the write below runs in the background.
            _selectedPreset = index;
            _suppressSlider = true;
            _slider.Value = dpi;
            _suppressSlider = false;
            ShowDpiHero(dpi.ToString("N0", CultureInfo.InvariantCulture));
            MarkActiveStage(index);

            if (_api == null) { _footStatus.Text = "设备未连接"; return; }
            Run("正在切换到档位 " + (index + 1) + "…", delegate
            {
                if (!_api.SetActiveStage(index)) return "切换档位失败：" + _api.LastError;
                if (!_api.Save()) return "写入固件失败：" + _api.LastError;
                var st = ReadAllRetry(4);
                if (st == null || !st.Valid) return "切换后读取超时，请点「重新读取」确认";
                if (st.ActivePreset != index)
                    return "回读不一致（鼠标停在档位 " + (st.ActivePreset + 1) + "）";
                _state = st;
                ApplyStateSafe(st);
                return null;
            }, "已切换到档位 " + (index + 1) + " · "
               + dpi.ToString("N0", CultureInfo.InvariantCulture) + " DPI");
        }

        /// <summary>
        /// Applies a DPI value to the selected stage and persists it in one round trip. Used
        /// by the quick-pick buttons, where the user already committed to a value.
        /// </summary>
        private void SetDpi(int dpi)
        {
            if (_api == null) { _footStatus.Text = "设备未连接"; return; }
            _slider.Value = dpi;
            int index = _selectedPreset;
            Run("正在应用 " + dpi + " DPI…", delegate
            {
                if (!_api.SetDpi(index, dpi, true)) return "写入 DPI 失败：" + _api.LastError;
                if (!_api.Save()) return "写入固件失败：" + _api.LastError;
                var st = ReadAllRetry(4);
                if (st == null || st.DpiX == null || st.DpiX[0] <= 0) return "写入后读取超时，请点「重新读取」确认";
                if (st.DpiX[index] != dpi) return "回读不一致（读到 " + st.DpiX[index] + "，期望 " + dpi + "）";
                _state = st;
                ApplyStateSafe(st);
                return null;
            }, "已应用 " + dpi + " DPI 并写入固件");
        }

        private void SetRate(int hz)
        {
            if (_api == null) return;
            Run("设置轮询率…", delegate
            {
                if (!_api.SetPollingRate(hz)) return "设置轮询率失败：" + _api.LastError;
                if (!_api.Save()) return "写入固件失败：" + _api.LastError;
                var st = ReadAllRetry(4);
                ApplyStateSafe(st);
                return null;
            }, "已应用 " + hz + " Hz 并写入固件");
        }

        private void SetDebounce(int ms)
        {
            if (_api == null) return;
            Run("设置按键响应…", delegate
            {
                if (!_api.SetDebounce(ms)) return "设置按键响应失败：" + _api.LastError;
                if (!_api.Save()) return "写入固件失败：" + _api.LastError;
                var st = ReadAllRetry(4);
                ApplyStateSafe(st);
                return null;
            }, "已应用 " + ms + " ms 并写入固件");
        }

        // ---------------- debounced DPI apply ----------------

        /// <summary>
        /// Marks a pending DPI edit. The slider fires per 50 DPI step, so the write waits for
        /// a short pause; releasing the thumb or the mouse wheel commits through ApplyNow().
        /// </summary>
        private void ScheduleApply()
        {
            if (_api == null) return;
            _autoSave.Stop();
            _autoSave.Start();
            if (_saveHint != null) _saveHint.Text = "待写入 " + Snap(_slider.Value).ToString("N0", CultureInfo.InvariantCulture) + " DPI…";
        }

        private void ApplyNow()
        {
            if (_api == null) return;
            if (_autoSave != null) _autoSave.Stop();
            int dpi = Snap(_slider.Value);
            int index = _selectedPreset;
            Run("正在应用 " + dpi + " DPI…", delegate
            {
                if (!_api.SetDpi(index, dpi, true)) return "写入 DPI 失败：" + _api.LastError;
                if (!_api.Save()) return "写入固件失败：" + _api.LastError;
                var st = ReadAllRetry(4);
                if (st == null || st.DpiX == null || st.DpiX[0] <= 0) return "写入后读取超时，请点「重新读取」确认";
                if (st.DpiX[index] != dpi) return "回读不一致（读到 " + st.DpiX[index] + "，期望 " + dpi + "）";
                _state = st;
                ApplyStateSafe(st);
                return null;
            }, "已应用 " + dpi + " DPI 并写入固件");
        }

        private void OnAutoSaveTick()
        {
            _autoSave.Stop();
            ApplyNow();
        }

        /// <summary>
        /// Reads the whole device state, retrying a couple of times. The mouse can drop the
        /// first request issued right after SAVE, and a dropped read leaves DpiX at zero,
        /// which would look like a verification mismatch instead of a transient timeout.
        /// </summary>
        private MouseState ReadAllRetry(int attempts)
        {
            MouseState last = null;
            for (int i = 0; i < attempts; i++)
            {
                last = _api.ReadAll();
                if (last.DpiX != null && last.DpiX[0] > 0) return last;
                System.Threading.Thread.Sleep(90);
            }
            return last;
        }

        // ---------------- background runner ----------------

        private bool _busy;

        private void Run(string busyText, Func<string> work)
        {
            Run(busyText, work, null);
        }

        /// <summary>
        /// Runs a device operation on a worker thread. <paramref name="doneText"/> is what the
        /// footer shows on success, so an immediate write can say what it just persisted.
        /// </summary>
        private void Run(string busyText, Func<string> work, string doneText)
        {
            if (_busy) return;
            _busy = true;
            SetBusy(busyText);
            Task.Factory.StartNew(delegate
            {
                string error;
                try { error = work(); }
                catch (Exception ex) { error = "操作异常：" + ex.Message; }

                _win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (error == null)
                    {
                        _footStatus.Text = doneText != null
                            ? doneText
                            : "已连接 · " + DateTime.Now.ToString("HH:mm:ss");
                        SetDot(true);
                    }
                    else
                    {
                        _footStatus.Text = error;
                        SetDot(false);
                    }
                    _busy = false;
                    SetIdle();
                }));
            });
        }

        private void SetBusy(string text)
        {
            _footStatus.Text = text;
            SetActionsEnabled(false);
        }

        private void SetIdle()
        {
            SetActionsEnabled(true);
        }

        private void SetActionsEnabled(bool on)
        {
            var btnRefresh = (Button)_win.FindName("BtnRefresh");
            if (btnRefresh != null) btnRefresh.IsEnabled = on;
        }

        /// <summary>
        /// Single source of truth for "the mouse answered": paints the header dot and the two
        /// small status tags together so they can never disagree.
        /// </summary>
        private void SetDot(bool ok)
        {
            _mouseOnline = ok;
            _mouseProbed = true;
            PaintDeviceTags();
        }

        /// <summary>
        /// Paints the DPI hero. A placeholder has to shrink: two hyphens at 86 px read as two
        /// slabs and dominate a page that has no number to report yet.
        /// </summary>
        private void ShowDpiHero(string text)
        {
            if (_dpiValue == null) return;
            bool placeholder = text == "--";
            _dpiValue.Text = placeholder ? "--" : text;
            _dpiValue.FontSize = placeholder ? 40 : 86;
            _dpiValue.Foreground = TagBrush(placeholder ? "#5B6561" : "#FFFFFF");
        }

        /// <summary>
        /// Paints the two small status tags that claim the mouse is alive: the header pair on
        /// the mouse page and the status line in the device rail. Both come from the last
        /// transaction result plus the USB enumeration, never from a fixed label, so a mouse
        /// that is unplugged or asleep cannot be reported as "online".
        /// </summary>
        private void PaintDeviceTags()
        {
            bool live = _mouseOnline;
            string code = _conn != null ? _conn.TransportCode : "LINK";
            // Three honest states: the mouse answered, nothing is attached, or the probe ran and
            // the mouse stayed quiet. "STANDBY" only covers the case where no attempt happened yet.
            bool absent = _conn != null && _conn.Count == 0;
            string verdict = live ? "OK" : absent ? "OFFLINE" : _mouseProbed ? "NO REPLY" : "STANDBY";
            // Only a probe that got no answer is worth the amber caution colour; "nothing
            // attached" and "nothing attempted yet" stay neutral.
            string tone = live ? "#79FF5A" : _mouseProbed && !absent ? "#FFBD62" : "#5B6561";

            if (_headerEyebrow != null)
                _headerEyebrow.Text = "MOUSE / " + EyebrowTransport();
            if (_headerDevice != null)
                _headerDevice.Text = "DEVICE 01 / " + (live ? "LIVE" : "IDLE");
            if (_headerLink != null)
            {
                _headerLink.Text = code + " / " + verdict;
                _headerLink.Foreground = TagBrush(tone);
            }
            if (_tabMouseStatus != null)
            {
                _tabMouseStatus.Text = live ? "CONNECTED" : absent ? "OFFLINE" : _mouseProbed ? "NO REPLY" : "STANDBY";
                _tabMouseStatus.Foreground = TagBrush(tone);
            }
            // The dot and the tags are one signal, so they share one colour.
            if (_statusDot != null) _statusDot.Background = TagBrush(tone);
            // The title chip for the mouse follows the same reading as the battery bar.
            PaintTitleChips();
        }

        /// <summary>
        /// Headset counterpart: the rail row shows what the receiver last answered, using the
        /// same degraded colour as the headset page badge.
        /// </summary>
        private void PaintHeadsetTags()
        {
            if (_tabHeadsetStatus == null || _headset == null) return;
            var reading = _headset.LastReading;
            _tabHeadsetStatus.Text = HeadsetPageState.LinkCode(reading);
            _tabHeadsetStatus.Foreground = TagBrush(HeadsetPageState.LinkColor(reading));
            PaintTitleChips();
        }

        /// <summary>
        /// Title-bar battery chips. Both used to be two static coloured dots that implied the
        /// device was present; now each chip carries the level and only lights up when that
        /// device actually produced a number.
        /// </summary>
        private void PaintTitleChips()
        {
            int mouse = _state != null ? _state.BatteryPercent : -1;
            if (_chipMouse != null)
                _chipMouse.Text = "ROG KERIS " + (mouse >= 0 ? mouse + "%" : "--");
            if (_chipMouseDot != null)
                _chipMouseDot.Background = TagBrush(mouse >= 0 ? "#FF4655" : "#5B6561");

            var hs = _headset != null ? _headset.LastReading : null;
            bool hsOk = hs != null && hs.Status == HeadsetStatus.Ok;
            if (_chipHeadset != null)
                _chipHeadset.Text = "BLACKSHARK " + (hsOk ? hs.BatteryPercent + "%" : "--");
            if (_chipHeadsetDot != null)
                _chipHeadsetDot.Background = TagBrush(hsOk ? "#79FF5A" : "#5B6561");
        }

        private static Brush TagBrush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        /// <summary>
        /// Transport half of the mouse-page eyebrow label. Derived from the enumeration so a
        /// mouse on Bluetooth or a cable is not still titled as a 2.4 GHz receiver.
        /// </summary>
        private string EyebrowTransport()
        {
            if (_conn == null) return "RECEIVER";
            if (_conn.Bluetooth && (_conn.Receiver || _conn.Wired)) return "2.4 GHZ + BT";
            if (_conn.Bluetooth) return "BLUETOOTH";
            if (_conn.Receiver && _conn.Wired) return "2.4 GHZ + USB";
            if (_conn.Receiver) return "2.4 GHZ RECEIVER";
            if (_conn.Wired) return "USB WIRED";
            return "RECEIVER";
        }

        // ---------------- rendering ----------------

        /// <summary>
        /// Applies device state from whichever thread read it: work runs on a background
        /// thread, but every control here belongs to the UI thread.
        /// </summary>
        private void ApplyStateSafe(MouseState st)
        {
            if (_win.Dispatcher.CheckAccess()) { ApplyState(st); return; }
            _win.Dispatcher.BeginInvoke(new Action(delegate { ApplyState(st); }));
        }

        private void ApplyState(MouseState st)
        {
            // Any state that came back from the mouse proves the link is answering, including
            // the cheap poll and the offscreen preview path, which do not run through Run().
            if (st.Connected || st.BatteryPercent >= 0) { _mouseOnline = true; PaintDeviceTags(); }
            // The active preset has to be resolved before anything paints, otherwise the
            // big readout shows the previous preset's DPI for one refresh.
            if (st.DpiX != null && st.DpiX.Length > 0 && st.ActivePreset >= 0)
                _selectedPreset = ClampPreset(st.ActivePreset, st.DpiX.Length);

            // A frame the protocol layer rejected leaves DpiX at its zero defaults; painting
            // it would replace the real numbers with 0. Keep the last good reading instead.
            bool haveDpi = st.Valid && st.DpiX != null && st.DpiX.Length > 0 && st.DpiX[0] > 0;

            if (haveDpi)
            {
                int shown = ClampPreset(_selectedPreset, st.DpiX.Length);
                _suppressSlider = true;
                _slider.Value = st.DpiX[shown];
                _suppressSlider = false;
                ShowDpiHero(st.DpiX[shown].ToString("N0", CultureInfo.InvariantCulture));
            }

            if (st.BatteryPercent >= 0)
            {
                _batteryValue.Text = st.BatteryPercent.ToString(CultureInfo.InvariantCulture);
                _batteryHint.Text = BatteryWord(st.BatteryPercent);
                DrawBattery(st.BatteryPercent);
            }
            else
            {
                _batteryValue.Text = "--";
                _batteryHint.Text = "无法读取电量";
                DrawBattery(-1);
            }

            _snapToggle.IsChecked = st.AngleSnapping;

            if (st.PollingRate > 0) MarkActive(_ratePanel, st.PollingRate + " Hz");
            if (st.Debounce > 0) MarkActiveIndex(_debouncePanel, Array.IndexOf(AsusProtocol.DebounceTimes, st.Debounce));
            MarkActiveStage(st.ActivePreset >= 0 ? _selectedPreset : -1);

            if (haveDpi) UpdateStageLabels(st);

            if (_specPreset != null && haveDpi)
            {
                int p = ClampPreset(_selectedPreset, st.DpiX.Length);
                _specPreset.Text = "档位 " + (p + 1) + " · "
                    + st.DpiX[p].ToString("N0", CultureInfo.InvariantCulture);
            }
            if (_specRate != null && st.PollingRate > 0) _specRate.Text = st.PollingRate + " Hz";
            if (_specDebounce != null && st.Debounce > 0) _specDebounce.Text = st.Debounce + " ms";

            if (_conn != null) ApplyConnection();

            if (!string.IsNullOrEmpty(st.FirmwarePrimary) && _infoFirmware != null)
                _infoFirmware.Text = "固件 " + st.FirmwarePrimary;

            // Only the pending-apply hint may overwrite this line; a plain refresh should not
            // leave the placeholder text sitting there once the device has answered.
            if (_saveHint != null && _autoSave != null && !_autoSave.IsEnabled)
                _saveHint.Text = "调整立即生效并写入固件";

            PushTray();
        }

        /// <summary>
        /// Transport and power come from the HID interface list rather than the protocol,
        /// so they stay correct while the mouse itself is asleep and will not answer.
        /// </summary>
        private void ApplyConnection()
        {
            if (_conn == null) return;
            if (_connText != null) _connText.Text = _conn.Transport;
            if (_specConn != null) _specConn.Text = _conn.TransportShort;
            if (_specCharge != null) _specCharge.Text = _conn.Power;
            PaintDeviceTags();
        }

        private void PushTray()
        {
            PaintHeadsetTags();
            if (_tray == null) return;
            int percent = _state != null ? _state.BatteryPercent : -1;
            var hs = _headset != null ? _headset.LastReading : null;
            int headsetPercent = hs != null && hs.Status == HeadsetStatus.Ok ? hs.BatteryPercent : -1;
            bool powerKnown = hs != null && hs.ChargingKnown;
            bool external = hs != null && hs.ExternalPower;
            _tray.Update(percent, _conn != null ? _conn.TransportShort : "",
                headsetPercent, powerKnown, external);
        }

        /// <summary>Pushes the current headset value to the tray after a page read.</summary>
        private void UpdateTrayFromHeadset()
        {
            PushTray();
        }

        /// <summary>
        /// Repaints only the charge readout. Used by the cheap poll, which asks the mouse
        /// for the level and nothing else; every other control already shows a good value.
        /// </summary>
        private void ApplyLevel(int percent)
        {
            if (_state == null) return;
            _state.BatteryPercent = percent;
            if (percent >= 0)
            {
                _batteryValue.Text = percent.ToString(CultureInfo.InvariantCulture);
                _batteryHint.Text = BatteryWord(percent);
                DrawBattery(percent);
                PaintTitleChips();
            }
        }

        /// <summary>
        /// The status byte the mouse returns alongside the level (0x01 / 0x02) does not
        /// track the charging state reliably, so the UI describes the level instead of
        /// claiming a charging state we cannot verify.
        /// </summary>
        private static string BatteryWord(int percent)
        {
            if (percent < 0) return "无法读取";
            if (percent >= 80) return "电量充足";
            if (percent >= 40) return "电量良好";
            if (percent >= 20) return "电量偏低";
            return "请尽快充电";
        }

        /// <summary>Fills the compact mouse telemetry bar; percent &lt; 0 leaves it empty.</summary>
        private void DrawBattery(int percent)
        {
            if (_batteryFill == null) return;
            if (percent < 0)
            {
                _batteryFill.Width = 0;
                return;
            }
            _batteryFill.Width = 128.0 * Math.Min(percent, 100) / 100.0;
        }

    }
}
