using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;
using CentBrowser_PortableAssistant.Services;
using CentBrowser_PortableAssistant.ViewModels;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>主界面：选浏览器、用户、内容、扩展，再定打包和上传。改动自动保存。</summary>
public partial class PlansPage : UserControl
{
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly ObservableCollection<PlanTab> _tabs = [];
    private PlanEditor? _editor;
    private string _signature = "";
    private string _locationSignature = "";
    private string _nameBeforeRename = "";
    private bool _selfSave;
    private int _sizeRequest;
    private readonly TagPlane _tag;

    public event Action? TargetsRequested;

    /// <summary>切到这个方案，让行李牌上的错误露出来。</summary>
    public void ShowPlan(string planId)
    {
        if (_tabs.FirstOrDefault(t => t.Id == planId) is not { } tab) return;
        if (ReferenceEquals(PlanTabs.SelectedItem, tab)) return;
        PlanTabs.SelectedItem = tab;
    }

    /// <summary>
    /// 整页不限高量过一遍之后，第 5、6 步这一列比最高那一列矮多少。
    /// 窗口按这一列定高度：扩展和用户列表自己能滚动，不该撑高窗口。
    /// </summary>
    public double PackColumnShortfall() => Body.DesiredSize.Height - PackColumn.DesiredSize.Height;

    /// <summary>
    /// 量窗口高度时按选了 .zip 来量。加密选项平时收着，不量进去的话，选中后第 5 步会多出滚动条。
    /// 量完把可见性交还给原来的绑定和样式。
    /// </summary>
    public void MeasureWithZipOptions(Action measure)
    {
        var zipBinding = BindingOperations.GetBinding(ZipOptions, VisibilityProperty);
        Binding? restore = zipBinding is null ? null : new Binding(zipBinding.Path.Path)
        {
            Mode = zipBinding.Mode,
            Converter = zipBinding.Converter,
        };
        ZipOptions.Visibility = Visibility.Visible;
        FormatHintText.Visibility = Visibility.Collapsed;
        try
        {
            measure();
        }
        finally
        {
            if (restore != null) BindingOperations.SetBinding(ZipOptions, VisibilityProperty, restore);
            else ZipOptions.ClearValue(VisibilityProperty);
            FormatHintText.ClearValue(VisibilityProperty);
        }
    }

