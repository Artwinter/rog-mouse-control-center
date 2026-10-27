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
        private TextBlock _battery;
        private Border _bar;
        private TextBlock _state;
        private TextBlock _conn;
        private TextBlock _power;
        private TextBlock _lastRead;
        private TextBlock _status;
        private Button _refresh;
        private DispatcherTimer _timer;
        private bool _busy;
        private bool _pageSelected;
        private bool _suspended;
        private HeadsetReading _last;

        public HeadsetController(Window win, HeadsetService service)
        {
            _win = win;
            _service = service;
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
            _refresh = (Button)_win.FindName("BtnHeadsetRefresh");
            if (_refresh != null) _refresh.Click += delegate { Refresh(); };

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(60);
            _timer.Tick += delegate { Refresh(); };
            Render(null);
        }

        public bool PageSelected { get { return _pageSelected; } }

        public void SelectPage(bool selected)
        {
            _pageSelected = selected;
            if (selected)
            {
                SyncTimer(true);
                Refresh();
            }
            else
            {
                SyncTimer(false);
            }
            Render(_last);
        }

        /// <summary>
        /// Stops background traffic while parked or while a fullscreen app has focus. The
        /// app-level suspension gate remains the source of truth; this method only mirrors it.
        /// </summary>
        public void SyncAccess(bool suspended)
        {
            _suspended = suspended;
            SyncTimer(!suspended && _pageSelected);
            if (!suspended && _pageSelected && _last == null) Refresh();
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

        private void SyncTimer(bool enabled)
        {
            if (_timer == null) return;
            if (enabled) { if (!_timer.IsEnabled) _timer.Start(); }
            else _timer.Stop();
        }

        public void Refresh()
        {
            if (_busy || !_pageSelected || !HeadsetPageState.CanPoll(true, _win.IsVisible,
                    _win.WindowState == WindowState.Minimized, _suspended)) return;
            _busy = true;
            if (_refresh != null) _refresh.IsEnabled = false;
            if (_status != null && _last == null) _status.Text = "正在读取耳机电量…";

            Task.Factory.StartNew(delegate { return _service.ReadOnce(); })
                .ContinueWith(delegate(Task<HeadsetReading> t)
                {
                    var reading = t.Result;
                    _win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _last = reading;
                        Render(reading);
                        _busy = false;
                        if (_refresh != null) _refresh.IsEnabled = true;
                    }));
                });
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
                return;
            }

            _state.Text = HeadsetPageState.StatusText(reading.Status, reading.ChargingKnown, reading.ExternalPower);
            _power.Text = _state.Text;
            _conn.Text = reading.Status == HeadsetStatus.NoResponse
                ? "2.4 GHz 接收器在线 · 耳机关机或超出范围"
                : "2.4 GHz 无线接收器";
            _lastRead.Text = reading.ReadAt == DateTime.MinValue
                ? "最后读取 --"
                : "最后读取 " + reading.ReadAt.ToString("HH:mm:ss");

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
