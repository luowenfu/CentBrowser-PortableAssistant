namespace CentBrowser_PortableAssistant.Core.Browser;

/// <summary>一个已安装的扩展。DataSize 是扩展自身存的数据，不含程序文件。</summary>
public sealed record ExtensionInfo(
    string Id,
    string Name,
    string Version,
    bool Enabled,
    long ProgramSize,
    long DataSize);
