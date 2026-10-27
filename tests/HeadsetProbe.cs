using System;
using System.Collections.Generic;

namespace RogMouse.Tests
{
    internal static class HeadsetProbe
    {
        public static int Main(string[] args)
        {
            var all = RazerHidDevice.Enumerate();
            Console.WriteLine("HID_COLLECTIONS=" + all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it.VendorId == RazerHidDevice.RazerVendorId)
                {
                    Console.WriteLine("RAZER " + it.VendorId.ToString("X4") + ":"
                        + it.ProductId.ToString("X4") + " usage="
                        + it.UsagePage.ToString("X4") + ":" + it.Usage.ToString("X4")
                        + " in=" + it.InputLength + " out=" + it.OutputLength
                        + " product=" + it.ProductName);
                }
            }

            string error;
            var dev = RazerHidDevice.OpenFirst(RazerHidDevice.RazerVendorId,
                RazerHidDevice.BlackSharkV2ProPid, RazerHidDevice.ProductNeedle, out error);
            if (dev == null)
            {
                Console.WriteLine("OPEN=FAILED " + error);
                return 2;
            }

            try
            {
                Console.WriteLine("OPEN=OK " + dev.DescribeCollection());
                Console.WriteLine("PATH=" + dev.Path);
                Console.Out.Flush();
                byte[] battery = dev.Query(RazerBlackSharkProtocol.CommandBattery, 2500, out error);
                Console.WriteLine("BATTERY=" + Hex(battery) + " ERROR=" + (error ?? ""));
                Console.Out.Flush();
                byte[] charging = dev.Query(RazerBlackSharkProtocol.CommandCharging, 2500, out error);
                Console.WriteLine("CHARGING=" + Hex(charging) + " ERROR=" + (error ?? ""));
                Console.Out.Flush();
                int percent;
                Console.WriteLine("PARSED_BATTERY=" + (battery != null && battery.Length > 0
                    ? battery[0].ToString() : "none"));
                Console.Out.Flush();
                return battery == null ? 3 : 0;
            }
            finally { dev.Dispose(); }
        }

        private static byte[] RawReply(byte[] payload)
        {
            var b = new byte[RazerBlackSharkProtocol.PacketSize];
            b[0] = 0x02;
            b[1] = 0x0E;
            b[2] = 0x50;
            b[3] = 0x49;
            b[12] = RazerBlackSharkProtocol.CommandBattery;
            b[13] = 0x01;
            b[14] = (byte)Math.Min(payload.Length, 16);
            Array.Copy(payload, 0, b, 15, Math.Min(payload.Length, 16));
            return b;
        }

        private static string Hex(byte[] bytes)
        {
            if (bytes == null) return "null";
            var parts = new List<string>();
            for (int i = 0; i < bytes.Length; i++) parts.Add(bytes[i].ToString("X2"));
            return string.Join(" ", parts.ToArray());
        }
    }
}