    public PlansPage()
    {
        InitializeComponent();
        _tag = new TagPlane(TagGrid, PlaneLayer, Plane, StartLine, ExtractLine, PackLine, BackupButton, SizeText,
            PlanNameText, PackedSizeText, FileList, DestinationPanel, DestinationText, ProblemText, OnPlaneArrived);
        foreach (var v in NameTemplate.Variables)
        {
            var key = new Button { Content = v, Style = (Style)FindResource("Keycap") };
            key.Click += (_, _) => InsertVariable(v);
            Variables.Children.Add(key);
        }
        _sizeTimer.Tick += (_, _) =>
        {
            _sizeTimer.Stop();
            _ = EstimateSizeAsync();
        };
        _saveTimer.Tick += (_, _) => Flush();
        PlanTabs.ItemsSource = _tabs;

        App.Backup.Notice += (title, _, _) => Dispatcher.BeginInvoke(() =>
        {
            if (title != "开始备份") return;
            _tag.Retake(() => _editor?.RefreshFileNames(), _editor?.RawSize is long size ? Format.Size(size) : null);
        });
        App.Status.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            _tag.OnStatus();
            UpdateBackupButton();
        });
        App.Backup.BusyChanged += () => Dispatcher.BeginInvoke(UpdateBackupButton);
        App.Services.ConfigChanged += () =>
        {
            if (!_selfSave) Dispatcher.BeginInvoke(OnConfigChanged);
        };
        Browser.RootChanged += OnBrowserRootChanged;
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is not { } window) return;
            window.PreviewMouseDown -= OnWindowMouseDown;
            window.PreviewMouseDown += OnWindowMouseDown;
        };
        LoadPlans(null);
    }

    private void OnBrowserRootChanged(string root)
    {
        if (_editor is null) return;
        _saveTimer.Stop();
        var plan = _editor.ToPlan();
        plan.BrowserRoot = root;
        var plans = App.Services.Config.Plans;
        var index = plans.FindIndex(p => p.Id == plan.Id);
        if (index < 0) return;
        plans[index] = plan;
        _selfSave = true;
        try { App.Services.SaveConfig(); }
        finally { _selfSave = false; }
        App.Services.RefreshInstalls();
        OpenEditor(plan);
    }

    /// <summary>方案列表或网盘变了才重建编辑器；只改了自动备份等设置时保持原样。</summary>
    private static string Signature(AppConfig c) =>
        string.Join("|", c.Plans.Select(p => p.Id)) + "#" + string.Join("|", c.Targets.Select(t => $"{t.Id}:{t.Name}:{t.Kind}"));

    /// <summary>地址、目录、端口变了要更新悬停提示，不必重建整个编辑器。</summary>
    private static string LocationSignature(AppConfig c) =>
        string.Join("|", c.Targets.Select(t => $"{t.Id}\u001f{t.Address}\u001f{t.RemoteDir}\u001f{t.Port}\u001f{t.Disabled}"));

    private void OnConfigChanged()
    {
        var config = App.Services.Config;
        if (Signature(config) != _signature)
        {
            LoadPlans(_editor?.PlanId);
            return;
        }
        var locations = LocationSignature(config);
        if (locations == _locationSignature) return;
        _locationSignature = locations;
        _editor?.RefreshLocations(config.Targets);
    }

    private void LoadPlans(string? selectId)
    {
        _saveTimer.Stop();
        var config = App.Services.Config;
        _signature = Signature(config);
        _locationSignature = LocationSignature(config);
        _tabs.Clear();
        foreach (var p in config.Plans) _tabs.Add(new PlanTab(p.Id, p.Name));
        PlanTabs.SelectedItem = _tabs.FirstOrDefault(t => t.Id == selectId) ?? _tabs.FirstOrDefault();
    }

    private void OnPlanSelected(object sender, SelectionChangedEventArgs e)
    {
        Flush();
        if (PlanTabs.SelectedItem is not PlanTab tab) return;
        var plan = App.Services.Config.Plans.FirstOrDefault(p => p.Id == tab.Id);
        if (plan is null) return;
        App.Services.RefreshInstalls();
        OpenEditor(plan);
    }

    private void OpenEditor(BackupPlan plan)
    {
        var state = App.Services.History.Read(h => h.For(plan.Id));
        _editor = new PlanEditor(plan, App.Services.Config, App.Services.InstallFor(plan), state);
        _editor.ContentChanged += () =>
        {
            _sizeTimer.Stop();
            _sizeTimer.Start();
            _saveTimer.Stop();
            _saveTimer.Start();
            UpdateHints();
        };
        _editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlanEditor.Current)) UpdateHints();
        };
        DataContext = _editor;
        PasswordInput.Password = _editor.Password;
        UpdateHints();
        _ = EstimateSizeAsync();
    }

    private void UpdateHints()
    {
        if (_editor is null) return;
        var noBrowser = _editor.HasBrowser ? null : "先在第 1 步选好浏览器";

        var profiles = noBrowser ?? (_editor.Profiles.Count == 0 ? "这个浏览器里还没有用户" : null);
        ProfilesHint.Text = profiles ?? "";
        ProfilesHint.Visibility = profiles is null ? Visibility.Collapsed : Visibility.Visible;

        var current = _editor.Current;
        var skipped = current is { IsChecked: false } ? "第 2 步没勾选这个用户，不会带走" : null;
        ContentHint.Text = skipped ?? "";
        ContentHint.Visibility = skipped is null ? Visibility.Collapsed : Visibility.Visible;

        var extensions = noBrowser
                         ?? (current is null ? "先在第 2 步选用户"
                             : !current.IncludeExtensions ? "第 3 步没勾选扩展"
                             : current.ExtensionCount == 0 ? "这个用户没有装扩展" : null);
        ExtensionsHint.Text = extensions ?? "";
        ExtensionsHint.Visibility = extensions is null ? Visibility.Collapsed : Visibility.Visible;
        ExtensionsList.Visibility = extensions is null ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>把还没写盘的改动立刻保存。切换方案、打开弹窗、关窗口前调用。</summary>
    public void Flush()
    {
        if (!_saveTimer.IsEnabled) return;
        _saveTimer.Stop();
        SaveCurrent();
    }

    private BackupPlan? SaveCurrent()
    {
        if (_editor is null) return null;
        var plan = _editor.ToPlan();
        var plans = App.Services.Config.Plans;
        var index = plans.FindIndex(p => p.Id == plan.Id);
        if (index < 0) return null;
        plans[index] = plan;
        _selfSave = true;
        try
        {
            App.Services.SaveConfig();
        }
        finally
        {
            _selfSave = false;
        }
        if (_tabs.FirstOrDefault(t => t.Id == plan.Id) is { } tab) tab.Name = plan.Name;
        return plan;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_editor is not null && _editor.Password != PasswordInput.Password) _editor.Password = PasswordInput.Password;
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        var show = PasswordPlain.Visibility != Visibility.Visible;
        if (!show) PasswordInput.Password = _editor?.Password ?? "";
        PasswordPlain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordInput.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        RevealButton.Content = show ? "隐藏" : "显示";
    }

    private void OnResetNameTemplate(object sender, RoutedEventArgs e)
    {
        if (_editor is null || _editor.NameTemplateText == NameTemplateDefaults.Template) return;
        _editor.NameTemplateText = NameTemplateDefaults.Template;
    }

    private void InsertVariable(string v)
    {
        var at = TemplateBox.CaretIndex;
        TemplateBox.Text = TemplateBox.Text.Insert(at, v);
        TemplateBox.CaretIndex = at + v.Length;
        TemplateBox.Focus();
    }

    private void OnManageTargets(object sender, RoutedEventArgs e) => TargetsRequested?.Invoke();

    private void OnOpenTarget(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CheckItem { CanOpen: true } item }) return;
        var path = item.Tip;
        if (path.Length == 0 || UploaderFactory.IsBlankLocation(path) || path.Contains("://", StringComparison.Ordinal)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void OnRenameMenu(object sender, RoutedEventArgs e)
    {
        if (PlanTabs.SelectedItem is PlanTab tab) BeginRename(tab);
    }

    private void OnTabDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PlanTabs.SelectedItem is PlanTab tab && !tab.IsEditing) BeginRename(tab);
    }

    private void BeginRename(PlanTab tab)
    {
        if (_editor is null) return;
        _nameBeforeRename = _editor.Name;
        tab.IsEditing = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (PlanTabs.ItemContainerGenerator.ContainerFromItem(tab) is not DependencyObject container) return;
            if (FindChild<TextBox>(container) is not { } box) return;
            box.Focus();
            box.SelectAll();
        });
    }

    private void EndRename(bool cancel)
    {
        if (_tabs.FirstOrDefault(t => t.IsEditing) is not { } tab || _editor is null) return;
        tab.IsEditing = false;
        if (cancel || string.IsNullOrWhiteSpace(_editor.Name)) _editor.Name = _nameBeforeRename;
        else _editor.Name = _editor.Name.Trim();
        Flush();
    }

    private void OnRenameKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Escape)) return;
        EndRename(cancel: e.Key == System.Windows.Input.Key.Escape);
        e.Handled = true;
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e) => EndRename(cancel: false);

    /// <summary>点空白处键盘焦点不会移走，所以在窗口上拦鼠标：点到改名框以外就结束改名。</summary>
    private void OnWindowMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_tabs.FirstOrDefault(t => t.IsEditing) is not { } tab) return;
        if (PlanTabs.ItemContainerGenerator.ContainerFromItem(tab) is DependencyObject container
            && FindChild<TextBox>(container) is { IsMouseOver: true }) return;
        EndRename(cancel: false);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match && child is FrameworkElement { Visibility: Visibility.Visible }) return match;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    private void OnBackupNow(object sender, RoutedEventArgs e)
    {
        if (_editor?.Problem is not null) return;
        _saveTimer.Stop();
        var plan = SaveCurrent();
        if (plan is null) return;
        if (_editor?.Install is { } install && BrowserProcess.IsRunning(install))
        {
            App.Status.Note("Cent Browser 还开着，关掉后再备份", error: true);
            return;
        }
        _ = App.Backup.RunAsync([plan], foreground: true);
    }

    private void AddPlan(BackupPlan plan)
    {
        Flush();
        App.Services.Config.Plans.Add(plan);
        App.Services.SaveConfig();
        LoadPlans(plan.Id);
        if (_tabs.FirstOrDefault(t => t.Id == plan.Id) is { } tab) BeginRename(tab);
    }

    private void OnNewPlan(object sender, RoutedEventArgs e)
    {
        var config = App.Services.Config;
        AddPlan(new BackupPlan
        {
            Name = $"方案 {config.Plans.Count + 1}",
            Slug = $"Plan{config.Plans.Count + 1}",
        });
    }

    private void OnCopyPlan(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        var copy = _editor.ToPlan();
        copy.Id = BackupPlan.NewId();
        copy.Name += " 副本";
        copy.Slug += "Copy";
        AddPlan(copy);
    }

    private void OnDeletePlan(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        var plans = App.Services.Config.Plans;
        if (plans.Count <= 1)
        {
            App.Status.Note("至少要留一个方案", error: true);
            return;
        }
        if (!MessageDialog.Confirm(Window.GetWindow(this), "删除方案", $"删除方案「{_editor.Name}」",
                "已经备份到目的地的文件会保留", "删除", danger: true)) return;
        _saveTimer.Stop();
        plans.RemoveAll(p => p.Id == _editor.PlanId);
        App.Services.SaveConfig();
        LoadPlans(null);
    }

    /// <summary>飞完目的地才解开定格。本地复制往往先结束，飞机还要滑完剩下的时间。</summary>
    private void OnPlaneArrived()
    {
        UpdateBackupButton();
        _ = EstimateSizeAsync();
    }

    /// <summary>飞行中按钮保持不可按。上传阶段提取和打包已经结束，单看 IsBusy 会把它放开。</summary>
    private void UpdateBackupButton() =>
        BackupButton.IsEnabled = !App.Backup.IsBusy && App.Status.Flight is null && !_tag.Frozen;

    /// <summary>按当前勾选估算未提取大小，后台算，只保留最后一次请求的结果。飞行中不改牌上的字。</summary>
    private async Task EstimateSizeAsync()
    {
        var editor = _editor;
        if (editor?.Install is not { } install)
        {
            if (!_tag.Frozen) SizeText.Text = "—";
            return;
        }
        var request = ++_sizeRequest;
        var plan = editor.ToPlan();
        if (!_tag.Frozen) SizeText.Text = "…";
        var size = await Task.Run(() =>
        {
            var profiles = CollectPlanner.SelectedProfiles(install, plan);
            return CollectPlanner.Plan(install, plan, profiles).Sum(i => ProfilePaths.SizeOf(i.Source));
        });
        if (request != _sizeRequest) return;
        editor.RawSize = size;
        if (!_tag.Frozen) SizeText.Text = Format.Size(size);
    }
}
