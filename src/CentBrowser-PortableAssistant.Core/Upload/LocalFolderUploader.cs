namespace CentBrowser_PortableAssistant.Core.Upload;

public sealed class LocalFolderUploader : IUploader
{
    private readonly string _folder;
    private readonly int _uploadKbps;

    /// <summary>uploadKbps 是复制限速，0 表示不限。走共享时由 SmbUploader 传进来。</summary>
    public LocalFolderUploader(string folder, int uploadKbps = 0)
    {
        _folder = Require(folder);
        _uploadKbps = uploadKbps;
    }

    /// <summary>只接受 D:\备份 或 \\电脑\共享 这种完整路径。</summary>
    public static string Require(string folder)
    {
        folder = folder.Trim().Trim('"');
        if (folder.Contains('/')) throw new ArgumentException(@"路径要用反斜杠，例如 D:\备份");
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException(@"要填完整路径，例如 D:\备份");
        try
        {
            return Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("路径不对");
        }
    }

    public async Task TestAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_folder)) throw new DirectoryNotFoundException("文件夹不存在");
        await UploadProbe.RunAsync(this, ct);
    }

    public async Task<string> UploadAsync(string localPath, string fileName, IProgress<long>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(_folder)) throw new DirectoryNotFoundException("文件夹不存在");
        var name = await UniqueName.Pick(fileName, (candidate, _) => Task.FromResult(File.Exists(Path.Combine(_folder, candidate))), ct);
        var final = Path.Combine(_folder, name);
        var size = new FileInfo(localPath).Length;
        var opened = false;
        try
        {
            await using (var src = ThrottledStream.Wrap(File.OpenRead(localPath), _uploadKbps))
            await using (var dst = File.Create(final))
            {
                opened = true;
                var buffer = new byte[1 << 20];
                long total = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    progress?.Report(total);
                }
            }
            if (new FileInfo(final).Length != size) throw new IOException("复制后大小不一致");
            return final;
        }
        catch (Exception ex) when (UploadErrors.IsExpected(ex) && !ct.IsCancellationRequested)
        {
            // 还没打开目标文件时，原来的文件没被动过，不要删
            if (opened && File.Exists(final) && new FileInfo(final).Length != size) File.Delete(final);
            throw;
        }
    }

    public Task DeleteAsync(string remotePath, CancellationToken ct)
    {
        if (File.Exists(remotePath)) File.Delete(remotePath);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
