using System;

namespace RogMouse
{
    public enum HeadsetStatus
    {
        ReceiverMissing,
        OpenFailed,
        NoResponse,
        InvalidReply,
        Ok
    }

    public sealed class HeadsetReading
    {
        public int BatteryPercent = -1;
        public bool ExternalPower;
        public bool ChargingKnown;
        public DateTime ReadAt = DateTime.MinValue;
        public HeadsetStatus Status = HeadsetStatus.ReceiverMissing;
        public string Error = "";
    }

    /// <summary>
    /// Minimal transport seam, so state mapping can be tested without hardware. It exposes
    /// no write path to UI code.
    /// </summary>
    public interface IHeadsetDevice : IDisposable
    {
        byte[] Query(byte command, out string error);
    }

    public sealed class RazerHeadsetDeviceAdapter : IHeadsetDevice
    {
        private readonly RazerHidDevice _device;
        public RazerHeadsetDeviceAdapter(RazerHidDevice device) { _device = device; }

        public byte[] Query(byte command, out string error)
        {
            return _device.Query(command, 900, out error);
        }

        public void Dispose() { _device.Dispose(); }
    }

    public sealed class HeadsetService
    {
        public delegate IHeadsetDevice OpenDevice(out string error);

        private readonly OpenDevice _open;
        private readonly object _sync = new object();
        private HeadsetReading _lastGood;

        public HeadsetService(OpenDevice open) { _open = open; }
        public Action<string> Log;
        public string LastError { get; private set; }
        public DateTime LastGoodAt { get; private set; }

        public static HeadsetService CreateDefault()
        {
            return new HeadsetService(delegate(out string error)
            {
                var device = RazerHidDevice.OpenFirst(RazerHidDevice.RazerVendorId,
                    RazerHidDevice.BlackSharkV2ProPid, RazerHidDevice.ProductNeedle, out error);
                return device == null ? null : new RazerHeadsetDeviceAdapter(device);
            });
        }

        /// <summary>
        /// Opens a fresh collection, reads the two permitted values, and releases the handle on
        /// every path. A missing ACK is not an empty battery: it maps to NoResponse and leaves
        /// BatteryPercent at -1.
        /// </summary>
        public HeadsetReading ReadOnce(bool allowAccess)
        {
            lock (_sync)
            {
                var result = new HeadsetReading();
                if (!allowAccess)
                {
                    result.Status = HeadsetStatus.NoResponse;
                    result.Error = "--no-device：未访问耳机 HID";
                    return result;
                }
                string error;
                IHeadsetDevice device = null;
                try
                {
                    device = _open(out error);
                    if (device == null)
                    {
                        result.Error = error ?? "";
                        if (result.Error.IndexOf("Win32", StringComparison.Ordinal) >= 0 && Log != null)
                            Log("headset HID unavailable: " + result.Error);
                        result.Status = IsMissing(error) ? HeadsetStatus.ReceiverMissing
                            : HeadsetStatus.OpenFailed;
                        LastError = result.Error;
                        if (Log != null) Log("headset open failed: " + result.Error);
                        return result;
                    }

                    byte[] battery = device.Query(RazerBlackSharkProtocol.CommandBattery, out error);
                    if (battery == null)
                    {
                        result.Status = HeadsetStatus.NoResponse;
                        result.Error = error ?? "耳机无响应";
                        LastError = result.Error;
                        if (Log != null) Log("headset no response: " + result.Error);
                        return result;
                    }
                    if (battery.Length != 1 || battery[0] > 100)
                    {
                        result.Status = HeadsetStatus.InvalidReply;
                        result.Error = "耳机电量回包格式无效";
                        LastError = result.Error;
                        if (Log != null) Log("headset invalid battery payload: " + battery[0]);
                        return result;
                    }

                    result.BatteryPercent = battery[0];
                    result.Status = HeadsetStatus.Ok;
                    result.ReadAt = DateTime.Now;

                    byte[] charging = device.Query(RazerBlackSharkProtocol.CommandCharging, out error);
                    if (charging != null && charging.Length == 1)
                    {
                        result.ChargingKnown = true;
                        result.ExternalPower = charging[0] != 0;
                    }
                    else
                    {
                        result.ChargingKnown = false;
                        LastError = error ?? "充电状态未知";
                    }

                    _lastGood = result;
                    LastGoodAt = result.ReadAt;
                    return result;
                }
                catch (Exception ex)
                {
                    result.Status = HeadsetStatus.OpenFailed;
                    result.Error = "读取耳机异常：" + ex.GetType().Name;
                    LastError = result.Error;
                    if (Log != null) Log("headset exception: " + ex.GetType().Name + ": " + ex.Message);
                    return result;
                }
                finally
                {
                    if (device != null) { try { device.Dispose(); } catch { } }
                }
            }
        }

        public HeadsetReading ReadOnce()
        {
            return ReadOnce(true);
        }

        public HeadsetReading LastGood
        {
            get { lock (_sync) { return _lastGood; } }
        }

        private static bool IsMissing(string error)
        {
            return error != null
                && error.IndexOf("未找到", StringComparison.Ordinal) >= 0;
        }
    }
}
