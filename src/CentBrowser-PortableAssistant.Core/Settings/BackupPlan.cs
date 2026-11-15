namespace CentBrowser_PortableAssistant.Core.Settings;

[Flags]
public enum ArchiveFormats
{
    None = 0,
    Sfx = 1,
    SevenZip = 2,
    Zip = 4,
}

public enum ZipEncryption
{
    Aes256,
    ZipCrypto,
}

/// <summary>一个提取方案：备份哪些用户、哪些内容、打成什么格式、传到哪里。</summary>
public sealed class BackupPlan
{
    public string Id { get; set; } = NewId();

    public string Name { get; set; } = "方案1";

    /// <summary>手动指定的浏览器目录，空表示自动识别。</summary>
    public string BrowserRoot { get; set; } = "";

    /// <summary>用于英文文件名的标识，只含字母数字。</summary>
    public string Slug { get; set; } = "Plan1";

    /// <summary>为 true 时备份浏览器里登记的全部用户，忽略 ProfileDirs。新方案默认否。</summary>
    public bool AllProfiles { get; set; }

    /// <summary>用户已经自己勾过。没勾过、也没开全选、名单还是空的，才用最近一天用过的用户。</summary>
    public bool ProfilesChosen { get; set; }

    public List<string> ProfileDirs { get; set; } = [];

    /// <summary>按用户目录名存的内容和扩展设置。没设置过的用户用默认值（全带）。</summary>
    public Dictionary<string, ProfileOptions> Profiles { get; set; } = [];

    public ProfileOptions OptionsFor(string profileDir) =>
        Profiles.TryGetValue(profileDir, out var options) ? options : new ProfileOptions();

    public bool IncludeProgram { get; set; } = true;

    public ArchiveFormats Formats { get; set; } = ArchiveFormats.Sfx;

    public ZipEncryption ZipEncryption { get; set; } = ZipEncryption.Aes256;

    /// <summary>为 true 时压缩包要解压密码。旧配置没有这个字段时仍要密码。</summary>
    public bool RequirePassword { get; set; } = true;

    /// <summary>DPAPI 加密后的解压密码。</summary>
    public string PasswordProtected { get; set; } = "";

    /// <summary>勾了机密信息，或者打开了「设置解压密码」，压缩包必须有密码。</summary>
    public bool NeedsArchivePassword()
    {
        if (RequirePassword) return true;
        IEnumerable<string> dirs = AllProfiles ? Profiles.Keys : ProfileDirs;
        return dirs.Any(d => OptionsFor(d).IncludeSecrets);
    }

    public string NameTemplate { get; set; } = NameTemplateDefaults.Template;

    public string NamePrefix { get; set; } = "CentBrowser";

    public bool AsciiFileName { get; set; }

    public List<string> TargetIds { get; set; } = [];

    public int KeepCount { get; set; } = 3;

    /// <summary>是否加入自动备份。新建方案默认加入，总开关仍然默认关着。</summary>
    public bool AutoBackup { get; set; } = true;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>界面只留一种。以前同时勾了多种时，留下靠前的：自解压、7z、zip。</summary>
    public static ArchiveFormats SingleFormat(ArchiveFormats formats)
    {
        if (formats.HasFlag(ArchiveFormats.Sfx)) return ArchiveFormats.Sfx;
        if (formats.HasFlag(ArchiveFormats.SevenZip)) return ArchiveFormats.SevenZip;
        if (formats.HasFlag(ArchiveFormats.Zip)) return ArchiveFormats.Zip;
        return ArchiveFormats.None;
    }

    public BackupPlan Clone()
    {
        var copy = (BackupPlan)MemberwiseClone();
        copy.ProfileDirs = [.. ProfileDirs];
        copy.Profiles = Profiles.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
        copy.TargetIds = [.. TargetIds];
        return copy;
    }
}

public static class NameTemplateDefaults
{
    public const string Template = "{前缀}_{方案}_{日期}-{时间}_{类型}";
}
