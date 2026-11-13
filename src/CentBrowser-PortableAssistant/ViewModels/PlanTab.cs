namespace CentBrowser_PortableAssistant.ViewModels;

/// <summary>方案切换栏上的一项。改名时只更新文字，不打断正在编辑的方案。</summary>
public sealed class PlanTab(string id, string name) : ObservableObject
{
    private string _name = name;
    private bool _isEditing;

    public string Id { get; } = id;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>双击标签后变成输入框改名。</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => Set(ref _isEditing, value);
    }
}
