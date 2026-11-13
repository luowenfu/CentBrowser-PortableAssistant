using System.Diagnostics;
using System.Security.Cryptography;

namespace CentBrowser_PortableAssistant.Core.Update;

public enum DownloadOutcome
{
    Done,
    Failed,
    Canceled,
}

/// <summary>把安装包直接下到原 exe 旁边的 .new。30 秒收不到数据算失败。</summary>
public static class UpdateDownloader
{
    public static async Task<DownloadOutcome> DownloadAsync(
        string url,
        string dest,
        long? knownSize,
        string? digest,
        string versionLabel,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        var ok = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await UpdateHttp.SendFileAsync(request, TimeSpan.FromSeconds(30), ct);
            if (!response.IsSuccessStatusCode) return DownloadOutcome.Failed;
            var length = response.Content.Headers.ContentLength;
            if (knownSize is { } expected && length is { } actual && expected != actual) return DownloadOutcome.Failed;
            var total = knownSize ?? length;
            if (total is null or <= 0) return DownloadOutcome.Failed;

            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long got = 0;
                var mz = false;
                var reported = -1;
                while (true)
                {
                    int read;
                    try
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        idle.CancelAfter(TimeSpan.FromSeconds(30));
                        read = await input.ReadAsync(buffer, idle.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        return DownloadOutcome.Failed;
                    }
                    if (read == 0) break;
                    if (got == 0) mz = read >= 2 && buffer[0] == (byte)'M' && buffer[1] == (byte)'Z';
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    got += read;
                    var percent = (int)Math.Min(100, got * 100 / total.Value);
                    if (percent != reported)
                    {
                        reported = percent;
                        progress?.Report(percent);
                    }
                }
                if (got != total || !mz) return DownloadOutcome.Failed;
                if (digest is not null)
                {
                    var hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    if (!hash.Equals(digest, StringComparison.Ordinal)) return DownloadOutcome.Failed;
                }
            }
            if (!VersionMatches(dest, versionLabel)) return DownloadOutcome.Failed;
            ok = true;
            return DownloadOutcome.Done;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return DownloadOutcome.Canceled;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return DownloadOutcome.Failed;
        }
        finally
        {
            if (!ok) TryDelete(dest);
        }
    }

    /// <summary>读不出文件版本就跳过。读出来了就必须和标签一致。</summary>
    private static bool VersionMatches(string path, string label)
    {
        try
        {
            var text = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!AppVersion.TryParse(text, out var file, out _)) return true;
            if (!AppVersion.TryParse(label, out var expected, out _)) return true;
            return file.CompareTo(expected) == 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
