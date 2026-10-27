namespace RogMouse
{
    /// <summary>
    /// Pure ordering rules for the headset transaction. Keeping this small state machine out
    /// of Win32 code makes the read-only sequence and retry boundary testable without a device.
    /// </summary>
    public sealed class HeadsetTransactionPlan
    {
        public const int MaxPasses = 2;
        private int _pass;

        public bool Start()
        {
            _pass = 0;
            return NextPass();
        }

        /// <summary>Starts the next bounded remote-on/query pass; false after MaxPasses.</summary>
        public bool NextPass()
        {
            if (_pass >= MaxPasses) return false;
            _pass++;
            return true;
        }

        public int Pass { get { return _pass; } }
        public bool CanAcceptAck { get { return _pass > 0; } }
        public bool MustRemoteOff { get { return true; } }
    }
}
