using System.Windows;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>和程序风格一致的提示框：一行主文案，下面灰色说明，按钮写明做什么。</summary>
public partial class MessageDialog : Window
{
    private MessageDialog(Window? owner, string title, string message, string? detail, string? note, string okText)
    {
        InitializeComponent();
        TitleBarIcon.Remove(this);
        Owner = owner;
        Title = title;
        MessageText.Text = message;
        SetText(DetailText, detail);
        SetText(NoteText, note);
        OkButton.Content = okText;
    }

    /// <summary>问一句，点了确认按钮才返回 true。danger 为真时按钮是红色。</summary>
    public static bool Confirm(Window? owner, string title, string message, string? detail, string okText, bool danger = false)
    {
        var dialog = new MessageDialog(owner, title, message, detail, null, okText);
        if (danger) dialog.OkButton.Style = (Style)dialog.FindResource("DangerButton");
        return dialog.ShowDialog() == true;
    }

    /// <summary>只告知，一个按钮。</summary>
    public static void Inform(Window? owner, string title, string message, string? detail = null, string? note = null, string okText = "知道了")
    {
        var dialog = new MessageDialog(owner, title, message, detail, note, okText);
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        dialog.OkButton.IsCancel = true;
        dialog.ShowDialog();
    }

    private static void SetText(System.Windows.Controls.TextBlock block, string? text)
    {
        block.Text = text ?? "";
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
