namespace CentBrowser_PortableAssistant.Core.Browser;

/// <summary>
/// 浏览器里登记的一个用户。DirName 是 User Data 下的目录名，例如 Profile 4。
/// Email 是登录的谷歌账号，没登录为空。LastActive 是最后一次用这个用户的时间。
/// </summary>
public sealed record ProfileInfo(string DirName, string DisplayName, string Email = "", DateTime? LastActive = null);
