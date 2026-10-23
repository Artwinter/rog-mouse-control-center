using System;
using System.Collections.Generic;

namespace RogMouse
{
    public sealed class MouseState
    {
        public int BatteryPercent = -1;
        public int BatteryRaw = -1;
        public bool Charging;
        public int[] DpiX = new int[4];
        public int[] DpiY = new int[4];
        public int DpiPresetCount = 4;
        public int ActivePreset = -1;
        public int ProfileId = -1;
        public int PollingRate = -1;
        public int Debounce = -1;
        public bool AngleSnapping;
        public string FirmwarePrimary = "";
        public string FirmwareSecondary = "";
        public bool Connected;

        /// <summary>True when the DPI table was actually read, not left at its defaults.</summary>
        public bool Valid;
    }

    public sealed class AsusProtocol
    {
        public const ushort CmdGetLed = 0x0312;
        public const ushort CmdGetSettings = 0x0412;
        public const ushort CmdGetButton = 0x0512;
        public const ushort CmdGetProfile = 0x0012;
        public const ushort CmdSetLed = 0x2851;
        public const ushort CmdSetSetting = 0x3151;
        public const ushort CmdSetButton = 0x2151;
        public const ushort CmdSetProfile = 0x0250;
        public const ushort CmdSave = 0x0350;
        public const ushort CmdGetBattery = 0x0712;

        public static readonly int[] PollingRates = { 125, 250, 500, 1000 };
        public static readonly int[] DebounceTimes = { 4, 8, 12, 16, 20, 24, 28, 32 };

        /// <summary>Stages the mouse holds per profile; used to bound the active-stage index.</summary>
        public const int StageCount = 4;

        private readonly AsusHidDevice _dev;
        public AsusProtocol(AsusHidDevice dev) { _dev = dev; }

        private byte[] Q(ushort cmd, params byte[] p)
        {
            return _dev.Query(cmd, p);
        }

        public bool ReadBattery(out int percent, out bool charging, out int raw)
        {
            percent = -1; charging = false; raw = -1;
            var r = Q(CmdGetBattery);
            if (r == null) return false;
            raw = r[5];
            percent = r[4];
            charging = r[5] == 0x01;
            return true;
        }

        /// <summary>
        /// GET_SETTINGS with param 2 (separate X/Y table). Returns false when the mouse
        /// answered with an empty frame instead of a DPI table: a sleeping mouse still
        /// replies, but with every byte zero, and writing those zeros into the state made
        /// the UI show four stages of 0 DPI.
        /// </summary>
        public bool ReadSettings(MouseState st)
        {
            var r = Q(CmdGetSettings, new byte[] { 0x02 });
            if (r == null) return false;

            int count = 4;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                int x = r[4 + i * 4] | (r[5 + i * 4] << 8);
                int y = r[6 + i * 4] | (r[7 + i * 4] << 8);
                int dpiX = x * 50 + 50;
                int dpiY = y * 50 + 50;
                if (x > 0) any = true;
                st.DpiX[i] = dpiX;
                st.DpiY[i] = dpiY;
            }
            if (!any) return false;
            st.DpiPresetCount = count;
            return true;
        }

        /// <summary>
        /// GET_PROFILE. The payload starts at raw[2] (raw[0..1] echo the command), so the
        /// libratbag field indices have to be shifted by 2: profile id is results[8] and the
        /// active DPI preset is results[9]. Reading them at raw[8]/raw[9] instead used to
        /// report preset 6, which clamped to the last stage and made the UI claim 3200 DPI
        /// while the mouse was actually on 1200.
        /// </summary>
        public bool ReadProfile(MouseState st)
        {
            var r = Q(CmdGetProfile, new byte[0]);
            if (r == null) return false;
            // A sleeping mouse still answers this command, but with the profile id and the
            // active-preset byte zeroed (observed frame: 12 00 00 00 01 00 04 00 00 ...).
            // A real reply always names an active preset, so both being zero means "asleep".
            if (r[10] == 0 && r[11] == 0) return false;
            st.ProfileId = r[10];
            st.ActivePreset = r[11] != 0 ? r[11] - 1 : -1;
            // results[4..2] -> raw[6..4] is the stable version block (4.0.1 on this unit);
            // results[13..11] -> raw[15..13] reads 0.9.255, which looks like a placeholder.
            st.FirmwarePrimary = r[6] + "." + r[5] + "." + r[4];
            st.FirmwareSecondary = r[15] + "." + r[14] + "." + r[13];
            return true;
        }

