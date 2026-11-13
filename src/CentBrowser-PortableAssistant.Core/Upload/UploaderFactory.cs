using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

public static class UploaderFactory
{
    /// <summary>uploadKbps 是上传限速，0 表示不限。本机文件夹是磁盘复制，限了反而拖很久，不受限。</summary>
    public static IUploader Create(UploadTarget target, int uploadKbps = 0)
    {
        Check(target);
        var password = SecretProtector.Unprotect(target.PasswordProtected);
        return target.Kind switch
        {
            TargetKind.LocalFolder => new LocalFolderUploader(target.Address),
            TargetKind.Smb => new SmbUploader(target, password, uploadKbps),
            TargetKind.WebDav => new WebDavUploader(target, password, uploadKbps),
            _ => new FtpUploader(target, password, uploadKbps),
        };
    }

    /// <summary>按界面上的示例检查。斜杠写反、服务器写成路径，都在这里拦住。</summary>
    public static void Check(UploadTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Address)) throw new ArgumentException(MissingAddress(target.Kind));
        switch (target.Kind)
        {
            case TargetKind.LocalFolder:
                LocalFolderUploader.Require(target.Address);
                break;
            case TargetKind.Smb:
                SmbPath.ShareRoot(SmbPath.Format(target.Address, target.RemoteDir));
                break;
            case TargetKind.WebDav:
                if (!Uri.TryCreate(target.Address.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                    throw new ArgumentException("地址要以 https:// 开头");
                break;
            default:
                RequireHost(target.Address, "服务器填主机名或 IP");
                RequireRemoteDir(target.RemoteDir);
                if (target.Port is < 0 or > 65535) throw new ArgumentException("端口不对");
                if (string.IsNullOrWhiteSpace(target.UserName)) throw new ArgumentException("要填用户名");
                break;
        }
    }

    private static void RequireHost(string address, string message)
    {
        if (address.Trim().Contains('/') || address.Contains('\\')) throw new ArgumentException(message);
    }

    private static void RequireRemoteDir(string dir)
    {
        var text = dir.Trim();
        if (text.Contains('\\') || !text.StartsWith('/') || text.Contains("//")) throw new ArgumentException("远程目录要写成 /目录");
    }

    public static string MissingAddress(TargetKind kind) => kind switch
    {
        TargetKind.LocalFolder => "还没选文件夹",
        TargetKind.WebDav => "还没填地址",
        _ => "还没填服务器",
    };

    public static bool IsBlankLocation(string location) =>
        location is "还没选文件夹" or "还没填地址" or "还没填服务器";

    /// <summary>把远程目录整理成 /a/b 的形式，空表示根目录。</summary>
    public static string NormalizeDir(string dir)
    {
        var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "" : "/" + string.Join('/', parts);
    }
}
