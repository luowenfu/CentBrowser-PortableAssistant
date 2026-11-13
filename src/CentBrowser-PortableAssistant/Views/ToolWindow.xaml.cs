using System.Windows;
using System.Windows.Controls;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>承载网盘、待上传、关于这几个页面的弹窗。页面实例由调用方复用。</summary>
public partial class ToolWindow : Window
{
    private ToolWindow()
    {
        InitializeComponent();
        TitleBarIcon.Remove(this);
    }

    public static void ShowPage(Window owner, string title, UserControl page, double width, double height)
    {
        var window = new ToolWindow
        {
            Owner = owner,
            Title = title,
            Width = width,
            Height = height,
            MinWidth = width * 0.8,
            MinHeight = height * 0.7,
            Content = page,
        };
        window.Closed += (_, _) => window.Content = null;
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) window.Close();
        };
        window.ShowDialog();
    }
}
