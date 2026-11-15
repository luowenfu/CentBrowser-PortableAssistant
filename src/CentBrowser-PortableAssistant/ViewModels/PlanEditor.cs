using System.Collections.ObjectModel;
using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.ViewModels;

/// <summary>编辑中的方案。改动由页面定时写回配置。</summary>
public sealed partial class PlanEditor : ObservableObject
{
    private readonly BackupPlan _plan;
    private readonly BrowserInstall? _install;
    private ProfileEditor? _current;

    public PlanEditor(BackupPlan plan, AppConfig config, BrowserInstall? install, PlanState state)
    {
        _plan = plan.Clone();
        _plan.Formats = BackupPlan.SingleFormat(_plan.Formats);
        _install = install;
        _password = SecretProtector.Unprotect(plan.PasswordProtected);
        _ratios = new Dictionary<string, double>(state.Ratios);

        if (install is not null)
        {
            var chosen = new HashSet<string>(CollectPlanner.SelectedProfiles(install, _plan), StringComparer.OrdinalIgnoreCase);
            foreach (var p in ProfileReader.ReadProfiles(install.UserDataDir))
            {
                Profiles.Add(new ProfileEditor(p, System.IO.Path.Combine(install.UserDataDir, p.DirName),
                    _plan.OptionsFor(p.DirName), chosen.Contains(p.DirName), OnProfilesChanged));
            }
        }

        LoadTargets(config.Targets, null);

        Current = Profiles.FirstOrDefault(p => p.IsChecked) ?? Profiles.FirstOrDefault();
    }

    /// <summary>停用的目的地不出现在主页面，但方案里原来勾过的还留着，重新启用后照旧勾上。</summary>
    private readonly HashSet<string> _hiddenTargets = [];

    /// <summary>目的地地址或停用状态变了之后刷新第 6 步，不用重开方案。</summary>
    public void RefreshLocations(IEnumerable<UploadTarget> targets)
    {
        var checks = Targets.ToDictionary(i => i.Key, i => i.IsChecked);
        foreach (var id in _hiddenTargets) checks[id] = true;
        LoadTargets(targets, checks);
        Raise(nameof(Destinations));
        Raise(nameof(DestinationsDetail));
        Raise(nameof(Problem));
        Raise(nameof(ShowDestination));
    }

    private void LoadTargets(IEnumerable<UploadTarget> targets, IReadOnlyDictionary<string, bool>? checks)
    {
        Targets.Clear();
        _hiddenTargets.Clear();
        foreach (var t in targets)
        {
            _locations[t.Id] = t.Location;
            var chosen = checks is not null ? checks.TryGetValue(t.Id, out var on) && on : _plan.TargetIds.Contains(t.Id);
            if (t.Disabled)
            {
                if (chosen) _hiddenTargets.Add(t.Id);
                continue;
            }
            var item = new CheckItem(t.Id, t.Name, UploadTarget.KindLabel(t.Kind))
            {
                IsChecked = chosen,
                Tip = t.Location,
                CanOpen = t.Kind is TargetKind.LocalFolder or TargetKind.Smb,
            };
            item.Toggled += () => Changed();
            Targets.Add(item);
        }
    }

    public string PlanId => _plan.Id;

    public ObservableCollection<ProfileEditor> Profiles { get; } = [];

    /// <summary>第 3、4 步正在编辑的用户。</summary>
    public ProfileEditor? Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            value?.EnsureExtensions();
            _current = value;
            Raise();
        }
    }

    public ObservableCollection<CheckItem> Targets { get; } = [];

    private readonly Dictionary<string, string> _locations = [];
    private readonly Dictionary<string, double> _ratios;
    private long? _rawSize;

    /// <summary>备份一开始就按现在的时间重画待打包的文件名。</summary>
    public void RefreshFileNames() => Raise(nameof(FileNames));

    public BrowserInstall? Install => _install;

    public bool HasBrowser => _install is not null;

    public bool IsManualBrowser => _plan.BrowserRoot.Length > 0;

    /// <summary>第 1 步标题右侧的标识。</summary>
    public string BrowserMode => IsManualBrowser ? "手动选择" : "自动识别";

    public string BrowserPath => _install?.RootDir ?? (IsManualBrowser ? _plan.BrowserRoot : "没找到 Cent Browser");

    /// <summary>浏览器名 • 版本 • 便携版，找不到时是提示。</summary>
    public string BrowserLabel => _install?.Label ?? (IsManualBrowser ? "这个目录里没有 Cent Browser" : "请选择 chrome.exe 所在目录");

    /// <summary>任何会影响打包结果的改动都会触发，页面据此保存并重新估算大小。</summary>
    public event Action? ContentChanged;

    public BackupPlan ToPlan()
    {
        var p = _plan.Clone();
        p.ProfileDirs = Profiles.Where(i => i.IsChecked).Select(i => i.Key).ToList();
        foreach (var profile in Profiles) p.Profiles[profile.Key] = profile.ToOptions();
        p.TargetIds = Targets.Where(i => i.IsChecked).Select(i => i.Key).Concat(_hiddenTargets).Distinct().ToList();
        if (Profiles.Any(i => i.IsChecked && i.IncludeSecrets)) p.RequirePassword = true;
        p.PasswordProtected = SecretProtector.Protect(_password);
        if (string.IsNullOrWhiteSpace(p.Slug)) p.Slug = NameTemplate.MakeSlug(p.Name);
        return p;
    }

    private bool _syncingProfiles;

    private void OnProfilesChanged()
    {
        if (_syncingProfiles) return;
        _plan.ProfilesChosen = true;
        if (_plan.AllProfiles && Profiles.Any(i => !i.IsChecked))
        {
            _plan.AllProfiles = false;
            Raise(nameof(AllProfiles));
        }
        Changed(null);
        Raise(nameof(PasswordLocked));
        Raise(nameof(PasswordEditable));
        Raise(nameof(PasswordCaption));
        Raise(nameof(RequirePassword));
        RaiseHint();
    }

    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (name is not null) Raise(name);
        Raise(nameof(FileNames));
        Raise(nameof(PackedSize));
        Raise(nameof(Destinations));
        Raise(nameof(DestinationsDetail));
        Raise(nameof(TemplateError));
        Raise(nameof(Problem));
        Raise(nameof(ShowDestination));
        ContentChanged?.Invoke();
    }
}
