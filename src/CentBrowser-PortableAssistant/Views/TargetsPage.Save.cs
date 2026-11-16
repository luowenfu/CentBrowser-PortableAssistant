using System.IO;
using System.Windows;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;
using Microsoft.Win32;

namespace CentBrowser_PortableAssistant.Views;

public partial class TargetsPage
{
    private UploadTarget? ReadForm()
    {
        if (_current is null) return null;
        var t = _current.Clone();
        t.Kind = SelectedKind;
        t.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? UploadTarget.KindLabel(t.Kind) : NameBox.Text.Trim();
        t.Address = AddressBox.Text.Trim();
        t.Port = ReadPort();
        t.RemoteDir = KindBox.SelectedIndex == FtpType ? DirBox.Text.Trim() : "";
        t.UserName = UserBox.Text.Trim();
        t.PasswordProtected = SecretProtector.Protect(PassBox.Password);
        t.AcceptAnyCertificate = AnyCertBox.IsChecked == true;
        t.Disabled = DisabledBox.IsChecked == true;
        return t;
    }

    private int ReadPort()
    {
        if (RemoteFields.Visibility != Visibility.Visible || PortPanel.Visibility != Visibility.Visible) return 0;
        var text = PortBox.Text.Trim();
        if (text.Length == 0) return 0;
        if (!int.TryParse(text, out var port) || port is < 1 or > 65535) throw new ArgumentException("端口不对");
        return port;
    }

    private bool TryRead(out UploadTarget target)
    {
        target = null!;
        try
        {
            if (ReadForm() is not { } form) return false;
            target = form;
            return true;
        }
        catch (ArgumentException ex)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            StatusText.Text = UploadErrors.Brief(ex);
            return false;
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var t)) return;
        if (!t.Disabled && !PrepareLocalFolder(t)) return;
        if (!t.Disabled && !await EnsureWritable(t)) return;
        var targets = App.Services.Config.Targets;
        if (_draft?.Id == t.Id)
        {
            targets.Add(t);
            _draft = null;
        }
        else
        {
            targets[targets.FindIndex(x => x.Id == t.Id)] = t;
        }
        App.Services.SaveConfig();
        Load(t.Id);
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Success");
        StatusText.Text = "已保存";
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var t)) return;
        if (!PrepareLocalFolder(t)) return;
        if (!await EnsureWritable(t)) return;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Success");
        StatusText.Text = "连接成功，可以写入" + _testNote;
    }

    /// <summary>本地文件夹还不存在时先问。点创建才建，取消就不继续。</summary>
    private bool PrepareLocalFolder(UploadTarget t)
    {
        if (t.Kind != TargetKind.LocalFolder) return true;
        string path;
        try
        {
            path = LocalFolderUploader.Require(t.Address);
        }
        catch (ArgumentException ex)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            StatusText.Text = UploadErrors.Brief(ex);
            return false;
        }
        if (Directory.Exists(path)) return true;
        if (!MessageDialog.Confirm(Window.GetWindow(this), "创建文件夹", "不存在这个文件夹，是否创建", null, "创建"))
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Text2");
            StatusText.Text = "没有创建文件夹";
            return false;
        }
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (UploadErrors.IsExpected(ex))
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            StatusText.Text = UploadErrors.Brief(ex);
            return false;
        }
    }

    /// <summary>真正写一个测试文件再删掉。写不进去就不算通过。</summary>
    private async Task<bool> EnsureWritable(UploadTarget t)
    {
        SetBusy(true);
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Text2");
        StatusText.Text = "正在检查能否写入…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var uploader = UploaderFactory.Create(t);
            await uploader.TestAsync(cts.Token);
            _testNote = uploader switch
            {
                SmbUploader { ReusedConnection: true } => "。用的是这台电脑已有的连接，填的用户名和密码没有验证",
                WebDavUploader { IsPlainHttp: true } => "。注意：地址是 http://，密码和文件是明文传输，建议改用 https://",
                FtpUploader { IsEncrypted: true } => "，传输已加密",
                FtpUploader when t.Kind == TargetKind.FtpAuto => "。注意：服务器不支持加密，密码和文件是明文传输",
                _ => "",
            };
            return true;
        }
        catch (Exception ex) when (UploadErrors.IsExpected(ex) || ex is OperationCanceledException)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            StatusText.Text = UploadErrors.Brief(ex);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Editor.IsEnabled = !busy;
        TargetList.IsEnabled = !busy;
        AddButton.IsEnabled = !busy;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        _draft = new UploadTarget
        {
            Name = $"目的地 {App.Services.Config.Targets.Count + 1}",
            Kind = TargetKind.LocalFolder,
        };
        Load(_draft.Id);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_draft?.Id == _current.Id)
        {
            _draft = null;
            Load(null);
            return;
        }
        if (!MessageDialog.Confirm(Window.GetWindow(this), "删除目的地", $"删除目的地「{_current.Name}」",
                "已经传上去的备份会保留", "删除", danger: true)) return;
        var config = App.Services.Config;
        config.Targets.RemoveAll(t => t.Id == _current.Id);
        foreach (var p in config.Plans) p.TargetIds.Remove(_current.Id);
        App.Services.SaveConfig();
        Load(null);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择备份文件夹", InitialDirectory = AddressBox.Text };
        if (dialog.ShowDialog() == true) AddressBox.Text = dialog.FolderName;
    }
}
