using System;

namespace RogMouse
{
    public static class WindowFit
    {
        /// <summary>
        /// The 1200x780 design fits a 1366x768 desktop with the taskbar visible only after the
        /// 16 px margin is applied. Small work areas win over the nominal minimum.
        /// </summary>
        public static void Calculate(double workWidth, double workHeight,
            out double width, out double height)
        {
            double maxW = Math.Max(640, workWidth - 16);
            double maxH = Math.Max(560, workHeight - 16);
            width = Math.Min(1200, Math.Max(1040, maxW));
            height = Math.Min(780, Math.Max(680, maxH));
            if (width > maxW) width = maxW;
            if (height > maxH) height = maxH;
        }
    }
}
