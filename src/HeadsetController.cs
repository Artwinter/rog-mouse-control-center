using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RogMouse
{
    public sealed class HeadsetController
    {
        private readonly Window _win;
        private readonly HeadsetService _service;
        private readonly bool _noDevice;
        private readonly bool _noPoll;
        private TextBlock _battery;
        private Border _bar;
        private TextBlock _state;
        private TextBlock _conn;
        private TextBlock _power;
        private TextBlock _lastRead;
        private TextBlock _status;
        private TextBlock _receiver;
        private Border _stateBadge;
        private Button _refresh;
        private DispatcherTimer _timer;
        private bool _busy;
        private bool _pageSelected;
        private bool _suspended;
        private int _generation;
        private bool _automaticPolling = true;
        private bool _suspendRefreshPending;
        private HeadsetReading _last;

        public HeadsetController(Window win, HeadsetService service, bool noDevice, bool noPoll)
        {
            _win = win;
            _service = service;
            _noDevice = noDevice;
            _noPoll = noPoll;
        }

        public void Initialize()
        {
            _battery = (TextBlock)_win.FindName("HeadsetBattery");
            _bar = (Border)_win.FindName("HeadsetBar");
            _state = (TextBlock)_win.FindName("HeadsetState");
            _conn = (TextBlock)_win.FindName("HeadsetConn");
            _power = (TextBlock)_win.FindName("HeadsetPower");
            _lastRead = (TextBlock)_win.FindName("HeadsetLastRead");
            _status = (TextBlock)_win.FindName("HeadsetStatus");
            _receiver = (TextBlock)_win.FindName("HeadsetReceiver");
            _stateBadge = (Border)_win.FindName("HeadsetStateBadge");
            _refresh = (Button)_win.FindName("BtnHeadsetRefresh");
            if (_refresh != null) _refresh.Click += delegate { Refresh(); };

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(60);
            _timer.Tick += delegate { Refresh(); };
            if (_noPoll) _automaticPolling = false;
            Render(null);
        }

        public bool PageSelected { get { return _pageSelected; } }

        /// <summary>Latest reading for the tray. Null until the first attempt finishes.</summary>
        public HeadsetReading LastReading { get { return _last; } }
        public Action<HeadsetReading> ReadingChanged;

        public bool Suspended { get { return _suspended; } }

        /// <summary>
        /// Tray keep-alive. Unlike <see cref="Refresh"/>, this is allowed while the headset
        /// page is not selected, but it never runs while the window is parked or fullscreen.
        /// </summary>
        public void RefreshForTray(Action<HeadsetReading> done)
        {
            if (_noDevice || _noPoll || _busy) return;
            _busy = true;
            Task.Factory.StartNew(delegate { return _service.ReadOnce(true); })
                .ContinueWith(delegate(Task<HeadsetReading> t)
                {
                    var reading = t.IsFaulted ? null : t.Result;
                    _win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (reading != null) _last = reading;
                        _busy = false;
                        if (done != null) done(reading);
                    }));
                });
        }

        public void SelectPage(bool selected)
        {
            bool wasSelected = _pageSelected;
            _generation++;
            _pageSelected = selected;
            if (selected)
            {
                if (_suspended) { Render(_last); return; }
                SyncTimer(_automaticPolling);
                Refresh();
            }
            else
            {
                SyncTimer(false);
                if (wasSelected) _busy = false;
            }
            Render(_last);
        }

        /// <summary>
        /// Stops background traffic while parked or while a fullscreen app has focus. The
        /// app-level suspension gate remains the source of truth; this method only mirrors it.
        /// </summary>
        public void SyncAccess(bool suspended)
        {
            bool resumed = _suspended && !suspended;
            _suspended = suspended;
            SyncTimer(!suspended && _pageSelected);
            if (resumed && _pageSelected) _suspendRefreshPending = true;
            if (!suspended && _pageSelected && (_last == null || resumed)) Refresh();
            Render(_last);
        }

        /// <summary>Offscreen screenshot hook: display a supplied reading without starting timers.</summary>
        public void Preview(HeadsetReading reading)
        {
            _pageSelected = true;
            SyncTimer(false);
            _last = reading;
            Render(reading);
        }

        public void SetAutomaticPolling(bool enabled)
        {
            _automaticPolling = enabled;
            if (!enabled) SyncTimer(false);
            else if (_pageSelected && !_suspended) SyncTimer(true);
        }

        /// <summary>
        /// One startup read for the tray. It does not start the 60-second page timer and
        /// still respects --no-device, --no-poll, hidden/minimized, and fullscreen gates.
        /// </summary>
        public void ReadOnceForTrayStartup()
        {
            if (_noDevice || _noPoll || _busy || _suspended) return;
            if (!_win.IsVisible || _win.WindowState == WindowState.Minimized) return;
            RefreshForTray(delegate(HeadsetReading reading) { });
        }

        private void SyncTimer(bool enabled)
        {
            if (_timer == null) return;
            if (enabled) { if (!_timer.IsEnabled) _timer.Start(); }
            else _timer.Stop();
        }

        public void Refresh()
        {
            if (_noDevice) return;
            if (_busy || !_pageSelected || !HeadsetPageState.CanPoll(true, _win.IsVisible,
                    _win.WindowState == WindowState.Minimized, _suspended)) return;
            _busy = true;
            int generation = ++_generation;
            if (_refresh != null) _refresh.IsEnabled = false;
            if (_status != null && _last == null) _status.Text = "正在读取耳机电量…";

            Task.Factory.StartNew(delegate { return _service.ReadOnce(!_noDevice); })
                .ContinueWith(delegate(Task<HeadsetReading> t)
                {
                    if (t.IsFaulted)
                    {
                        var failed = new HeadsetReading();
                        failed.Status = HeadsetStatus.OpenFailed;
                        failed.Error = "读取耳机任务异常";
                        t = Task<HeadsetReading>.FromResult(failed);
                    }
                    var reading = t.Result;
                    _win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (generation != _generation)
                        {
                        _busy = false;
                            if (_refresh != null && _pageSelected && !_suspended) _refresh.IsEnabled = true;
                        if (_suspendRefreshPending && _pageSelected && !_suspended)
                        {
                            _suspendRefreshPending = false;
                            Refresh();
                        }
                            return;
                        }
                        _last = reading;
                        if (ReadingChanged != null) ReadingChanged(reading);
                        Render(reading);
                        _busy = false;
                        if (_refresh != null) _refresh.IsEnabled = true;
                        if (_suspendRefreshPending && _pageSelected && !_suspended)
                        {
                            _suspendRefreshPending = false;
                            Refresh();
                        }
                    }));
                });
        }

        /// <summary>Stop timer and invalidate any in-flight result before the window closes.</summary>
        public void Shutdown()
        {
            _generation++;
            if (_timer != null) _timer.Stop();
        }

        private void Render(HeadsetReading reading)
        {
            if (_battery == null) return;
            var now = DateTime.Now;
            bool stale = HeadsetPageState.IsStale(reading, now);
            _battery.Text = HeadsetPageState.LevelText(reading, now);
            double max = 252;
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(_bar) as FrameworkElement;
            if (parent != null && parent.ActualWidth > 0) max = parent.ActualWidth;
            _bar.Width = HeadsetPageState.BarWidth(reading, now, max);

            if (reading == null)
            {
                _state.Text = "正在读取";
                _conn.Text = "2.4 GHz 无线接收器 · 正在连接…";
                _power.Text = "--";
                _lastRead.Text = "最后读取 --";
                _status.Text = "正在读取耳机电量…";
                if (_receiver != null) _receiver.Text = "检测中…";
                if (_stateBadge != null)
                {
                    _stateBadge.Background = (Brush)new BrushConverter().ConvertFromString("#1A44D62C");
                    _stateBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#4D44D62C");
                    _state.Foreground = (Brush)new BrushConverter().ConvertFromString("#44D62C");
                }
                return;
            }

            _state.Text = HeadsetPageState.StatusText(reading.Status, reading.ChargingKnown, reading.ExternalPower);
            _power.Text = _state.Text;
            _conn.Text = reading.Status == HeadsetStatus.NoResponse
                ? "2.4 GHz 接收器在线 · 耳机关机或超出范围"
                : "2.4 GHz 无线接收器";
            _lastRead.Text = reading.ReadAt == DateTime.MinValue
                ? (_service.LastGoodAt == DateTime.MinValue ? "最后读取 --" : "上次成功 " + _service.LastGoodAt.ToString("HH:mm:ss"))
                : "最后读取 " + reading.ReadAt.ToString("HH:mm:ss");

            bool unavailable = reading.Status == HeadsetStatus.ReceiverMissing
                || reading.Status == HeadsetStatus.OpenFailed;
            bool noResponse = reading.Status == HeadsetStatus.NoResponse;
            if (_receiver != null)
                _receiver.Text = unavailable ? "未检测到" : (noResponse ? "在线 · 耳机未响应" : "Razer USB 接收器");
            if (_stateBadge != null)
            {
                _stateBadge.Background = (Brush)new BrushConverter().ConvertFromString(
                    unavailable ? "#18FF5C5C" : noResponse ? "#18FFC24D" : "#1A44D62C");
                _stateBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString(
                    unavailable ? "#55FF5C5C" : noResponse ? "#55FFC24D" : "#4D44D62C");
                _state.Foreground = (Brush)new BrushConverter().ConvertFromString(
                    unavailable ? "#FF7777" : noResponse ? "#FFD36A" : "#44D62C");
            }

            if (stale)
            {
                _status.Text = HeadsetPageState.StatusText(reading.Status, reading.ChargingKnown, reading.ExternalPower);
            }
            else
            {
                _status.Text = reading.ChargingKnown
                    ? (reading.ExternalPower ? "已连接充电线" : "电池供电")
                    : "电量已读取 · 充电状态未知";
            }

            if (!string.IsNullOrEmpty(reading.Error) && reading.Status != HeadsetStatus.Ok)
                _status.Text = reading.Error;
        }
    }
}