        public bool ReadExtra(MouseState st)
        {
            var r = Q(CmdGetSettings, new byte[] { 0x00 });
            if (r == null) return false;
            // A zero frame carries no settings at all; treating it as "rate 125 Hz,
            // debounce 4 ms, snapping off" would silently rewrite the whole UI.
            if (r[12] == 0 && r[14] == 0 && r[16] == 0) return false;

            int rateIndex = r[12] & 0x07;
            st.PollingRate = rateIndex < PollingRates.Length ? PollingRates[rateIndex] : -1;
            int deb = r[14];
            st.Debounce = deb < DebounceTimes.Length ? DebounceTimes[deb] : -1;
            st.AngleSnapping = r[16] != 0;
            return true;
        }

        public string LastError = "";

        public bool SetDpi(int index, int dpi, bool separateXY)
        {
            if (dpi < 50) dpi = 50;
            if (dpi > 36000) dpi = 36000;
            if (dpi % 50 != 0) dpi = (int)Math.Round(dpi / 50.0) * 50;
            int idpi = (dpi - 50) / 50;
            var r = Q(CmdSetSetting, (byte)index, (byte)0, (byte)(idpi & 0xFF), (byte)((idpi >> 8) & 0xFF));
            if (r == null) { LastError = "set dpi failed"; return false; }
            return true;
        }

        /// <summary>
        /// Switches the active onboard profile (a whole set of four DPI stages), which is a
        /// different thing from picking one of the four stages inside the current profile.
        /// </summary>
        public bool SetProfile(int profileId)
        {
            var r = Q(CmdSetProfile, (byte)profileId);
            if (r == null) { LastError = "set profile failed"; return false; }
            return true;
        }

        /// <summary>
        /// Makes DPI stage <paramref name="index"/> the one the mouse actually uses. This is
        /// SET_SETTING field 9, which libratbag never writes: it reads the same byte through
        /// GET_PROFILE and only uses it to mark which stage is active. The value is the
        /// 1-based stage number, so stage 0 is written as 1. Verified on a Keris Wireless
        /// AimPoint: writing 1..4 moves the active stage and leaves the DPI table intact.
        /// </summary>
        public bool SetActiveStage(int index)
        {
            if (index < 0 || index >= StageCount) return false;
            var r = Q(CmdSetSetting, new byte[] { 9, 0, (byte)(index + 1), 0 });
            if (r == null) { LastError = "set active stage failed"; return false; }
            return true;
        }

        public bool SetPollingRate(int hz)
        {
            int idx = Array.IndexOf(PollingRates, hz);
            if (idx < 0) return false;
            var r = Q(CmdSetSetting, new byte[] { 4, 0, (byte)idx, 0 });
            if (r == null) { LastError = "set rate failed"; return false; }
            return true;
        }

        public bool SetDebounce(int ms)
        {
            int idx = Array.IndexOf(DebounceTimes, ms);
            if (idx < 0) return false;
            var r = Q(CmdSetSetting, new byte[] { 5, 0, (byte)idx, 0 });
            if (r == null) { LastError = "set debounce failed"; return false; }
            return true;
        }

        public bool SetAngleSnapping(bool on)
        {
            var r = Q(CmdSetSetting, new byte[] { 6, 0, (byte)(on ? 1 : 0), 0 });
            if (r == null) { LastError = "set snapping failed"; return false; }
            return true;
        }

        public bool Save()
        {
            var r = Q(CmdSave);
            if (r == null) { LastError = "save failed"; return false; }
            return true;
        }

        /// <summary>Closes the underlying HID handle; the device stays open until now.</summary>
        public void Dispose()
        {
            if (_dev != null) _dev.Dispose();
        }

        public MouseState ReadAll()
        {
            var st = new MouseState();
            int p; bool c; int raw;
            bool batteryOk = ReadBattery(out p, out c, out raw);
            st.BatteryPercent = p;
            st.BatteryRaw = raw;
            st.Charging = c;

            // Each section reports whether it actually got a usable frame, so a sleeping
            // mouse leaves the old values in place instead of blanking the UI with zeros.
            bool settingsOk = ReadSettings(st);
            bool profileOk = ReadProfile(st);
            bool extraOk = ReadExtra(st);
            st.Connected = batteryOk || settingsOk || profileOk || extraOk;
            st.Valid = settingsOk;
            return st;
        }
    }
}