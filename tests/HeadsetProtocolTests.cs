using System;

namespace RogMouse.Tests
{
    internal static class HeadsetProtocolTests
    {
        private static int _passed;
        private static int _failed;

        public static int Main(string[] args)
        {
            Run("BatteryAckParsesPercent", BatteryAckParsesPercent);
            Run("ChargingAckParsesExternalPower", ChargingAckParsesExternalPower);
            Run("UnrelatedOrMalformedFramesAreRejected", UnrelatedOrMalformedFramesAreRejected);
            Run("OutOfRangeBatteryIsRejected", OutOfRangeBatteryIsRejected);
            Run("QueryFramesMatchProtocol", QueryFramesMatchProtocol);
            Run("PacketIsZeroPaddedAndReportIdFirst", PacketIsZeroPaddedAndReportIdFirst);

            Console.WriteLine(_failed == 0
                ? "RESULT: " + _passed + " passed"
                : "RESULT: " + _failed + " failed, " + _passed + " passed");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + ": " + ex.Message);
            }
        }

        private static void Equal(int expected, int actual, string what)
        {
            if (expected != actual)
                throw new Exception(what + " expected " + expected + ", got " + actual);
        }

        private static void Equal(bool expected, bool actual, string what)
        {
            if (expected != actual)
                throw new Exception(what + " expected " + expected + ", got " + actual);
        }

        private static void EqualBytes(byte[] expected, byte[] actual, string what)
        {
            if (actual == null) throw new Exception(what + " was null");
            if (expected.Length > actual.Length)
                throw new Exception(what + " length expected at least " + expected.Length + ", got " + actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                    throw new Exception(what + " differs at byte " + i
                        + ": expected 0x" + expected[i].ToString("X2")
                        + ", got 0x" + actual[i].ToString("X2"));
            }
        }

        private static byte[] Reply(byte command, byte length, params byte[] payload)
        {
            var b = new byte[RazerBlackSharkProtocol.PacketSize];
            b[0] = 0x02;
            b[1] = 0x0E;
            b[2] = 0x50;
            b[3] = 0x49;
            b[12] = command;
            b[13] = 0x01;
            b[14] = length;
            for (int i = 0; i < payload.Length; i++) b[15 + i] = payload[i];
            return b;
        }

        private static void BatteryAckParsesPercent()
        {
            int value;
            Equal(true, RazerBlackSharkProtocol.TryReadBattery(Reply(0x21, 1, 87), out value),
                "battery ACK accepted");
            Equal(87, value, "battery value");
        }

        private static void ChargingAckParsesExternalPower()
        {
            bool value;
            Equal(true, RazerBlackSharkProtocol.TryReadExternalPower(Reply(0x2A, 1, 2), out value),
                "charging ACK accepted");
            Equal(true, value, "external power");

            Equal(true, RazerBlackSharkProtocol.TryReadExternalPower(Reply(0x2A, 1, 0), out value),
                "on-battery ACK accepted");
            Equal(false, value, "on battery");
        }

        private static void UnrelatedOrMalformedFramesAreRejected()
        {
            int value;
            var wrongId = Reply(0x21, 1, 87);
            wrongId[0] = 0x01;
            Equal(false, RazerBlackSharkProtocol.TryReadBattery(wrongId, out value), "wrong report id");

            var wrongCommand = Reply(0x2A, 1, 87);
            Equal(false, RazerBlackSharkProtocol.TryReadBattery(wrongCommand, out value), "wrong command");

            var wrongAck = Reply(0x21, 1, 87);
            wrongAck[13] = 0x00;
            Equal(false, RazerBlackSharkProtocol.TryReadBattery(wrongAck, out value), "wrong ACK");

            var zeroLength = Reply(0x21, 0, 87);
            Equal(false, RazerBlackSharkProtocol.TryReadBattery(zeroLength, out value), "zero payload length");

            var tooLong = Reply(0x21, 17, 87);
            Equal(false, RazerBlackSharkProtocol.TryReadBattery(tooLong, out value), "oversized payload length");

            Equal(false, RazerBlackSharkProtocol.TryReadBattery(new byte[10], out value), "short report");
        }

        private static void OutOfRangeBatteryIsRejected()
        {
            int value;
            Equal(false, RazerBlackSharkProtocol.TryReadBattery(Reply(0x21, 1, 101), out value), "101 rejected");
            Equal(false, RazerBlackSharkProtocol.TryReadBattery(Reply(0x21, 1, 255), out value), "255 rejected");
        }

        private static void QueryFramesMatchProtocol()
        {
            EqualBytes(new byte[] {
                0x02, 0x80, 0x07, 0x00, 0x00, 0x50, 0x41, 0x0E,
                0x00, 0x02, 0xE1, 0x01
            }, RazerBlackSharkProtocol.BuildRemote(true), "remote on");

            EqualBytes(new byte[] {
                0x02, 0x80, 0x07, 0x00, 0x00, 0x50, 0x41, 0x0E,
                0x00, 0x02, 0xE1, 0x00
            }, RazerBlackSharkProtocol.BuildRemote(false), "remote off");

            EqualBytes(new byte[] {
                0x02, 0x80, 0x08, 0x00, 0x00, 0x50, 0x41, 0x08,
                0x00, 0x03, 0x21, 0x00
            }, RazerBlackSharkProtocol.BuildQuery(0x21), "battery query");

            EqualBytes(new byte[] {
                0x02, 0x80, 0x08, 0x00, 0x00, 0x50, 0x41, 0x08,
                0x00, 0x03, 0x2A, 0x00
            }, RazerBlackSharkProtocol.BuildQuery(0x2A), "charging query");
        }

        private static void PacketIsZeroPaddedAndReportIdFirst()
        {
            var frames = new[] {
                RazerBlackSharkProtocol.BuildRemote(true),
                RazerBlackSharkProtocol.BuildRemote(false),
                RazerBlackSharkProtocol.BuildQuery(0x21),
                RazerBlackSharkProtocol.BuildQuery(0x2A)
            };
            foreach (var f in frames)
            {
                Equal(64, f.Length, "packet size");
                Equal(0x02, f[0], "report id");
                for (int i = 12; i < f.Length; i++)
                    Equal(0, f[i], "padding at " + i);
            }
        }
    }
}
