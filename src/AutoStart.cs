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
        private const string StartupApprovedRunKey =
            "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
        private const string ValueName = "PeripheralInstrument";
        private const string LegacyValueName = "ROGMouseControlCenter";
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
                    return string.Equals(v, RunCommand(), StringComparison.OrdinalIgnoreCase)
                        && !IsDisabledByWindows(ValueName);
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// True when launch-at-logon needs to be written or migrated. This covers an old
        /// ROGMouseControlCenter path and a current-name value that points at an older exe.
        /// A Windows-disabled entry is left alone so an explicit Task Manager disable is not
        /// silently overridden during a routine app launch.
        /// </summary>
        public static bool NeedsUpgrade()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    string v = key.GetValue(ValueName, null) as string;
                    if (v != null && !string.Equals(v, RunCommand(), StringComparison.OrdinalIgnoreCase)
                        && !IsDisabledByWindows(ValueName)) return true;

                    string legacy = key.GetValue(LegacyValueName, null) as string;
                    return !string.IsNullOrEmpty(legacy) && !IsDisabledByWindows(LegacyValueName);
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
                    string legacy = key.GetValue(LegacyValueName, null) as string;
                    return (v != null && v.IndexOf(ExePath(), StringComparison.OrdinalIgnoreCase) < 0)
                        || (legacy != null && legacy.IndexOf(ExePath(), StringComparison.OrdinalIgnoreCase) < 0);
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Task Manager stores a separate launch approval flag. A valid Run value can still
        /// be skipped at logon when that flag is disabled, so it is part of the real state.
        /// </summary>
        private static bool IsDisabledByWindows(string valueName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKey, false))
                {
                    if (key == null) return false;
                    byte[] state = key.GetValue(valueName, null) as byte[];
                    return state != null && state.Length > 0 && (state[0] & 1) != 0;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Removes the Task Manager override so Windows starts honoring the Run value again.
        /// A missing approval value is the normal enabled state.
        /// </summary>
        private static string ClearWindowsDisableOverride(string valueName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKey, true))
                {
                    if (key == null) return null;
                    if (key.GetValue(valueName, null) != null) key.DeleteValue(valueName, false);
                    return null;
                }
            }
            catch (Exception ex) { return "无法清除 Windows 启动项禁用状态：" + ex.Message; }
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
                    if (key.GetValue(LegacyValueName, null) != null) key.DeleteValue(LegacyValueName, false);
                }
                string error = ClearWindowsDisableOverride(ValueName);
                return error ?? ClearWindowsDisableOverride(LegacyValueName);
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
                    if (key.GetValue(LegacyValueName, null) != null) key.DeleteValue(LegacyValueName, false);
                    string error = ClearWindowsDisableOverride(ValueName);
                    if (error != null) return error;
                    ClearWindowsDisableOverride(LegacyValueName);
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
