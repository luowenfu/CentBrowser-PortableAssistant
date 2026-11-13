namespace CentBrowser_PortableAssistant.Core.Settings;

/// <summary>一个用户要带走的内容和扩展。每个用户各自一份。</summary>
public sealed class ProfileOptions
{
    public bool IncludeBookmarks { get; set; } = true;

    public bool IncludeHistory { get; set; } = true;

    public bool IncludeSettings { get; set; } = true;

    public bool IncludeExtensions { get; set; } = true;

    /// <summary>为 true 时带上这个用户的全部扩展，忽略 ExtensionIds。</summary>
    public bool AllExtensions { get; set; } = true;

    public List<string> ExtensionIds { get; set; } = [];

    /// <summary>为 true 时选中的扩展都带上数据，忽略 ExtensionDataIds。</summary>
    public bool IncludeExtensionData { get; set; } = true;

    /// <summary>IncludeExtensionData 为 false 时，只带这些扩展的数据。</summary>
    public List<string> ExtensionDataIds { get; set; } = [];

    public bool WantsExtensionData(string extensionId) =>
        IncludeExtensionData || ExtensionDataIds.Contains(extensionId);

    public ProfileOptions Clone()
    {
        var copy = (ProfileOptions)MemberwiseClone();
        copy.ExtensionIds = [.. ExtensionIds];
        copy.ExtensionDataIds = [.. ExtensionDataIds];
        return copy;
    }
}
