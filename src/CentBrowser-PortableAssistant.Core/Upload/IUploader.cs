namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>把本地打好的包按原文件名直接上传。中途失败时删掉大小对不上的半截文件。</summary>
public interface IUploader : IAsyncDisposable
{
    Task TestAsync(CancellationToken ct);

    /// <summary>progress 收到已传字节数。返回远程完整路径，清理旧备份时用它删除。</summary>
    Task<string> UploadAsync(string localPath, string fileName, IProgress<long>? progress, CancellationToken ct);

    Task DeleteAsync(string remotePath, CancellationToken ct);
}
