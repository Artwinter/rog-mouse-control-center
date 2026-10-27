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
            Run("CapturedBatteryAckParses77", CapturedBatteryAckParses77);
            Run("ChargingAckParsesExternalPower", ChargingAckParsesExternalPower);
            Run("UnrelatedOrMalformedFramesAreRejected", UnrelatedOrMalformedFramesAreRejected);
            Run("OutOfRangeBatteryIsRejected", OutOfRangeBatteryIsRejected);
            Run("QueryFramesMatchProtocol", QueryFramesMatchProtocol);
            Run("PacketIsZeroPaddedAndReportIdFirst", PacketIsZeroPaddedAndReportIdFirst);
            Run("SelectsOnlyBlackSharkV2ProVendorCollection", SelectsOnlyBlackSharkV2ProVendorCollection);
            Run("SelectionRejectsWrongProductAndUsage", SelectionRejectsWrongProductAndUsage);
            Run("SelectionReturnsNullWithoutCandidate", SelectionReturnsNullWithoutCandidate);
            Run("OpenRejectsCollectionWithoutRawPacketLengths", OpenRejectsCollectionWithoutRawPacketLengths);

            int serviceResult = HeadsetServiceTests.RunAll();
            if (serviceResult != 0) _failed++;

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

        private static void Equal(object expected, object actual, string what)
        {
            if (!object.Equals(expected, actual))
                throw new Exception(what + " expected " + (expected ?? "null") + ", got " + (actual ?? "null"));
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

        private static void CapturedBatteryAckParses77()
        {
            // Real 2.4 GHz receiver reply captured on this machine at 77%.
            var raw = new byte[64];
            byte[] prefix = {
                0x02, 0x0E, 0x50, 0x49, 0x08, 0xC5, 0xFB, 0x9A, 0x14, 0x00,
                0x04, 0x00, 0x21, 0x01, 0x01, 0x4D
            };
            Array.Copy(prefix, raw, prefix.Length);
            int value;
            Equal(true, RazerBlackSharkProtocol.TryReadBattery(raw, out value), "captured ACK accepted");
            Equal(77, value, "captured battery value");
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

        private static RazerHidInterface Candidate(ushort vid, ushort pid, string name, ushort usagePage)
        {
            var item = new RazerHidInterface();
            item.VendorId = vid;
            item.ProductId = pid;
            item.ProductName = name;
            item.UsagePage = usagePage;
            item.Usage = 1;
            item.Path = "path";
            return item;
        }

        private static void SelectsOnlyBlackSharkV2ProVendorCollection()
        {
            var mouse = Candidate(0x1532, 0x0084, "Razer DeathAdder V2 Pro", 0xFF00);
            var headsetWrongPage = Candidate(0x1532, 0x0555, "Razer BlackShark V2 Pro 2.4", 0x0001);
            var headset = Candidate(0x1532, 0x0555, "Razer BlackShark V2 Pro 2.4", 0xFF00);
            var selected = RazerHidDevice.Select(new[] { mouse, headsetWrongPage, headset },
                0x1532, 0x0555, "BlackShark V2 Pro", 0xFF00);
            if (selected != headset) throw new Exception("selected the wrong HID collection");
        }

        private static void SelectionRejectsWrongProductAndUsage()
        {
            var wrongProduct = Candidate(0x1532, 0x0555, "Razer Barracuda Pro", 0xFF00);
            var wrongUsage = Candidate(0x1532, 0x0555, "Razer BlackShark V2 Pro", 0x0001);
            Equal(null, RazerHidDevice.Select(new[] { wrongProduct }, 0x1532, 0x0555,
                "BlackShark V2 Pro", 0xFF00), "wrong product");
            Equal(null, RazerHidDevice.Select(new[] { wrongUsage }, 0x1532, 0x0555,
                "BlackShark V2 Pro", 0xFF00), "wrong usage page");
        }

        private static void SelectionReturnsNullWithoutCandidate()
        {
            Equal(null, RazerHidDevice.Select(new RazerHidInterface[0], 0x1532, 0x0555,
                "BlackShark V2 Pro", 0xFF00), "empty collection");
        }

        private static void OpenRejectsCollectionWithoutRawPacketLengths()
        {
            var item = Candidate(0x1532, 0x0555, "Razer BlackShark V2 Pro 2.4", 0xFF00);
            item.InputLength = 0;
            item.OutputLength = 0;
            Equal(false, RazerHidDevice.HasProtocolLengths(item), "zero report lengths");
            item.InputLength = 64;
            item.OutputLength = 64;
            Equal(true, RazerHidDevice.HasProtocolLengths(item), "64-byte report lengths");
        }
    }
}
