namespace CentBrowser_PortableAssistant.Core.Browser;

/// <summary>用户目录里各类内容对应的文件，相对 Profile 目录。</summary>
public static class ProfilePaths
{
    public static readonly string[] Bookmarks = ["Bookmarks"];

    public static readonly string[] History = ["History", "Favicons", "Top Sites", "Visited Links", "Shortcuts"];

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
}
