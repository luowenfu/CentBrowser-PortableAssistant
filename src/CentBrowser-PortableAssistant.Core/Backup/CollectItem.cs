namespace CentBrowser_PortableAssistant.Core.Backup;

public enum CollectKind
{
    /// <summary>原样复制文件或整个目录。</summary>
    Copy,
    /// <summary>Local State，复制时去掉本机密钥并只留选中的用户。</summary>
    LocalState,
    /// <summary>Preferences 或 Secure Preferences，复制时去掉未选的扩展。</summary>
    Preferences,
    /// <summary>Web Data，只留搜索引擎，清掉自动填充、支付和令牌。</summary>
    SearchEngines,
    /// <summary>空的便携标记文件。</summary>
    PortableMarker,
    /// <summary>空的 First Run，表示这套用户数据已经启动过。</summary>
    FirstRun,
    /// <summary>Last Version，写入打包时的浏览器版本。</summary>
    LastVersion,
}

/// <summary>Source 是绝对路径（文件或目录），Dest 是相对打包根目录的路径。</summary>
public sealed record CollectItem(string Source, string Dest, CollectKind Kind, bool IsProgram = false);
