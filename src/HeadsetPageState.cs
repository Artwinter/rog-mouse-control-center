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

        /// <summary>
        /// Power row on the headset page. It must answer only "how is the cell being fed", so a
        /// link problem is reported as no data instead of repeating the receiver sentence that
        /// the row above already shows.
        /// </summary>
        public static string PowerText(HeadsetReading reading)
        {
            if (reading == null) return "--";
            if (reading.Status != HeadsetStatus.Ok) return "无数据";
            if (!reading.ChargingKnown) return "充电状态未知";
            return reading.ExternalPower ? "已连接充电线" : "电池供电";
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

        /// <summary>
        /// ASCII link tag for the sidebar row, which is drawn in Bahnschrift and must stay
        /// in the same uppercase code style as the rest of the deck.
        /// </summary>
        public static string LinkCode(HeadsetReading reading)
        {
            if (reading == null) return "STANDBY";
            if (reading.Status == HeadsetStatus.Ok) return "LINK STABLE";
            if (reading.Status == HeadsetStatus.NoResponse) return "NO RESPONSE";
            if (reading.Status == HeadsetStatus.ReceiverMissing) return "NO RECEIVER";
            if (reading.Status == HeadsetStatus.OpenFailed) return "RECEIVER BUSY";
            return "BAD REPLY";
        }

        /// <summary>Foreground for <see cref="LinkCode"/>: green steady, amber degraded.</summary>
        public static string LinkColor(HeadsetReading reading)
        {
            if (reading == null) return "#5B6561";
            if (reading.Status == HeadsetStatus.Ok) return "#79FF5A";
            if (reading.Status == HeadsetStatus.NoResponse) return "#FFBD62";
            return "#5B6561";
        }
    }
}
