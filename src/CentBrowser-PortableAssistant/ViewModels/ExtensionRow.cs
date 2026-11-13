using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Services;

namespace CentBrowser_PortableAssistant.ViewModels;

/// <summary>扩展表格里的一行。</summary>
public sealed class ExtensionRow(ExtensionInfo info) : ObservableObject
{
    private bool _isChecked;
    private bool _includeData = true;

    public string Id { get; } = info.Id;

    public string Name { get; } = info.Name;

    public string Version { get; } = info.Version;

    public bool Enabled { get; } = info.Enabled;

    public string Status => Enabled ? "已启用" : "已停用";

    public long ProgramBytes { get; } = info.ProgramSize;

    public long DataBytes { get; } = info.DataSize;

    public string ProgramSize { get; } = Format.Size(info.ProgramSize);

    public string DataSize { get; } = info.DataSize > 0 ? Format.Size(info.DataSize) : "—";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!Set(ref _isChecked, value)) return;
            IncludeData = value;
            Toggled?.Invoke();
        }
    }

    /// <summary>带上这个扩展自己存的设置和内容。默认带上。</summary>
    public bool IncludeData
    {
        get => _includeData;
        set
        {
            if (Set(ref _includeData, value)) DataToggled?.Invoke();
        }
    }

    public event Action? Toggled;

    public event Action? DataToggled;
}
