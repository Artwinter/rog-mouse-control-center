using System;

namespace RogMouse.Tests
{
    internal sealed class FakeHeadsetDevice : IHeadsetDevice
    {
        public string Error;
        public byte[] BatteryPayload;
        public byte[] ChargingPayload;
        public int Disposed;

        public byte[] Query(byte command, out string error)
        {
            error = null;
            return command == RazerBlackSharkProtocol.CommandBattery ? BatteryPayload : ChargingPayload;
        }

        public void Dispose() { Disposed++; }
    }

    internal static class HeadsetServiceTests
    {
        public static int RunAll()
        {
            int passed = 0;
            int failed = 0;
            Run("ReceiverMissingHasNoBattery", ReceiverMissingHasNoBattery, ref passed, ref failed);
            Run("NoBatteryAckIsNoResponse", NoBatteryAckIsNoResponse, ref passed, ref failed);
            Run("GoodBatteryAndPowerAreReported", GoodBatteryAndPowerAreReported, ref passed, ref failed);
            Run("MissingChargingReplyIsUnknown", MissingChargingReplyIsUnknown, ref passed, ref failed);
            Run("MalformedBatteryIsInvalid", MalformedBatteryIsInvalid, ref passed, ref failed);
            Run("ReceiverOpenErrorIsReported", ReceiverOpenErrorIsReported, ref passed, ref failed);
            Console.WriteLine("RESULT: " + (failed == 0 ? passed + " passed" : failed + " failed, " + passed + " passed"));
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test, ref int passed, ref int failed)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }

        private static void Equal(object expected, object actual, string what)
        {
            if (!object.Equals(expected, actual))
                throw new Exception(what + " expected " + expected + ", got " + actual);
        }

        private static HeadsetService Service(FakeHeadsetDevice fake)
        {
            return new HeadsetService(delegate(out string error)
            {
                error = null;
                return fake;
            });
        }

        private static HeadsetService MissingService()
        {
            return new HeadsetService(delegate(out string error)
            {
                error = "未找到 Razer BlackShark V2 Pro 2.4 GHz 接收器";
                return null;
            });
        }

        private static void ReceiverMissingHasNoBattery()
        {
            var r = MissingService().ReadOnce();
            Equal(HeadsetStatus.ReceiverMissing, r.Status, "status");
            Equal(-1, r.BatteryPercent, "battery");
        }

        private static void NoBatteryAckIsNoResponse()
        {
            var fake = new FakeHeadsetDevice();
            fake.BatteryPayload = null;
            var r = Service(fake).ReadOnce();
            Equal(HeadsetStatus.NoResponse, r.Status, "status");
            Equal(-1, r.BatteryPercent, "battery");
            Equal(1, fake.Disposed, "device disposed");
        }

        private static void GoodBatteryAndPowerAreReported()
        {
            var fake = new FakeHeadsetDevice();
            fake.BatteryPayload = new byte[] { 87 };
            fake.ChargingPayload = new byte[] { 2 };
            var r = Service(fake).ReadOnce();
            Equal(HeadsetStatus.Ok, r.Status, "status");
            Equal(87, r.BatteryPercent, "battery");
            Equal(true, r.ExternalPower, "external power");
            Equal(true, r.ChargingKnown, "charging known");
        }

        private static void MissingChargingReplyIsUnknown()
        {
            var fake = new FakeHeadsetDevice();
            fake.BatteryPayload = new byte[] { 45 };
            fake.ChargingPayload = null;
            var r = Service(fake).ReadOnce();
            Equal(HeadsetStatus.Ok, r.Status, "status");
            Equal(45, r.BatteryPercent, "battery");
            Equal(false, r.ChargingKnown, "charging known");
        }

        private static void MalformedBatteryIsInvalid()
        {
            var fake = new FakeHeadsetDevice();
            fake.BatteryPayload = new byte[0];
            var r = Service(fake).ReadOnce();
            Equal(HeadsetStatus.InvalidReply, r.Status, "status");
            Equal(-1, r.BatteryPercent, "battery");
        }

        private static void ReceiverOpenErrorIsReported()
        {
            var service = new HeadsetService(delegate(out string error)
            {
                error = "无法打开耳机控制集合 (Win32 5)";
                return null;
            });
            var r = service.ReadOnce();
            Equal(HeadsetStatus.OpenFailed, r.Status, "status");
            if (r.Error == null || r.Error.IndexOf("Win32 5") < 0) throw new Exception("Win32 error lost");
        }
    }
}
