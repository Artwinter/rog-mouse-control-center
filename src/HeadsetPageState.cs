using System;

namespace RogMouse
{
    /// <summary>
    /// Pure display/polling decisions for the headset page, kept separate from WPF so the
    /// failure states can be tested without a receiver attached.
    /// </summary>
    public static class HeadsetPageState
    {
        public static bool CanPoll(bool selected, bool visible, bool minimized, bool fullscreen)
        {
            return selected && visible && !minimized && !fullscreen;
        }

        public static string StatusText(HeadsetStatus status, bool chargingKnown, bool externalPower)
        {
            if (status == HeadsetStatus.ReceiverMissing) return "未检测到 2.4 GHz 接收器";
            if (status == HeadsetStatus.OpenFailed) return "接收器被占用或无法打开";
            if (status == HeadsetStatus.NoResponse) return "接收器在线 · 耳机未响应";
            if (status == HeadsetStatus.InvalidReply) return "耳机回包格式无效";
            if (!chargingKnown) return "充电状态未知";
            return externalPower ? "已连接充电线" : "电池供电";
        }

        public static bool IsStale(HeadsetReading reading, DateTime now)
        {
            if (reading == null || reading.Status != HeadsetStatus.Ok) return true;
            return (now - reading.ReadAt).TotalSeconds > 90;
        }

        public static string LevelText(HeadsetReading reading, DateTime now)
        {
            if (IsStale(reading, now)) return "--";
            return reading.BatteryPercent.ToString();
        }

        public static double BarWidth(HeadsetReading reading, DateTime now, double maxWidth)
        {
            if (IsStale(reading, now)) return 0;
            return maxWidth * Math.Max(0, Math.Min(100, reading.BatteryPercent)) / 100.0;
        }
    }
}
