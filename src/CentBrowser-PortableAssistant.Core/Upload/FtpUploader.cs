using FluentFTP;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>FTP / FTPS。本地打好的包按原文件名直接上传。</summary>
public sealed class FtpUploader : IUploader
{
    private readonly AsyncFtpClient _client;
    private readonly string _dir;

    public FtpUploader(UploadTarget target, string password, int uploadKbps = 0)
    {
        var config = new FtpConfig
        {
            // FluentFTP 自带限速，单位就是 KB/s，0 表示不限
            UploadRateLimit = (uint)Math.Max(uploadKbps, 0),
            EncryptionMode = target.Kind switch
            {
                TargetKind.FtpsExplicit => FtpEncryptionMode.Explicit,
                TargetKind.FtpsImplicit => FtpEncryptionMode.Implicit,
                TargetKind.FtpAuto => FtpEncryptionMode.Auto,
                _ => FtpEncryptionMode.None,
            },
            ValidateAnyCertificate = target.AcceptAnyCertificate,
            ConnectTimeout = 15000,
        };
        var port = target.Port > 0 ? target.Port : target.Kind == TargetKind.FtpsImplicit ? 990 : 21;
        var host = target.Address.Trim();
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("要填服务器");
        if (string.IsNullOrWhiteSpace(target.UserName)) throw new ArgumentException("要填用户名");
        _client = new AsyncFtpClient(host, target.UserName, password, port, config);
        _dir = UploaderFactory.NormalizeDir(target.RemoteDir);
    }

    /// <summary>连上之后，这次连接实际有没有加密。</summary>
    public bool IsEncrypted => _client.IsEncrypted;

    public Task TestAsync(CancellationToken ct) => UploadProbe.RunAsync(this, ct);

    public async Task<string> UploadAsync(string localPath, string fileName, IProgress<long>? progress, CancellationToken ct)
    {
        await PrepareAsync(ct);
        var name = await UniqueName.Pick(fileName, (candidate, token) => _client.FileExists($"{_dir}/{candidate}", token), ct);
        var final = $"{_dir}/{name}";
        var size = new FileInfo(localPath).Length;
        var ftpProgress = progress is null ? null : new Progress<FtpProgress>(p => progress.Report(p.TransferredBytes));
        try
        {
            var status = await _client.UploadFile(localPath, final, FtpRemoteExists.Overwrite, false, FtpVerify.None, ftpProgress, ct);
            if (status == FtpStatus.Failed) throw new IOException("上传失败");
            var remoteSize = await _client.GetFileSize(final, -1, ct);
            if (remoteSize >= 0 && remoteSize != size) throw new IOException("传上去的文件大小不一致");
            return final;
        }
        catch (Exception ex) when (UploadErrors.IsExpected(ex) && !ct.IsCancellationRequested)
        {
            await RemovePartialAsync(final, size, ct);
            throw;
        }
    }

    /// <summary>大小已经对上的文件留着。对不上的半截删掉，避免下次把它当成完整备份。</summary>
    private async Task RemovePartialAsync(string remotePath, long expected, CancellationToken ct)
    {
        try
        {
            var remoteSize = await _client.GetFileSize(remotePath, -1, ct);
            if (remoteSize == expected) return;
            if (await _client.FileExists(remotePath, ct)) await _client.DeleteFile(remotePath, ct);
        }
        catch (Exception)
        {
            // 清理失败不能盖住原来的上传错误
        }
    }

    private async Task PrepareAsync(CancellationToken ct)
    {
        await ConnectAsync(ct);
        if (_dir.Length > 0 && !await _client.DirectoryExists(_dir, ct)) throw new IOException("远程目录不存在");
    }

    public async Task DeleteAsync(string remotePath, CancellationToken ct)
    {
        await ConnectAsync(ct);
        if (await _client.FileExists(remotePath, ct)) await _client.DeleteFile(remotePath, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected) await _client.Disconnect();
        _client.Dispose();
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (!_client.IsConnected) await _client.Connect(ct);
    }
}
