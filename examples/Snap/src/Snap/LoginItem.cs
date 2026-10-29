using Microsoft.Win32;

namespace Snap;

/// Open at sign-in, for an unpackaged app: a value under the per-user Run key.
/// Test runs use their own value name, so they never change the real setting.
public static class LoginItem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string ValueName => AppPaths.IsOverridden ? "SnapSelfTest" : "Snap";

    public static bool IsOn
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
