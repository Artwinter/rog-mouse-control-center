using System;
using Microsoft.Win32;

namespace RogMouse
{
    /// <summary>
    /// Launch-at-logon switch backed by the HKCU Run key. Per-user (no admin needed),
    /// visible in Task Manager's Startup tab, and fully reversible: enabling writes one
    /// value, disabling deletes it. The value always quotes the exe path, because the
    /// install folder name contains spaces and an unquoted path would misparse.
    /// </summary>
    public static class AutoStart
    {
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "ROGMouseControlCenter";
        private const string StartupArgument = " --startup";

        /// <summary>Path written into the Run key; the app entry assembly, not the host exe of a shortcut.</summary>
        private static string ExePath()
        {
            try { return System.Reflection.Assembly.GetEntryAssembly().Location; }
            catch { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>Command line used only by the logon entry; manual launches stay visible.</summary>
        private static string RunCommand()
        {
            return '"' + ExePath() + '"' + StartupArgument;
        }

        /// <summary>True when the Run value exists and still points at this exe.</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    string v = key.GetValue(ValueName, null) as string;
                    return string.Equals(v, RunCommand(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        /// <summary>True when the Run value targets this exe but predates silent startup.</summary>
        public static bool NeedsUpgrade()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    string v = key.GetValue(ValueName, null) as string;
                    return v != null
                        && v.IndexOf(ExePath(), StringComparison.OrdinalIgnoreCase) >= 0
                        && !string.Equals(v, RunCommand(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        /// <summary>True when the value exists but points somewhere else (stale install path).</summary>
        public static bool IsStale()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    string v = key.GetValue(ValueName, null) as string;
                    return v != null && v.IndexOf(ExePath(), StringComparison.OrdinalIgnoreCase) < 0;
                }
            }
            catch { return false; }
        }

        /// <summary>Writes or updates the Run value. Returns null on success, else the error text.</summary>
        public static string Enable()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return "无法打开注册表 Run 键";
                    key.SetValue(ValueName, RunCommand(), RegistryValueKind.String);
                    return null;
                }
            }
            catch (Exception ex) { return "写入注册表失败：" + ex.Message; }
        }

        /// <summary>Removes the Run value. A missing value counts as success. Returns null on success.</summary>
        public static string Disable()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return null;
                    if (key.GetValue(ValueName, null) != null) key.DeleteValue(ValueName, false);
                    return null;
                }
            }
            catch (Exception ex) { return "删除注册表值失败：" + ex.Message; }
        }

        /// <summary>Turns the switch on or off; returns null on success, else the error text.</summary>
        public static string Set(bool on)
        {
            return on ? Enable() : Disable();
        }
    }
}
