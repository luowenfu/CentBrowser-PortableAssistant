namespace CentBrowser_PortableAssistant.Core.Backup;

public enum CollectKind
{
    /// <summary>原样复制文件或整个目录。</summary>
    Copy,
    /// <summary>Local State，复制时去掉本机密钥并只留选中的用户。</summary>
    LocalState,
    /// <summary>Preferences 或 Secure Preferences，复制时去掉未选的扩展。</summary>
    Preferences,
    /// <summary>空的便携标记文件。</summary>
    PortableMarker,
}

/// <summary>Source 是绝对路径（文件或目录），Dest 是相对打包根目录的路径。</summary>
public sealed record CollectItem(string Source, string Dest, CollectKind Kind, bool IsProgram = false);
