using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>通过 Windows 共享（SMB）复制文件。填了账号就临时连上，用完断开。</summary>
public sealed class SmbUploader : IUploader
{
    private readonly LocalFolderUploader _files;
    private readonly string _folder;
    private readonly string? _share;
    private readonly bool _ownsConnection;

    public SmbUploader(UploadTarget target, string password, int uploadKbps = 0)
    {
        _folder = SmbPath.Format(target.Address, target.RemoteDir);
        _share = SmbPath.ShareRoot(_folder);
        _files = new LocalFolderUploader(_folder, uploadKbps);
        if (string.IsNullOrWhiteSpace(target.UserName)) return;

        var resource = new SmbNative.NetResource { Type = SmbNative.TypeDisk, RemoteName = _share };
        var code = SmbNative.WNetAddConnection2(ref resource, password, target.UserName.Trim(), SmbNative.ConnectTemporary);
        if (code == 0)
        {
            _ownsConnection = true;
            return;
        }
        // Windows 不允许同一台电脑用两个账号连同一台服务器，只能沿用已有连接。
        // 系统查不到已有连接在服务器上用的是哪个账号，所以这时填的用户名和密码没有被验证
        if (code is 85 or 1219 && Directory.Exists(_share))
        {
            ReusedConnection = true;
            return;
        }
        throw new IOException(Explain(code));
    }

    /// <summary>为 true 表示沿用了这台电脑已有的连接，填的用户名和密码没有验证。</summary>
    public bool ReusedConnection { get; }

    public Task TestAsync(CancellationToken ct) => _files.TestAsync(ct);

    public Task<string> UploadAsync(string localPath, string fileName, IProgress<long>? progress, CancellationToken ct) =>
        _files.UploadAsync(localPath, fileName, progress, ct);

    public Task DeleteAsync(string remotePath, CancellationToken ct) => _files.DeleteAsync(remotePath, ct);

    public ValueTask DisposeAsync()
    {
        if (_ownsConnection && _share is not null) SmbNative.WNetCancelConnection2(_share, 0, false);
        return ValueTask.CompletedTask;
    }

    private static string Explain(int code) => code switch
    {
        // 错误码含义见微软文档 System Error Codes 和 WNetAddConnection2
        86 or 1326 or 1327 => "用户名或密码不对",
        1330 => "这个账号的密码已过期",
        1331 => "这个账号被停用了",
        1909 => "这个账号被锁定了，稍后再试或到服务器上解锁",
        1385 => "这个账号没有被允许从网络登录",
        5 or 65 => "没有权限。账号对这个共享没有访问权限；只允许访客登录的设备，Windows 默认也会拒绝，要填用户名和密码",
        67 => "服务器能连上，但找不到这个共享名",
        53 => "找不到这台电脑。检查电脑名或 IP。只支持 SMB1 的老设备，Windows 默认连不上",
        1203 => "找不到这台电脑或这个共享",
        1311 or 1355 => "找不到域或域服务器，域账号要写成 域名\\用户名",
        64 or 121 or 1225 or 1231 or 1232 => "连不上这台服务器。检查网络，以及服务器有没有开启 SMB",
        1219 => "共享名可能写错了，或这台电脑已用别的账号连着这台服务器。先断开旧连接再试",
        1222 => "网络没有连上",
        _ => "连不上这台电脑",
    };
}
