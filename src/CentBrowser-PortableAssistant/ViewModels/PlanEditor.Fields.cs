using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;
using CentBrowser_PortableAssistant.Services;

namespace CentBrowser_PortableAssistant.ViewModels;

/// <summary>方案里可编辑的字段和由它们算出的预览文字。</summary>
public sealed partial class PlanEditor
{
    private string _password;

    public string Name
    {
        get => _plan.Name;
        set
        {
            _plan.Name = value;
            _plan.Slug = NameTemplate.MakeSlug(value);
            Changed();
            Raise(nameof(Slug));
        }
    }

    public string Slug
    {
        get => _plan.Slug;
        set { _plan.Slug = NameTemplate.Sanitize(value, asciiOnly: true); Changed(); }
    }

    public bool AutoBackup
    {
        get => _plan.AutoBackup;
        set { _plan.AutoBackup = value; Changed(); }
    }

    public bool AllProfiles
    {
        get => _plan.AllProfiles;
        set
        {
            if (_plan.AllProfiles == value) return;
            _plan.AllProfiles = value;
            _plan.ProfilesChosen = true;
            _syncingProfiles = true;
            foreach (var p in Profiles) p.IsChecked = value;
            _syncingProfiles = false;
            Changed();
        }
    }

    public bool IncludeProgram
    {
        get => _plan.IncludeProgram;
        set { _plan.IncludeProgram = value; Changed(); }
    }

    public ArchiveFormats ArchiveFormat
    {
        get => _plan.Formats;
        set
        {
            if (_plan.Formats == value) return;
            _plan.Formats = value;
            Changed();
            Raise(nameof(FormatZip));
            RaiseHint();
        }
    }

    public bool FormatZip => _plan.Formats == ArchiveFormats.Zip;

    /// <summary>当前格式下的那一行小字。zip 再跟着加密方式变。</summary>
    public string FormatHint => _plan.Formats switch
    {
        ArchiveFormats.Sfx => "双击输密码就能解压，无需压缩软件",
        ArchiveFormats.SevenZip => "体积最小，要用 7-Zip 或 Bandizip 解压",
        ArchiveFormats.Zip when _plan.ZipEncryption == CentBrowser_PortableAssistant.Core.Settings.ZipEncryption.ZipCrypto => "兼容性好，但安全性很弱",
        ArchiveFormats.Zip => "安全，但 Windows 自带解压打不开",
        _ => "",
    };

    public bool FormatHintDanger =>
        _plan.Formats == ArchiveFormats.Zip && _plan.ZipEncryption == CentBrowser_PortableAssistant.Core.Settings.ZipEncryption.ZipCrypto;

    public CentBrowser_PortableAssistant.Core.Settings.ZipEncryption ZipEncryption
    {
        get => _plan.ZipEncryption;
        set
        {
            if (_plan.ZipEncryption == value) return;
            _plan.ZipEncryption = value;
            Changed();
            RaiseHint();
        }
    }

    public string Password
    {
        get => _password;
        set { _password = value; Changed(); }
    }

    public string NameTemplateText
    {
        get => _plan.NameTemplate;
        set { _plan.NameTemplate = value; Changed(); }
    }

    public string NamePrefix
    {
        get => _plan.NamePrefix;
        set { _plan.NamePrefix = value; Changed(); }
    }

    public bool AsciiFileName
    {
        get => _plan.AsciiFileName;
        set { _plan.AsciiFileName = value; Changed(); }
    }

    public string KeepCount
    {
        get => _plan.KeepCount.ToString();
        set
        {
            _plan.KeepCount = int.TryParse(value, out var n) && n >= 0 ? Math.Min(n, 999) : _plan.KeepCount;
            Changed();
        }
    }

    public string? TemplateError
    {
        get
        {
            if (NameTemplate.Validate(_plan.NameTemplate) is { } error) return error;
            return NameTemplate.LengthError(RenderedName());
        }
    }

    /// <summary>按当前设置生成的文件，每种格式一个，带估算大小。</summary>
    public IReadOnlyList<OutputFile> FileNames
    {
        get
        {
            if (NameTemplate.Validate(_plan.NameTemplate) is not null) return [];
            var name = RenderedName();
            var list = new List<OutputFile>();
            void Add(ArchiveFormats f, string ext)
            {
                if (!_plan.Formats.HasFlag(f)) return;
                var size = _rawSize is { } raw ? "约 " + Format.Size(PackEstimate.For(f, raw, _ratios)) : "";
                list.Add(new OutputFile(name + ext, size));
            }
            Add(ArchiveFormats.Sfx, ".exe");
            Add(ArchiveFormats.SevenZip, ".7z");
            Add(ArchiveFormats.Zip, ".zip");
            return list;
        }
    }

    private string RenderedName() => NameTemplate.Render(_plan.NameTemplate, new NameValues(
        _plan.NamePrefix, _plan.Name, _plan.Slug, DateTime.Now, _plan.IncludeProgram,
        _install?.Version ?? "0.0.0.0", Environment.MachineName), _plan.AsciiFileName);

    /// <summary>页面后台算好的未提取大小，算好前为 null。</summary>
    public long? RawSize
    {
        get => _rawSize;
        set
        {
            _rawSize = value;
            Raise();
            Raise(nameof(FileNames));
            Raise(nameof(PackedSize));
        }
    }

    /// <summary>行李牌上的"提取后"：按当前这一种格式估算。</summary>
    public string PackedSize
    {
        get
        {
            if (_rawSize is not { } raw) return "…";
            var formats = new[] { ArchiveFormats.Sfx, ArchiveFormats.SevenZip, ArchiveFormats.Zip }.Where(f => _plan.Formats.HasFlag(f)).ToList();
            return formats.Count == 0 ? "—" : "约 " + Format.Size(formats.Sum(f => PackEstimate.For(f, raw, _ratios)));
        }
    }

    /// <summary>行李牌上的"目的地"：勾选的目的地名称。</summary>
    public string Destinations => Targets.Where(t => t.IsChecked).Select(t => t.Title).ToList() is { Count: > 0 } names
        ? string.Join("、", names)
        : "未选";

    /// <summary>"目的地"的悬停提示：每个目的地的完整路径，一行一个。</summary>
    public string DestinationsDetail => Targets.Where(t => t.IsChecked).Select(t => $"{t.Title}：{_locations[t.Key]}").ToList() is { Count: > 0 } lines
        ? string.Join("\n", lines)
        : "第 6 步还没选目的地";

    /// <summary>保存前要用户补的东西，没有则为 null。</summary>
    public string? Problem
    {
        get
        {
            if (string.IsNullOrEmpty(_password)) return "还没设置解压密码";
            if (_plan.Formats == ArchiveFormats.None) return "至少选一种打包格式";
            if (FormatZip && !Packer.IsZipSafePassword(_password)) return Packer.ZipPasswordRule;
            if (Profiles.Count > 0 && !Profiles.Any(p => p.IsChecked)) return "至少选一个用户";
            if (!Targets.Any(t => t.IsChecked)) return _hiddenTargets.Count > 0 ? "选中的目的地都停用了" : "至少选一个目的地";
            if (Targets.FirstOrDefault(t => t.IsChecked && UploaderFactory.IsBlankLocation(t.Tip)) is { } missing)
                return $"“{missing.Title}”{missing.Tip}";
            return TemplateError;
        }
    }

    private void RaiseHint()
    {
        Raise(nameof(FormatHint));
        Raise(nameof(FormatHintDanger));
    }
}
