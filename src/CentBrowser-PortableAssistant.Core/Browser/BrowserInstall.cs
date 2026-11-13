namespace CentBrowser_PortableAssistant.Core.Browser;

/// <summary>一份 Cent Browser：程序目录、版本目录和用户数据目录。</summary>
public sealed record BrowserInstall(
    string RootDir,
    string VersionDir,
    string Version,
    string UserDataDir)
{
    public const string PortableMarker = "cbportable";

    /// <summary>空文件。没有它，浏览器会当成第一次启动。</summary>
    public const string FirstRunFile = "First Run";

    /// <summary>内容是版本号。和当前程序对不上时，会打开更新说明。</summary>
    public const string LastVersionFile = "Last Version";

    public string ChromeExe => Path.Combine(RootDir, "chrome.exe");

    /// <summary>版本目录里有 cbportable 标记，用户数据跟着程序走。</summary>
    public bool IsPortable => File.Exists(Path.Combine(VersionDir, PortableMarker));

    public string ProductName => "Cent Browser";

    /// <summary>第 1 步显示的一行：Cent Browser • 5.3.1184.31 • 便携版。</summary>
    public string Label => $"{ProductName} • {Version} • {(IsPortable ? "便携版" : "安装版")}";
}
