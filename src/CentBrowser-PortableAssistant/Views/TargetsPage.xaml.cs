using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;
using Microsoft.Win32;

namespace CentBrowser_PortableAssistant.Views;

public partial class TargetsPage : UserControl
{
    private sealed class Row : INotifyPropertyChanged
    {
        public Row(string id, string name, string kind, bool disabled, bool draft = false)
        {
            Id = id;
            Name = name;
            _kind = kind;
            _disabled = disabled;
            IsDraft = draft;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsDraft { get; }

        private string _kind;
        public string Kind
        {
            get => _kind;
            set
            {
                if (_kind == value) return;
                _kind = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Kind)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KindLine)));
            }
        }

        private bool _disabled;
        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value) return;
                _disabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Disabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KindLine)));
            }
        }

        public string KindLine => Disabled ? $"{Kind} · 已停用" : Kind;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private const int SmbType = 1;
    private const int WebDavType = 2;
    private const int FtpType = 3;
    private static readonly string[] Types = ["本地文件夹", "SMB", "WebDAV", "FTP"];
    private static readonly (string Label, TargetKind Kind)[] FtpModes =
    [
        ("自动（推荐）", TargetKind.FtpAuto),
        ("FTPS 显式加密", TargetKind.FtpsExplicit),
        ("FTPS 隐式加密", TargetKind.FtpsImplicit),
        ("不加密", TargetKind.Ftp),
    ];

    private UploadTarget? _current;

    /// <summary>点了添加但还没保存的目的地，关掉窗口或选别的就丢掉。</summary>
    private UploadTarget? _draft;
    private bool _loading;
    private bool _reverting;
    private bool _applyingKind;
    private string _testNote = "";

    /// <summary>四种类型各记各的地址，切换时不把路径或服务器带过去。</summary>
    private readonly string[] _addressOfKind = new string[4];
    private int _addressKind = -1;

    public TargetsPage()
    {
        InitializeComponent();
        KindBox.ItemsSource = Types;
        SecurityBox.ItemsSource = FtpModes.Select(m => m.Label).ToList();
        Loaded += (_, _) => Load(_current?.Id);
        Unloaded += (_, _) => _draft = null;
    }

    private void Load(string? selectId)
    {
        _loading = true;
        var targets = App.Services.Config.Targets;
        var rows = targets.Select(t => new Row(t.Id, t.Name, UploadTarget.KindLabel(t.Kind), t.Disabled)).ToList();
        if (_draft is not null)
            rows.Add(new Row(_draft.Id, $"{_draft.Name}（未保存）", UploadTarget.KindLabel(_draft.Kind), _draft.Disabled, draft: true));
        TargetList.ItemsSource = rows;
        var index = rows.FindIndex(r => r.Id == selectId);
        TargetList.SelectedIndex = index >= 0 ? index : rows.Count > 0 ? 0 : -1;
        Editor.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Hidden;
        _loading = false;
        ShowSelected();
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        // 没保存就换了一项，左侧类型回到已保存的值
        foreach (var previous in e.RemovedItems.OfType<Row>())
        {
            previous.Kind = KindLabelOf(previous.Id);
            previous.Disabled = DisabledOf(previous.Id);
        }
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (TargetList.SelectedItem is not Row row) return;
        if (_draft is not null && row.Id != _draft.Id)
        {
            _draft = null;
            Load(row.Id);
            return;
        }
        _current = _draft is { } draft && row.Id == draft.Id
            ? draft.Clone()
            : App.Services.Config.Targets.First(t => t.Id == row.Id).Clone();
        var ftpMode = Array.FindIndex(FtpModes, m => m.Kind == _current.Kind);
        _loading = true;
        KindBox.SelectedIndex = _current.Kind switch
        {
            TargetKind.LocalFolder => 0,
            TargetKind.Smb => SmbType,
            TargetKind.WebDav => WebDavType,
            _ => ftpMode >= 0 ? FtpType : WebDavType,
        };
        SecurityBox.SelectedIndex = Math.Max(ftpMode, 0);
        DisabledBox.IsChecked = _current.Disabled;
        _loading = false;
        NameBox.Text = _current.Name;
        RememberAddresses();
        PortBox.Text = _current.Port > 0 ? _current.Port.ToString() : "";
        DirBox.Text = _current.RemoteDir;
        UserBox.Text = _current.UserName;
        PassBox.Password = SecretProtector.Unprotect(_current.PasswordProtected);
        AnyCertBox.IsChecked = _current.AcceptAnyCertificate;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Text2");
        StatusText.Text = "";
        UpdateFields();
        ApplyEditorMode();
    }

    private void ApplyEditorMode()
    {
        if (_current is null) return;
        var creating = _draft?.Id == _current.Id;
        EditorTitle.Text = creating ? "添加目的地" : $"编辑「{_current.Name}」";
        EditorHint.Text = creating
            ? "填好后点下方的添加。已有的目的地不会被改掉"
            : "改的是这一条。要新增请点左侧的添加目的地";
        SaveButton.Content = creating ? "添加" : "保存";
        DeleteButton.Visibility = creating ? Visibility.Collapsed : Visibility.Visible;
        CancelAddButton.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        KindPicker.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        KindSummary.Visibility = creating ? Visibility.Collapsed : Visibility.Visible;
        if (!creating) KindName.Text = UploadTarget.KindLabel(_current.Kind);
    }

    private void OnChangeType(object sender, RoutedEventArgs e)
    {
        KindSummary.Visibility = Visibility.Collapsed;
        KindPicker.Visibility = Visibility.Visible;
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_reverting)
        {
            UpdateFields();
            return;
        }
        if (!_loading && IsInitialized && sender == KindBox && KindBox.SelectedIndex >= 0)
        {
            if (!_applyingKind && _draft is null && NeedsKindConfirm())
            {
                var targetId = (TargetList.SelectedItem as Row)?.Id;
                var newIndex = KindBox.SelectedIndex;
                var oldIndex = PreviousTypeIndex(e);
                if (targetId is not null)
                    Dispatcher.BeginInvoke(() => HoldKindChange(targetId, oldIndex, newIndex));
                return;
            }
            SwapAddress(KindBox.SelectedIndex);
        }
        UpdateFields();
        if (_loading || !IsInitialized || KindBox.SelectedIndex < 0) return;
        if (TargetList.SelectedItem is not Row row) return;
        var kind = SelectedKind;
        row.Kind = UploadTarget.KindLabel(kind);
        if (_current?.Id == row.Id) _current.Kind = kind;
        if (_draft?.Id == row.Id) _draft.Kind = kind;
        if (_draft is null)
        {
            KindName.Text = UploadTarget.KindLabel(kind);
            KindPicker.Visibility = Visibility.Collapsed;
            KindSummary.Visibility = Visibility.Visible;
        }
    }

    private bool NeedsKindConfirm()
    {
        if (TargetList.SelectedItem is not Row row) return false;
        var saved = App.Services.Config.Targets.FirstOrDefault(t => t.Id == row.Id);
        return saved is not null && TypeIndex(saved.Kind) != KindBox.SelectedIndex;
    }

    private static int PreviousTypeIndex(SelectionChangedEventArgs e)
    {
        var previous = e.RemovedItems.OfType<string>().FirstOrDefault();
        return previous is null ? -1 : Array.IndexOf(Types, previous);
    }

    private static int TypeIndex(TargetKind kind) => kind switch
    {
        TargetKind.LocalFolder => 0,
        TargetKind.Smb => SmbType,
        TargetKind.WebDav => WebDavType,
        _ => FtpType,
    };

    private static TargetKind KindFromTypeIndex(int index) => index switch
    {
        0 => TargetKind.LocalFolder,
        SmbType => TargetKind.Smb,
        FtpType => TargetKind.FtpAuto,
        _ => TargetKind.WebDav,
    };

    /// <summary>先把类型选回原来的，再问是改这一条还是另加一条。选中项要等这次选择事件结束才改得回去。</summary>
    private void HoldKindChange(string targetId, int oldIndex, int newIndex)
    {
        if (!IsLoaded || _draft is not null) return;
        if (oldIndex >= 0 && KindBox.SelectedIndex != oldIndex)
        {
            _reverting = true;
            KindBox.SelectedIndex = oldIndex;
            _reverting = false;
        }
        KindPicker.Visibility = Visibility.Collapsed;
        KindSummary.Visibility = Visibility.Visible;
        AskKindChange(targetId, newIndex);
    }

    /// <summary>改已有目的地的类型前先问。选添加新的就另开一条，原来的不动。</summary>
    private void AskKindChange(string targetId, int newIndex)
    {
        if (!IsLoaded || _draft is not null) return;
        if (TargetList.SelectedItem is not Row row || row.Id != targetId) return;
        var saved = App.Services.Config.Targets.FirstOrDefault(t => t.Id == targetId);
        if (saved is null || newIndex < 0 || newIndex >= Types.Length) return;
        if (TypeIndex(saved.Kind) == newIndex) return;

        var choice = MessageDialog.Choose(
            Window.GetWindow(this),
            "改的是现有目的地",
            $"这会把「{saved.Name}」从{UploadTarget.KindLabel(saved.Kind)}改成{Types[newIndex]}",
            "不会新建一条。要另存一条，点添加新的",
            "改这一条",
            "添加新的");
        if (choice == DialogChoice.Alternate)
        {
            StartDraft(newIndex);
            return;
        }
        if (choice != DialogChoice.Confirm) return;
        if (TargetList.SelectedItem is not Row still || still.Id != targetId) return;
        _applyingKind = true;
        KindBox.SelectedIndex = newIndex;
        _applyingKind = false;
    }

    private string KindLabelOf(string id)
    {
        if (_draft?.Id == id) return UploadTarget.KindLabel(_draft.Kind);
        var saved = App.Services.Config.Targets.FirstOrDefault(t => t.Id == id);
        return saved is null ? "" : UploadTarget.KindLabel(saved.Kind);
    }

    private bool DisabledOf(string id)
    {
        if (_draft?.Id == id) return _draft.Disabled;
        return App.Services.Config.Targets.FirstOrDefault(t => t.Id == id)?.Disabled == true;
    }

    private void OnDisabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized) return;
        if (TargetList.SelectedItem is not Row row) return;
        var disabled = DisabledBox.IsChecked == true;
        row.Disabled = disabled;
        if (_current?.Id == row.Id) _current.Disabled = disabled;
        if (_draft?.Id == row.Id) _draft.Disabled = disabled;
    }

    private TargetKind SelectedKind => KindBox.SelectedIndex switch
    {
        0 => TargetKind.LocalFolder,
        SmbType => TargetKind.Smb,
        FtpType => FtpModes[Math.Max(SecurityBox.SelectedIndex, 0)].Kind,
        _ => TargetKind.WebDav,
    };

    private void UpdateFields()
    {
        if (!IsInitialized) return;
        var kind = SelectedKind;
        var local = kind == TargetKind.LocalFolder;
        var ftp = KindBox.SelectedIndex == FtpType;
        SecurityPanel.Visibility = ftp ? Visibility.Visible : Visibility.Collapsed;
        RemoteFields.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        // 端口和加密方式贴在服务器右边，远程目录单独一行。都只有 FTP 用。SMB 的共享名、WebDAV 的子目录写在地址里
        DirPanel.Visibility = ftp ? Visibility.Visible : Visibility.Collapsed;
        if (ftp && string.IsNullOrWhiteSpace(DirBox.Text)) DirBox.Text = "/";
        if (ftp && PortBox.Text.Trim() is "" or "21" or "990") PortBox.Text = kind == TargetKind.FtpsImplicit ? "990" : "21";
        BrowseButton.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        PortPanel.Visibility = ftp ? Visibility.Visible : Visibility.Collapsed;
        AnyCertBox.Visibility = kind is TargetKind.Ftp or TargetKind.Smb ? Visibility.Collapsed : Visibility.Visible;
        DirHint.Text = "目录要已经存在，例如 /backup，根目录填 /";
        AuthHint.Text = kind switch
        {
            TargetKind.Smb => "留空就用当前 Windows 登录的账号。域账号写成 域名\\用户名",
            TargetKind.WebDav => "坚果云等网盘要填“应用密码”，不是登录密码",
            _ => "必填。匿名登录填 anonymous",
        };
        (AddressLabel.Text, AddressHint.Text) = kind switch
        {
            TargetKind.LocalFolder => ("文件夹", "可以选 U 盘、移动硬盘，或同步盘的本地目录。没有这个文件夹时会询问是否创建"),
            TargetKind.Smb => ("服务器", @"写到共享名，例如 \\192.168.1.10\home，共享名里的子文件夹接在后面。子文件夹要已经存在"),
            TargetKind.WebDav => ("WebDAV 地址", "坚果云：https://dav.jianguoyun.com/dav/\nNextcloud：https://域名/remote.php/dav/files/用户名/\n要存到子目录就接在后面，子目录要已经存在"),
            _ => ("服务器", "主机名或 IP，例如 192.168.1.10，不要带 ftp://"),
        };
    }

    /// <summary>打开一条时，只给当前类型留下地址，另外三种是空的。</summary>
    private void RememberAddresses()
    {
        Array.Clear(_addressOfKind);
        _addressKind = KindBox.SelectedIndex;
        if (_addressKind < 0) return;
        _addressOfKind[_addressKind] = AddressForEdit(_current!);
        AddressBox.Text = _addressOfKind[_addressKind];
    }

    /// <summary>先把正在看的地址记回原来的类型，再显示新类型自己的。</summary>
    private void SwapAddress(int kindIndex)
    {
        if (_addressKind >= 0) _addressOfKind[_addressKind] = AddressBox.Text;
        _addressKind = kindIndex;
        AddressBox.Text = _addressOfKind[kindIndex] ?? "";
    }

    /// <summary>旧版把共享名、子目录单独存在 RemoteDir 里，打开时并进地址，保存后就不再用它。</summary>
    private static string AddressForEdit(UploadTarget t)
    {
        if (t.Address.Trim().Length == 0) return t.Address;
        return t.Kind switch
        {
            TargetKind.Smb => SmbPath.Format(t.Address, t.RemoteDir),
            TargetKind.WebDav when UploaderFactory.NormalizeDir(t.RemoteDir).Length > 0 => t.Location,
            _ => t.Address,
        };
    }

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
        var added = _draft?.Id == t.Id;
        if (added)
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
        StatusText.Text = added ? "已添加" : "已保存";
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

    private void OnAdd(object sender, RoutedEventArgs e) => StartDraft(0);

    private void OnCancelAdd(object sender, RoutedEventArgs e)
    {
        _draft = null;
        Load(null);
    }

    private void StartDraft(int kindIndex)
    {
        _draft = new UploadTarget
        {
            Name = $"目的地 {App.Services.Config.Targets.Count + 1}",
            Kind = KindFromTypeIndex(kindIndex),
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
