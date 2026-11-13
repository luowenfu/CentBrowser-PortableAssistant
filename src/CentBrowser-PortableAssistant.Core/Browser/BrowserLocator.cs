using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CentBrowser_PortableAssistant.Core.Browser;

public static partial class BrowserLocator
{
    /// <summary>manualRoot 为空时自动识别：先查注册表里登记的 Cent Browser，再查默认安装位置。</summary>
    public static BrowserInstall? Locate(string? manualRoot)
    {
        if (!string.IsNullOrWhiteSpace(manualRoot)) return FromRoot(manualRoot);

        foreach (var exe in RegisteredExePaths().Concat(DefaultExePaths()))
        {
            var install = FromRoot(Path.GetDirectoryName(exe)!);
            if (install is not null) return install;
        }
        return null;
    }

    /// <summary>目录里有 Cent Browser 的 chrome.exe 才算数。</summary>
    public static BrowserInstall? FromRoot(string root)
    {
        var exe = Path.Combine(root, "chrome.exe");
        if (!File.Exists(exe)) return null;
        var info = FileVersionInfo.GetVersionInfo(exe);
        if (info.ProductName?.Contains("Cent", StringComparison.OrdinalIgnoreCase) != true) return null;

        var version = info.FileVersion ?? "";
        var versionDir = Path.Combine(root, version);
        if (!Directory.Exists(versionDir))
        {
            var latest = Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => n is not null && VersionPattern().IsMatch(n))
                .OrderByDescending(n => Version.Parse(n!))
                .FirstOrDefault();
            if (latest is null) return null;
            version = latest;
            versionDir = Path.Combine(root, latest);
        }

        var portableData = Path.Combine(root, "User Data");
        var userData = Directory.Exists(portableData) || File.Exists(Path.Combine(versionDir, BrowserInstall.PortableMarker))
            ? portableData
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CentBrowser", "User Data");

        return new BrowserInstall(Path.GetFullPath(root), versionDir, version, userData);
    }

    private static IEnumerable<string> RegisteredExePaths()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var clients = hive.OpenSubKey(@"Software\Clients\StartMenuInternet");
            if (clients is null) continue;
            foreach (var name in clients.GetSubKeyNames().Where(n => n.StartsWith("CentBrowser", StringComparison.OrdinalIgnoreCase)))
            {
                using var cmd = clients.OpenSubKey($@"{name}\shell\open\command");
                if (cmd?.GetValue(null) is string command && ParseExe(command) is { } exe) yield return exe;
            }
        }
    }

    private static IEnumerable<string> DefaultExePaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(local, "CentBrowser", "Application", "chrome.exe");
        yield return Path.Combine(pf, "CentBrowser", "Application", "chrome.exe");
        yield return Path.Combine(pf86, "CentBrowser", "Application", "chrome.exe");
    }

    internal static string? ParseExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var idx = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? command[..(idx + 4)] : null;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+\.\d+$")]
    private static partial Regex VersionPattern();
}
