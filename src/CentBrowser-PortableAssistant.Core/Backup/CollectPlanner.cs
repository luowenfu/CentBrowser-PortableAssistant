using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>按方案算出要带走的文件清单。缓存和打开的标签页不在白名单里，密码和 Cookie 只在勾了机密信息时加入。</summary>
public static class CollectPlanner
{
    public const string RootFolder = "CentBrowser";

    private static readonly string[] ProgramDirs = ["Dictionaries"];

    private static readonly string[] SkippedRootExtensions = [".bat", ".cmd", ".ps1", ".log", ".tmp"];

    public static IReadOnlyList<string> SelectedProfiles(BrowserInstall install, BackupPlan plan)
    {
        var registered = ProfileReader.ReadProfiles(install.UserDataDir).Select(p => p.DirName).ToList();
        if (plan.AllProfiles) return registered;
        if (plan.ProfilesChosen || plan.ProfileDirs.Count > 0)
            return registered.Where(d => plan.ProfileDirs.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
        var recent = ProfileReader.ReadRecentProfiles(install.UserDataDir);
        return registered.Where(d => recent.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    public static IReadOnlyList<CollectItem> Plan(BrowserInstall install, BackupPlan plan, IReadOnlyList<string> profiles)
    {
        var items = new List<CollectItem>();
        if (plan.IncludeProgram) AddProgram(install, items);

        var userData = Path.Combine(RootFolder, "User Data");
        items.Add(new CollectItem(Path.Combine(install.UserDataDir, "Local State"), Path.Combine(userData, "Local State"), CollectKind.LocalState));
        // 缺了这两个文件，解压后第一次打开会进功能介绍页，而不是设好的起始页
        items.Add(new CollectItem("", Path.Combine(userData, BrowserInstall.FirstRunFile), CollectKind.FirstRun));
        items.Add(new CollectItem("", Path.Combine(userData, BrowserInstall.LastVersionFile), CollectKind.LastVersion));

        foreach (var dir in profiles)
        {
            var src = Path.Combine(install.UserDataDir, dir);
            var dst = Path.Combine(userData, dir);
            void Add(string rel, CollectKind kind = CollectKind.Copy)
            {
                var path = Path.Combine(src, rel);
                if (File.Exists(path) || Directory.Exists(path)) items.Add(new CollectItem(path, Path.Combine(dst, rel), kind));
            }

            var o = plan.OptionsFor(dir);
            Add(ProfilePaths.GoogleProfilePicture);
            Add(ProfilePaths.AvatarImages);
            if (o.IncludeBookmarks) foreach (var f in ProfilePaths.Bookmarks) Add(f);
            if (o.IncludeHistory) foreach (var f in ProfilePaths.History) Add(f);
            if (o.IncludeBookmarks || o.IncludeHistory) foreach (var f in ProfilePaths.Favicons) Add(f);
            if (o.IncludeSiteData) Add(ProfilePaths.LocalStorage);

            if (o.IncludeSettings || o.IncludeSecrets)
                Add(ProfilePaths.WebData, o.IncludeSecrets ? CollectKind.Copy : CollectKind.SearchEngines);
            if (o.IncludeSecrets)
            {
                Add(ProfilePaths.WebData + "-journal");
                Add(ProfilePaths.WebData + "-wal");
                foreach (var f in ProfilePaths.Secrets) Add(f);
            }

            // 扩展登记在 Preferences 里，只带扩展不带设置时也要一份精简过的 Preferences
            if (o.IncludeSettings || o.IncludeExtensions)
            {
                Add(ProfilePaths.Preferences, CollectKind.Preferences);
                Add(ProfilePaths.SecurePreferences, CollectKind.Preferences);
            }
            if (o.IncludeSettings) Add("Custom Dictionary.txt");

            if (!o.IncludeExtensions) continue;
            var extensionIds = SelectedExtensions(src, o).ToList();
            foreach (var id in extensionIds)
            {
                Add(Path.Combine(ProfilePaths.ExtensionsDir, id));
                if (o.WantsExtensionData(id)) foreach (var p in ProfilePaths.ExtensionData(id)) Add(p);
            }
            // 这几个库是多个扩展共用的，没法按扩展拆开，只要有一个扩展带数据就整体带上
            if (extensionIds.Any(o.WantsExtensionData)) foreach (var p in ProfilePaths.SharedExtensionData) Add(p);
        }
        return items;
    }

    public static IEnumerable<string> SelectedExtensions(string profileDir, ProfileOptions options)
    {
        var installed = ProfileReader.ReadExtensionIds(profileDir);
        return options.AllExtensions ? installed : installed.Where(id => options.ExtensionIds.Contains(id));
    }

    private static void AddProgram(BrowserInstall install, List<CollectItem> items)
    {
        foreach (var file in Directory.GetFiles(install.RootDir))
        {
            if (SkippedRootExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            items.Add(new CollectItem(file, Path.Combine(RootFolder, Path.GetFileName(file)), CollectKind.Copy, IsProgram: true));
        }

        var versionName = Path.GetFileName(install.VersionDir);
        items.Add(new CollectItem(install.VersionDir, Path.Combine(RootFolder, versionName), CollectKind.Copy, IsProgram: true));
        items.Add(new CollectItem("", Path.Combine(RootFolder, versionName, BrowserInstall.PortableMarker), CollectKind.PortableMarker, IsProgram: true));

        foreach (var dir in ProgramDirs)
        {
            var path = Path.Combine(install.RootDir, dir);
            if (Directory.Exists(path)) items.Add(new CollectItem(path, Path.Combine(RootFolder, dir), CollectKind.Copy, IsProgram: true));
        }
    }

    /// <summary>会打进包里的浏览器程序有多大。脚本和日志不算。</summary>
    public static long MeasureProgram(BrowserInstall install)
    {
        long bytes = 0;
        foreach (var file in Directory.GetFiles(install.RootDir))
        {
            if (SkippedRootExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            bytes += new FileInfo(file).Length;
        }
        bytes += ProfilePaths.SizeOf(install.VersionDir);
        foreach (var dir in ProgramDirs)
            bytes += ProfilePaths.SizeOf(Path.Combine(install.RootDir, dir));
        return bytes;
    }
}
