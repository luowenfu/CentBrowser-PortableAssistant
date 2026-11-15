using System.Collections.ObjectModel;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Services;

namespace CentBrowser_PortableAssistant.ViewModels;

/// <summary>一个用户：带不带走，以及这个用户自己的内容和扩展。</summary>
public sealed class ProfileEditor : ObservableObject
{
    private readonly ProfileOptions _options;
    private readonly string _profilePath;
    private readonly Action _changed;
    private bool _isChecked;
    private bool _extensionsLoaded;
    private bool _bulk;

    public ProfileEditor(ProfileInfo profile, string profilePath, ProfileOptions options, bool isChecked, Action changed)
    {
        Key = profile.DirName;
        Title = profile.DisplayName;
        FullTitle = profile.Email.Length == 0 || profile.Email == profile.DisplayName
            ? profile.DisplayName
            : $"{profile.DisplayName} • {profile.Email}";
        Email = profile.Email.Length > 0 ? profile.Email : "未登录账号";
        LastUsed = profile.LastActive is { } t ? DescribeLastUsed(t) : "";
        _profilePath = profilePath;
        _options = options.Clone();
        _isChecked = isChecked;
        _changed = changed;
    }

    public string Key { get; }

    public string Title { get; }

    /// <summary>悬停提示：昵称 • 邮箱。</summary>
    public string FullTitle { get; }

    public string Email { get; }

    /// <summary>今天用过、昨天用过、N 天前用过。</summary>
    public string LastUsed { get; }

