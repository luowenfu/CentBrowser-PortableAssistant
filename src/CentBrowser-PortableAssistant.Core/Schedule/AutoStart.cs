using Microsoft.Win32;

namespace CentBrowser_PortableAssistant.Core.Schedule;

/// <summary>开机启动写在当前用户的 Run 键，不需要管理员权限。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CentBrowser-PortableAssistant";
    public const string TrayArgument = "--tray";

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static bool IsSet()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>开机启动已经开着时，把路径改到新文件。没开则不动。</summary>
    public static void Retarget(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not string) return;
        key.SetValue(ValueName, $"\"{exePath}\" {TrayArgument}");
    }
}
