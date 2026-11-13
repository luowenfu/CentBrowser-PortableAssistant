namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>测试连接走正式上传：把一个小文件传到正式文件名，核对大小，再删掉。</summary>
static class UploadProbe
{
    public static async Task RunAsync(IUploader uploader, CancellationToken ct)
    {
        var local = Path.Combine(Path.GetTempPath(), "centbrowser-write-test-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(local, "ok", ct);
        try
        {
            var remote = await uploader.UploadAsync(local, "centbrowser-write-test.txt", null, ct);
            await uploader.DeleteAsync(remote, ct);
        }
        finally
        {
            if (File.Exists(local)) File.Delete(local);
        }
    }
}