    private static string DescribeLastUsed(DateTime when)
    {
        var days = (DateTime.Today - when.Date).Days;
        return days switch
        {
            <= 0 => "今天用过",
            1 => "昨天用过",
            < 365 => $"{days} 天前用过",
            _ => "一年多没用",
        };
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value)) _changed();
        }
    }

    public bool IncludeBookmarks
    {
        get => _options.IncludeBookmarks;
        set
        {
            if (_options.IncludeBookmarks == value) return;
            _options.IncludeBookmarks = value;
            Raise(nameof(BookmarkSize));
            Raise(nameof(HistorySize));
            Changed();
        }
    }

    public bool IncludeHistory
    {
        get => _options.IncludeHistory;
        set
        {
            if (_options.IncludeHistory == value) return;
            _options.IncludeHistory = value;
            Raise(nameof(BookmarkSize));
            Raise(nameof(HistorySize));
            Changed();
        }
    }

    public bool IncludeSettings
    {
        get => _options.IncludeSettings;
        set { _options.IncludeSettings = value; Changed(); }
    }

    public bool IncludeSiteData
    {
        get => _options.IncludeSiteData;
        set { _options.IncludeSiteData = value; Changed(); }
    }

    public bool IncludeSecrets
    {
        get => _options.IncludeSecrets;
        set
        {
            if (_options.IncludeSecrets == value) return;
            _options.IncludeSecrets = value;
            Raise(nameof(IncludeSecrets));
            Raise(nameof(SettingsSize));
            Raise(nameof(SecretsSize));
            Changed();
        }
    }

    public bool IncludeExtensions
    {
        get => _options.IncludeExtensions;
        set { _options.IncludeExtensions = value; Changed(); }
    }

    public bool IncludeExtensionData
    {
        get => _options.IncludeExtensionData;
        set
        {
            if (_options.IncludeExtensionData == value) return;
            _options.IncludeExtensionData = value;
            _bulk = true;
            foreach (var e in Extensions)
                if (e.IsChecked) e.IncludeData = value;
            _bulk = false;
            Raise(nameof(IncludeExtensionData));
            Raise(nameof(ExtensionSummary));
            Raise(nameof(ExtensionSize));
            Changed();
        }
    }

    public bool AllExtensions
    {
        get => _options.AllExtensions;
        set
        {
            if (_options.AllExtensions == value) return;
            _options.AllExtensions = value;
            _bulk = true;
            foreach (var e in Extensions) e.IsChecked = value;
            var selected = Extensions.Where(e => e.IsChecked).ToList();
            var allData = selected.Count > 0 && selected.All(e => e.IncludeData);
            var dataChanged = _options.IncludeExtensionData != allData;
            _options.IncludeExtensionData = allData;
            _bulk = false;
            Raise(nameof(AllExtensions));
            if (dataChanged) Raise(nameof(IncludeExtensionData));
            Raise(nameof(ExtensionSummary));
            Raise(nameof(ExtensionSize));
            Changed();
        }
    }

    public ObservableCollection<ExtensionRow> Extensions { get; } = [];

    public int ExtensionCount => Extensions.Count;

    /// <summary>标题栏右侧：勾选的扩展有多少，程序和数据各多大。</summary>
    public string ExtensionSummary
    {
        get
        {
            if (!_extensionsLoaded) return "";
            var selected = Extensions.Where(e => e.IsChecked).ToList();
            var program = selected.Sum(e => e.ProgramBytes);
            var data = selected.Where(e => e.IncludeData).Sum(e => e.DataBytes);
            return $"{selected.Count}个扩展 | 程序 {Format.Size(program).Replace(" ", "")} | 数据 {Format.Size(data).Replace(" ", "")}";
        }
    }

    public string BookmarkSize => Show(BookmarkBytes);

    public string HistorySize => Show(HistoryBytes);

    public string SettingsSize => Show(SettingsBytes);

    public string SiteDataSize => Show(_measure?.SiteData ?? 0);

    public string SecretsSize => Show(SecretsBytes);

    public string ExtensionSize
    {
        get
        {
            if (!_extensionsLoaded) return "…";
            var selected = Extensions.Where(e => e.IsChecked).ToList();
            var bytes = selected.Sum(e => e.ProgramBytes) + selected.Where(e => e.IncludeData).Sum(e => e.DataBytes);
            if (_measure is { } m && selected.Any(e => e.IncludeData)) bytes += m.SharedExtensions;
            return Format.Size(bytes);
        }
    }

    /// <summary>第一次打开这个用户时在后台统计各项体积。</summary>
    public void EnsureContentSizes()
    {
        if (_sizesStarted) return;
        _sizesStarted = true;
        var path = _profilePath;
        var context = SynchronizationContext.Current;
        Task.Run(() =>
        {
            try { return ProfilePaths.MeasureContent(path); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return default; }
        }).ContinueWith(t =>
        {
            void Apply()
            {
                if (t.IsCompletedSuccessfully)
                {
                    _measure = t.Result;
                    Raise(nameof(BookmarkSize));
                    Raise(nameof(HistorySize));
                    Raise(nameof(SettingsSize));
                    Raise(nameof(SiteDataSize));
                    Raise(nameof(SecretsSize));
                    Raise(nameof(ExtensionSize));
                }
            }
            if (context is null) Apply();
            else context.Post(_ => Apply(), null);
        });
    }

    private bool _sizesStarted;
    private ProfilePaths.ContentMeasure? _measure;

    private bool IconsWithBookmarks => _options.IncludeBookmarks || !_options.IncludeHistory;

    private long BookmarkBytes => (_measure?.Bookmarks ?? 0) + (IconsWithBookmarks ? _measure?.Favicons ?? 0 : 0);

    private long HistoryBytes => (_measure?.History ?? 0) + (IconsWithBookmarks ? 0 : _measure?.Favicons ?? 0);

    private long SettingsBytes => (_measure?.Settings ?? 0) + (_options.IncludeSecrets ? 0 : _measure?.WebData ?? 0);

    private long SecretsBytes => (_measure?.Secrets ?? 0) + (_options.IncludeSecrets ? (_measure?.WebData ?? 0) + (_measure?.WebSide ?? 0) : 0);

    private string Show(long bytes) => _measure is null ? "…" : Format.Size(bytes);

    /// <summary>第一次选中这个用户时才读扩展，避免一打开就扫描所有用户的扩展大小。</summary>
    public void EnsureExtensions()
    {
        if (_extensionsLoaded) return;
        _extensionsLoaded = true;
        foreach (var info in ProfileReader.ReadExtensions(_profilePath).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var selected = _options.AllExtensions || _options.ExtensionIds.Contains(info.Id);
            var item = new ExtensionRow(info)
            {
                IsChecked = selected,
                IncludeData = selected && _options.WantsExtensionData(info.Id),
            };
            item.Toggled += () =>
            {
                if (_bulk) return;
                SyncMaster(all => _options.AllExtensions = all, e => e.IsChecked, nameof(AllExtensions));
            };
            item.DataToggled += SyncDataMaster;
            Extensions.Add(item);
        }
        Raise(nameof(ExtensionCount));
        Raise(nameof(ExtensionSummary));
        Raise(nameof(ExtensionSize));
    }

    public ProfileOptions ToOptions()
    {
        var o = _options.Clone();
        if (_extensionsLoaded)
        {
            var selected = Extensions.Where(e => e.IsChecked).ToList();
            o.ExtensionIds = selected.Select(e => e.Id).ToList();
            o.ExtensionDataIds = selected.Where(e => e.IncludeData).Select(e => e.Id).ToList();
            o.AllExtensions = Extensions.Count > 0 && selected.Count == Extensions.Count;
            o.IncludeExtensionData = selected.Count > 0 && selected.All(e => e.IncludeData);
        }
        return o;
    }

    private void SyncDataMaster()
    {
        if (_bulk) return;
        var selected = Extensions.Where(e => e.IsChecked).ToList();
        var all = selected.Count > 0 && selected.All(e => e.IncludeData);
        if (_options.IncludeExtensionData != all)
        {
            _options.IncludeExtensionData = all;
            Raise(nameof(IncludeExtensionData));
        }
        Raise(nameof(ExtensionSummary));
        Raise(nameof(ExtensionSize));
        Changed();
    }

    private void SyncMaster(Action<bool> store, Func<ExtensionRow, bool> read, string name)
    {
        if (_bulk) return;
        var all = Extensions.Count > 0 && Extensions.All(read);
        store(all);
        Raise(name);
        Raise(nameof(ExtensionSummary));
        Raise(nameof(ExtensionSize));
        Changed();
    }

    private void Changed() => _changed();
}
