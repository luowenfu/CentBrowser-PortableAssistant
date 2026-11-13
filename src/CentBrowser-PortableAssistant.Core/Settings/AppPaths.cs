namespace CentBrowser_PortableAssistant.Core.Settings;

public static class AppPaths
{
    public const string AppFolderName = "CentBrowser-PortableAssistant";

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string LocalDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string ConfigFile => Path.Combine(ConfigDir, "config.json");

    public static string HistoryFile => Path.Combine(ConfigDir, "history.json");

    public static string PendingDir => Path.Combine(LocalDir, "Pending");

    public static string QueueFile => Path.Combine(PendingDir, "queue.json");

    public static string StagingDir => Path.Combine(LocalDir, "Staging");

    public static string ToolsDir => Path.Combine(LocalDir, "7zip");

    public static string DefaultLocalBackupDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CentBrowserBackups");
}
