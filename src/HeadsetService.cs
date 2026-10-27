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
        public string LastError { get; private set; }

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
        public HeadsetReading ReadOnce()
        {
            lock (_sync)
            {
                var result = new HeadsetReading();
                string error;
                IHeadsetDevice device = null;
                try
                {
                    device = _open(out error);
                    if (device == null)
                    {
                        result.Error = error ?? "";
                        result.Status = IsMissing(error) ? HeadsetStatus.ReceiverMissing
                            : HeadsetStatus.OpenFailed;
                        LastError = result.Error;
                        return result;
                    }

                    byte[] battery = device.Query(RazerBlackSharkProtocol.CommandBattery, out error);
                    if (battery == null)
                    {
                        result.Status = HeadsetStatus.NoResponse;
                        result.Error = error ?? "耳机无响应";
                        LastError = result.Error;
                        return result;
                    }
                    if (battery.Length < 1 || battery[0] > 100)
                    {
                        result.Status = HeadsetStatus.InvalidReply;
                        result.Error = "耳机电量回包格式无效";
                        LastError = result.Error;
                        return result;
                    }

                    result.BatteryPercent = battery[0];
                    result.Status = HeadsetStatus.Ok;
                    result.ReadAt = DateTime.Now;

                    byte[] charging = device.Query(RazerBlackSharkProtocol.CommandCharging, out error);
                    if (charging != null && charging.Length >= 1)
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
                    return result;
                }
                catch (Exception ex)
                {
                    result.Status = HeadsetStatus.OpenFailed;
                    result.Error = "读取耳机异常：" + ex.GetType().Name;
                    LastError = result.Error;
                    return result;
                }
                finally
                {
                    if (device != null) { try { device.Dispose(); } catch { } }
                }
            }
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
