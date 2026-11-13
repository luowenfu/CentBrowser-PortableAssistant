using System.Text.Json;

namespace CentBrowser_PortableAssistant.Core.Settings;

public static class ConfigStore
{
    public static AppConfig Load()
    {
        var config = JsonFile.Read<AppConfig>(AppPaths.ConfigFile);
        if (config is not null)
        {
            var changed = false;
            // 以前开机启动和自动备份是同一个开关。拆开后，旧配置沿用原来的选择
            if (!HasStartWithWindows())
            {
                config.StartWithWindows = config.AutoEnabled;
                changed = true;
            }
            if (!config.StartWithWindows && config.AutoEnabled)
            {
                config.AutoEnabled = false;
                changed = true;
            }
            if (config.BrowserRoot.Length > 0)
            {
                foreach (var p in config.Plans.Where(p => p.BrowserRoot.Length == 0)) p.BrowserRoot = config.BrowserRoot;
                config.BrowserRoot = "";
                changed = true;
            }
            foreach (var p in config.Plans)
            {
                var one = BackupPlan.SingleFormat(p.Formats);
                if (p.Formats != one)
                {
                    p.Formats = one;
                    changed = true;
                }
                if (p.Name == "默认方案")
                {
                    p.Name = "方案1";
                    if (p.Slug == "Default") p.Slug = "Plan1";
                    changed = true;
                }
            }
            if (changed) Save(config);
            return config;
        }
        config = AppConfig.CreateDefault();
        Save(config);
        return config;
    }

    public static void Save(AppConfig config) => JsonFile.Write(AppPaths.ConfigFile, config);

    private static bool HasStartWithWindows()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.ConfigFile));
            return doc.RootElement.TryGetProperty("StartWithWindows", out _);
        }
        catch (JsonException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
