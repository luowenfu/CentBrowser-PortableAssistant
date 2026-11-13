using System.Windows;
using System.Windows.Controls;
using CentBrowser_PortableAssistant.Core.Browser;
using Microsoft.Win32;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>第 1 步：这个方案用的浏览器，自动识别或手动选目录。</summary>
public partial class BrowserCard : UserControl
{
    /// <summary>用户选了新目录，空字符串表示改回自动识别。</summary>
    public event Action<string>? RootChanged;

    public BrowserCard() => InitializeComponent();

    private void OnPick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择浏览器 chrome.exe 所在目录" };
        if (dialog.ShowDialog() != true) return;
        if (BrowserLocator.FromRoot(dialog.FolderName) is null)
        {
            MessageDialog.Inform(Window.GetWindow(this), "没找到浏览器", "这个目录里没有 Cent Browser", "请选 chrome.exe 所在的目录");
            return;
        }
        RootChanged?.Invoke(dialog.FolderName);
    }

    private void OnAutoDetect(object sender, RoutedEventArgs e) => RootChanged?.Invoke("");
}
