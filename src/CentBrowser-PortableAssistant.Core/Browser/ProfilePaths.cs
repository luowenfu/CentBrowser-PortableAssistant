namespace CentBrowser_PortableAssistant.Core.Browser;

/// <summary>用户目录里各类内容对应的文件，相对 Profile 目录。</summary>
public static class ProfilePaths
{
    public static readonly string[] Bookmarks = ["Bookmarks"];

    public static readonly string[] History = ["History", "Top Sites", "Visited Links", "Shortcuts"];

    /// <summary>书签和历史共用的网站图标。</summary>
    public static readonly string[] Favicons = ["Favicons", "Favicons-journal"];

    public const string LocalStorage = "Local Storage";

    /// <summary>密码、Cookie、账号自动填充。Web Data 另算，因为设置项也要用它的搜索引擎。</summary>
    public static readonly string[] Secrets =
    [
        "Login Data", "Login Data-journal", "Login Data-wal",
        "Login Data For Account", "Login Data For Account-journal",
        Path.Combine("Network", "Cookies"), Path.Combine("Network", "Cookies-journal"), Path.Combine("Network", "Cookies-wal"),
        "Account Web Data", "Account Web Data-journal",
    ];

    public const string WebData = "Web Data";

    public const string Preferences = "Preferences";

    public const string SecurePreferences = "Secure Preferences";

    public static readonly string[] Settings = [Preferences, SecurePreferences, "Custom Dictionary.txt"];

    public const string ExtensionsDir = "Extensions";

    /// <summary>谷歌账号头像。选择用户那一页读的就是这张图。</summary>
    public const string GoogleProfilePicture = "Google Profile Picture.png";

    /// <summary>账号头像的另一份缓存，按账号编号存放。</summary>
    public static readonly string AvatarImages = Path.Combine("Accounts", "Avatar Images");

    /// <summary>多个扩展共用的数据库，只能整体带上。</summary>
    public static readonly string[] SharedExtensionData = ["Extension State", "Extension Rules", "Extension Scripts"];

    /// <summary>单个扩展自己的数据目录。</summary>
    public static IEnumerable<string> ExtensionData(string id)
    {
        yield return Path.Combine("Local Extension Settings", id);
        yield return Path.Combine("Sync Extension Settings", id);
        yield return Path.Combine("Managed Extension Settings", id);
        yield return Path.Combine("IndexedDB", $"chrome-extension_{id}_0.indexeddb.leveldb");
        yield return Path.Combine("IndexedDB", $"chrome-extension_{id}_0.indexeddb.blob");
        yield return Path.Combine("Storage", "ext", id);
        yield return Path.Combine("DNR Extension Rules", id);
    }

    public static long SizeOf(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        if (!Directory.Exists(path)) return 0;
        return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    }

    /// <summary>第 3 步六项各自的体积。图标固定归书签，Web Data 固定归浏览器设置。</summary>
    public readonly record struct ContentMeasure(
        long Bookmarks,
        long Favicons,
        long History,
        long Settings,
        long WebData,
        long WebSide,
        long SiteData,
        long Secrets,
        long SharedExtensions);

    public static ContentMeasure MeasureContent(string profileDir)
    {
        long Of(string rel) => SizeOf(Path.Combine(profileDir, rel));
        long Sum(IEnumerable<string> rels) => rels.Sum(Of);
        return new ContentMeasure(
            Sum(Bookmarks),
            Sum(Favicons),
            Sum(History),
            Sum(Settings),
            Of(WebData),
            Of(WebData + "-journal") + Of(WebData + "-wal"),
            Of(LocalStorage),
            Sum(Secrets),
            Sum(SharedExtensionData));
    }
}
