using System.Windows;

namespace CentBrowser_PortableAssistant.Views;

public enum ResetChoice
{
    None,
    Reset,
    Uninstall,
}

public partial class ResetDialog : Window
{
    private ResetDialog()
    {
        InitializeComponent();
        TitleBarIcon.Remove(this);
    }

    public static ResetChoice Ask(Window? owner)
    {
        var dialog = new ResetDialog { Owner = owner };
        if (dialog.ShowDialog() != true) return ResetChoice.None;
        return dialog.UninstallOption.IsChecked == true ? ResetChoice.Uninstall : ResetChoice.Reset;
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        // 第一个选项在 InitializeComponent 里就会勾上，那时按钮还没创建
        if (OkButton is null) return;
        var uninstall = UninstallOption.IsChecked == true;
        OkButton.Content = uninstall ? "卸载" : "重置";
        OkButton.Style = (Style)FindResource(uninstall ? "DangerButton" : "PrimaryButton");
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
