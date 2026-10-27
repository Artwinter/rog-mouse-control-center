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
            Run("HeadsetPagePollingGate", HeadsetPagePollingGate, ref passed, ref failed);
            Run("HeadsetPageStateText", HeadsetPageStateText, ref passed, ref failed);
            Run("HeadsetPageStaleReading", HeadsetPageStaleReading, ref passed, ref failed);
            Run("FreshReadingReplacesExpiredValue", FreshReadingReplacesExpiredValue, ref passed, ref failed);
            Run("WindowFitStaysInsideWorkArea", WindowFitStaysInsideWorkArea, ref passed, ref failed);
            Run("HeadsetTransactionHasBoundedRetryAndRemoteOff", HeadsetTransactionHasBoundedRetryAndRemoteOff, ref passed, ref failed);
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

        private static void HeadsetPagePollingGate()
        {
            Equal(true, HeadsetPageState.CanPoll(true, true, false, false), "visible selected");
            Equal(false, HeadsetPageState.CanPoll(false, true, false, false), "mouse page");
            Equal(false, HeadsetPageState.CanPoll(true, false, false, false), "hidden");
            Equal(false, HeadsetPageState.CanPoll(true, true, true, false), "minimized");
            Equal(false, HeadsetPageState.CanPoll(true, true, false, true), "fullscreen");
        }

        private static void HeadsetPageStateText()
        {
            Equal("未检测到 2.4 GHz 接收器",
                HeadsetPageState.StatusText(HeadsetStatus.ReceiverMissing, false, false), "missing");
            Equal("接收器在线 · 耳机未响应",
                HeadsetPageState.StatusText(HeadsetStatus.NoResponse, false, false), "no response");
            Equal("电池供电", HeadsetPageState.StatusText(HeadsetStatus.Ok, true, false), "on battery");
            Equal("已连接充电线", HeadsetPageState.StatusText(HeadsetStatus.Ok, true, true), "external");
            Equal("充电状态未知", HeadsetPageState.StatusText(HeadsetStatus.Ok, false, false), "unknown");
        }

        private static void HeadsetPageStaleReading()
        {
            var reading = new HeadsetReading();
            reading.Status = HeadsetStatus.Ok;
            reading.BatteryPercent = 77;
            reading.ReadAt = new DateTime(2026, 10, 27, 12, 0, 0);
            Equal(false, HeadsetPageState.IsStale(reading, reading.ReadAt.AddSeconds(30)), "fresh");
            Equal(true, HeadsetPageState.IsStale(reading, reading.ReadAt.AddSeconds(91)), "stale");
            Equal("--", HeadsetPageState.LevelText(reading, reading.ReadAt.AddSeconds(91)), "stale level");
        }

        private static void FreshReadingReplacesExpiredValue()
        {
            var old = new HeadsetReading();
            old.Status = HeadsetStatus.Ok;
            old.BatteryPercent = 77;
            old.ReadAt = new DateTime(2026, 10, 27, 12, 0, 0);
            var fresh = new HeadsetReading();
            fresh.Status = HeadsetStatus.Ok;
            fresh.BatteryPercent = 55;
            fresh.ReadAt = new DateTime(2026, 10, 27, 12, 2, 0);
            Equal("--", HeadsetPageState.LevelText(old, old.ReadAt.AddSeconds(120)), "expired");
            Equal("55", HeadsetPageState.LevelText(fresh, fresh.ReadAt.AddSeconds(1)), "fresh");
        }

        private static void WindowFitStaysInsideWorkArea()
        {
            double w, h;
            WindowFit.Calculate(1920, 1040, out w, out h);
            Equal(1200.0, w, "large width");
            Equal(780.0, h, "large height");
            WindowFit.Calculate(1366, 728, out w, out h);
            if (w > 1350 || h > 712) throw new Exception("window exceeds 1366x728 work area");
            WindowFit.Calculate(1024, 728, out w, out h);
            if (w > 1008 || h > 712) throw new Exception("window exceeds 1024x728 work area");
        }

        private static void HeadsetTransactionHasBoundedRetryAndRemoteOff()
        {
            var plan = new HeadsetTransactionPlan();
            Equal(true, plan.Start(), "first pass");
            Equal(1, plan.Pass, "pass one");
            Equal(true, plan.NextPass(), "second pass");
            Equal(2, plan.Pass, "pass two");
            Equal(false, plan.NextPass(), "retry bounded");
            Equal(true, plan.MustRemoteOff, "remote off required");
        }
    }
}
