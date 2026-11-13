namespace CentBrowser_PortableAssistant.ViewModels;

/// <summary>勾选列表里的一行：用户、扩展或网盘。</summary>
public sealed class CheckItem(string key, string title, string detail = "", string extra = "") : ObservableObject
{
    private bool _isChecked;

    public string Key { get; } = key;

    public string Title { get; } = title;

    /// <summary>标题旁边的次要信息，例如版本号或目录名。</summary>
    private string _detail = detail;
    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    /// <summary>右侧对齐的数字，例如大小。</summary>
    public string Extra { get; } = extra;

    private string _tip = "";

    /// <summary>悬停类型时显示的路径或地址。</summary>
    public string Tip
    {
        get => _tip;
        set => Set(ref _tip, value);
    }

    /// <summary>本地文件夹和 SMB 能在资源管理器里打开，类型后面显示“打开”。</summary>
    public bool CanOpen { get; init; }

    public bool Muted { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value)) Toggled?.Invoke();
        }
    }

    public event Action? Toggled;
}
