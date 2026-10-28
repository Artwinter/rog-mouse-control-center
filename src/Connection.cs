using System;
using System.Collections.Generic;

namespace RogMouse
{
    /// <summary>
    /// How the mouse is attached, derived from USB descriptor enumeration only.
    /// Enumeration never talks to the mouse, so this keeps working while the mouse is
    /// asleep and the protocol layer is timing out.
    /// </summary>
    public sealed class ConnectionInfo
    {
        public const ushort PidReceiver = 0x1A68;
        public const ushort PidBluetooth = 0x1A66;

        public bool Receiver;
        public bool Bluetooth;
        public bool Wired;
        public int Count;

        /// <summary>True when a cabled vendor collection proves USB power is present.</summary>
        public bool UsbPowered { get { return Wired; } }

        /// <summary>Detailed transport label shown next to the status dot.</summary>
        public string Transport
        {
            get
            {
                if (Bluetooth && (Receiver || Wired)) return "2.4 GHz + 蓝牙";
                if (Bluetooth) return "蓝牙无线";
                if (Receiver && Wired) return "2.4 GHz 接收器 + USB";
                if (Receiver) return "2.4 GHz 接收器";
                if (Wired) return "USB 有线";
                return "未检测到设备";
            }
        }

        /// <summary>Short transport label for the spec list (the card is only 274 px wide).</summary>
        public string TransportShort
        {
            get
            {
                if (Bluetooth && (Receiver || Wired)) return "2.4G + 蓝牙";
                if (Bluetooth) return "蓝牙无线";
                if (Receiver) return "2.4G 无线";
                if (Wired) return "USB 有线";
                return "--";
            }
        }

        /// <summary>
        /// ASCII transport tag for the small uppercase header label, which is set in
        /// Bahnschrift and must not mix in CJK glyphs.
        /// </summary>
        public string TransportCode
        {
            get
            {
                if (Bluetooth && (Receiver || Wired)) return "2.4G + BT";
                if (Bluetooth) return "BT";
                if (Receiver && Wired) return "2.4G + USB";
                if (Receiver) return "2.4 GHZ";
                if (Wired) return "USB";
                return "LINK";
            }
        }

        /// <summary>
        /// Power label. The 0x0712 battery frame carries no charging flag (verified on the
        /// device: no parameter variant and no sibling command exposes one), so the only
        /// honest signal is whether a cabled collection is enumerated.
        /// </summary>
        public string Power
        {
            get
            {
                if (Wired) return "USB 供电";
                if (Receiver || Bluetooth) return "电池供电";
                return "--";
            }
        }

        public static ConnectionInfo Detect()
        {
            var info = new ConnectionInfo();
            List<AsusInterface> all = null;
            try { all = AsusHidDevice.EnumerateVendorInterfaces(); }
            catch { }
            if (all == null || all.Count == 0) return info;

            // An ASUS keyboard exposes a 0xFF01 collection too; when the mouse is present
            // only its own collections may be counted, otherwise everything is off.
            var mine = new List<AsusInterface>();
            for (int i = 0; i < all.Count; i++)
            {
                string name = all[i].ProductName;
                if (name != null && name.ToUpperInvariant().IndexOf("KERIS", StringComparison.Ordinal) >= 0)
                    mine.Add(all[i]);
            }
            if (mine.Count == 0) mine = all;

            info.Count = mine.Count;
            for (int i = 0; i < mine.Count; i++)
            {
                ushort pid = mine[i].ProductId;
                string path = (mine[i].Path == null ? "" : mine[i].Path).ToLowerInvariant();
                bool bluetooth = pid == PidBluetooth
                    || path.IndexOf("bth", StringComparison.Ordinal) >= 0
                    || path.IndexOf("bluetooth", StringComparison.Ordinal) >= 0;
                if (bluetooth) info.Bluetooth = true;
                else if (pid == PidReceiver) info.Receiver = true;
                else info.Wired = true;
            }
            return info;
        }
    }
}
