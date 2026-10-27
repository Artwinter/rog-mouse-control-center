using System;

namespace RogMouse
{
    /// <summary>
    /// Razer BlackShark V2 Pro (2023) "PA" frames. This headset does not speak the
    /// 90-byte Razer mouse protocol: its receiver uses 64-byte output/input reports on
    /// the 0xFF00 vendor collection, with the command echoed at byte 12.
    /// </summary>
    public static class RazerBlackSharkProtocol
    {
        public const int PacketSize = 64;
        public const byte ReportId = 0x02;
        public const byte CommandBattery = 0x21;
        public const byte CommandCharging = 0x2A;

        private const int ReplyCommandOffset = 12;
        private const int ReplyPayloadOffset = 15;

        public static byte[] BuildRemote(bool on)
        {
            var b = new byte[PacketSize];
            b[0] = ReportId;
            b[1] = 0x80;
            b[2] = 0x07;
            b[5] = 0x50;
            b[6] = 0x41;
            b[7] = 0x0E;
            b[9] = 0x02;
            b[10] = 0xE1;
            b[11] = on ? (byte)0x01 : (byte)0x00;
            return b;
        }

        public static byte[] BuildQuery(byte command)
        {
            var b = new byte[PacketSize];
            b[0] = ReportId;
            b[1] = 0x80;
            b[2] = 0x08;
            b[5] = 0x50;
            b[6] = 0x41;
            b[7] = 0x08;
            b[9] = 0x03;
            b[10] = command;
            return b;
        }

        /// <summary>
        /// Returns the payload only for an ACKed reply to this command. A sleeping headset can
        /// produce status frames and unrelated reports; none of them are allowed to look like a
        /// reading.
        /// </summary>
        public static bool TryReadPayload(byte[] report, byte command, out byte[] payload)
        {
            payload = null;
            if (report == null || report.Length <= ReplyPayloadOffset) return false;
            if (report[0] != ReportId) return false;
            if (report[2] != 0x50 || report[3] != 0x49) return false;
            if (report[ReplyCommandOffset] != command) return false;
            if (report[ReplyCommandOffset + 1] != 0x01) return false;

            int length = report[ReplyCommandOffset + 2];
            if (length < 1 || length > 16) return false;
            if (replyEnd(report) < ReplyPayloadOffset + length) return false;

            var result = new byte[length];
            Array.Copy(report, ReplyPayloadOffset, result, 0, length);
            payload = result;
            return true;
        }

        public static bool TryReadBattery(byte[] report, out int percent)
        {
            percent = -1;
            byte[] payload;
            if (!TryReadPayload(report, CommandBattery, out payload)) return false;
            if (payload[0] > 100) return false;
            percent = payload[0];
            return true;
        }

        public static bool TryReadExternalPower(byte[] report, out bool connected)
        {
            connected = false;
            byte[] payload;
            if (!TryReadPayload(report, CommandCharging, out payload)) return false;
            connected = payload[0] != 0;
            return true;
        }

        private static int replyEnd(byte[] report)
        {
            return report.Length;
        }
    }
}
