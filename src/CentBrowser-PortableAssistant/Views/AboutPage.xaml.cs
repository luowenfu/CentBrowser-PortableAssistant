using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText.Text = "v" + version.Split('+')[0];
        ConfigPath.Text = AppPaths.ConfigDir;
        PendingPath.Text = AppPaths.PendingDir;
    }

    private void OnOpenConfig(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.ConfigDir);

    private void OnOpenPending(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.PendingDir);

    private void OnOpenGitHub(object sender, RoutedEventArgs e) =>
        OpenSite("https://github.com/luowenfu/CentBrowser-PortableAssistant");

    private void OnOpenGitee(object sender, RoutedEventArgs e) =>
        OpenSite("https://gitee.com/dfsss/CentBrowser-PortableAssistant");

    private static void OpenSite(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void OnOpenLicense(object sender, RoutedEventArgs e)
    {
        SevenZipTool.EnsureExtracted();
        Process.Start(new ProcessStartInfo(Path.Combine(AppPaths.ToolsDir, "License.txt")) { UseShellExecute = true });
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void OnCoffee(object sender, RoutedEventArgs e) => CoffeePopup.IsOpen = true;

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var choice = ResetDialog.Ask(owner);
        if (choice == ResetChoice.None) return;

        var uninstall = choice == ResetChoice.Uninstall;
        if (sender is Button button) button.IsEnabled = false;
        var held = false;
        try
        {
            await App.Backup.QuiesceAsync();
            held = true;
            await App.Services.Queue.DropPendingAsync();
            if (uninstall) App.Services.Uninstall();
            else App.Services.Reset();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Inform(owner, uninstall ? "卸载" : "重置", "有文件正在使用，没删干净",
                uninstall ? $"退出后手动删除这个文件夹：{AppPaths.LocalDir}" : "关掉占用这些文件的程序后再重置一次",
                ex.Message, okText: uninstall ? "退出" : "知道了");
            if (uninstall) ((App)Application.Current).ExitNow();
            return;
        }
        finally
        {
            if (held && !uninstall) App.Backup.Resume();
            if (!uninstall && sender is Button again) again.IsEnabled = true;
        }

        if (uninstall)
        {
            MessageDialog.Inform(owner, "卸载", "已清理完毕",
                "设置、备份记录、待上传文件、开机启动和本机上释放的工具文件都已删除。最后删掉本程序的 exe 就卸载完了", okText: "退出");
            ((App)Application.Current).ExitNow();
            return;
        }

        MessageDialog.Inform(owner, "重置", "已回到刚开始用的状态",
            "设置和还没传完的文件都已删除。备份记录保留");
    }
}
