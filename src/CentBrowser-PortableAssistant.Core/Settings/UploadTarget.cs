using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Core.Settings;

public enum TargetKind
{
    LocalFolder,
    Smb,
    WebDav,
    Ftp,
    FtpsExplicit,
    FtpsImplicit,
    /// <summary>先试显式 FTPS，服务器不支持就退回不加密。放在最后是为了不改旧配置里已有的值。</summary>
    FtpAuto,
}

/// <summary>备份要送达的地方：本地文件夹、SMB、WebDAV 或 FTP/FTPS。</summary>
public sealed class UploadTarget
{
    public string Id { get; set; } = BackupPlan.NewId();

    public string Name { get; set; } = "";

    public TargetKind Kind { get; set; } = TargetKind.WebDav;

    /// <summary>本地文件夹路径、SMB 服务器、WebDAV 地址（https://...）或 FTP 主机名。</summary>
    public string Address { get; set; } = "";

    /// <summary>0 表示使用默认端口。</summary>
    public int Port { get; set; }

    public string UserName { get; set; } = "";

    public string PasswordProtected { get; set; } = "";

    /// <summary>远程目录。新建时只预填根目录。</summary>
    public string RemoteDir { get; set; } = "/";

    public bool AcceptAnyCertificate { get; set; }

    /// <summary>停用后不接收新备份。队列里已经排上的，会等重新启用再传。</summary>
    public bool Disabled { get; set; }

    public UploadTarget Clone() => (UploadTarget)MemberwiseClone();

    /// <summary>给人看的完整位置，例如 D:\Backup 或 ftp://host:21/backup。</summary>
    public string Location
    {
        get
        {
            var address = Address.Trim();
            if (address.Length == 0) return UploaderFactory.MissingAddress(Kind);
            var dir = "/" + RemoteDir.Trim().Trim('/');
            return Kind switch
            {
                TargetKind.LocalFolder => address,
                TargetKind.Smb => SmbPath.Format(address, RemoteDir),
                TargetKind.WebDav => address.TrimEnd('/') + (dir == "/" ? "" : dir),
                _ => $"{(Kind is TargetKind.Ftp or TargetKind.FtpAuto ? "ftp" : "ftps")}://{address}{(Port > 0 ? $":{Port}" : "")}{(dir == "/" ? "" : dir)}",
            };
        }
    }

    public static string KindLabel(TargetKind kind) => kind switch
    {
        TargetKind.LocalFolder => "本地文件夹",
        TargetKind.Smb => "SMB",
        TargetKind.WebDav => "WebDAV",
        TargetKind.Ftp => "FTP",
        TargetKind.FtpAuto => "FTP 自动",
        TargetKind.FtpsExplicit => "FTPS 显式",
        TargetKind.FtpsImplicit => "FTPS 隐式",
        _ => kind.ToString(),
    };
}
