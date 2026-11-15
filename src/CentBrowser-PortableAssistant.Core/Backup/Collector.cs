using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>把清单里的文件复制到暂存目录，按需改写 JSON。</summary>
public static class Collector
{
    /// <summary>LevelDB 的锁和日志、SQLite 的日志，留着反而可能让浏览器误判。</summary>
    private static readonly string[] SkippedFileNames = ["LOCK", "LOG", "LOG.old", "lockfile"];

    /// <param name="progress">已复制字节的百分比。不传就不先统计大小。0 表示统计完、还没开始复制。</param>
    public static long Collect(
        BrowserInstall install,
        BackupPlan plan,
        IReadOnlyList<string> profiles,
        IReadOnlyList<CollectItem> items,
        string stagingRoot,
        CancellationToken ct,
        IProgress<int>? progress = null)
    {
        // 先扫体积再复制。扫的时候调用方仍显示没有百分比的「正在提取」
        var tracker = progress is null ? null : Track(install, items, progress, ct);
        long bytes = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(stagingRoot, item.Dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            switch (item.Kind)
            {
                case CollectKind.PortableMarker:
                case CollectKind.FirstRun:
                    File.WriteAllBytes(dest, []);
                    break;
                case CollectKind.LastVersion:
                    File.WriteAllText(dest, install.Version);
                    tracker?.Advance(Encoding.UTF8.GetByteCount(install.Version));
                    break;
                case CollectKind.LocalState:
                    var keepKey = profiles.Any(dir => plan.OptionsFor(dir).IncludeSecrets);
                    WriteJson(item.Source, dest, n => JsonTrimmer.TrimLocalState(n, profiles, install.Version, keepKey));
                    tracker?.Advance(FileSize(item.Source));
                    break;
                case CollectKind.SearchEngines:
                    bytes += CopyFile(item.Source, dest, ct, tracker);
                    foreach (var ext in new[] { "-wal", "-journal", "-shm" })
                    {
                        var side = item.Source + ext;
                        if (!File.Exists(side)) continue;
                        bytes += CopyFile(side, dest + ext, ct, tracker);
                    }
                    WebDataTrimmer.KeepSearchEngines(dest);
                    foreach (var ext in new[] { "-wal", "-journal", "-shm" })
                    {
                        var side = dest + ext;
                        if (File.Exists(side)) File.Delete(side);
                    }
                    break;
                case CollectKind.Preferences:
                    var profileDir = Path.GetDirectoryName(item.Source)!;
                    var options = plan.OptionsFor(Path.GetFileName(profileDir));
                    var keep = options.IncludeExtensions
                        ? CollectPlanner.SelectedExtensions(profileDir, options).ToHashSet()
                        : [];
                    WriteJson(item.Source, dest, n => JsonTrimmer.TrimPreferences(n, keep, options.IncludeSettings));
                    tracker?.Advance(FileSize(item.Source));
                    break;
                default:
                    bytes += Directory.Exists(item.Source)
                        ? CopyDir(item.Source, dest, ct, tracker)
                        : CopyFile(item.Source, dest, ct, tracker);
                    break;
            }
        }
        tracker?.Finish();
        return bytes;
    }

    /// <summary>要复制的总字节。改写的 JSON 按原文件大小算，目录里的锁和日志不算。</summary>
    private static ByteProgress? Track(BrowserInstall install, IReadOnlyList<CollectItem> items, IProgress<int> progress, CancellationToken ct)
    {
        long total = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            total += item.Kind switch
            {
                CollectKind.LastVersion => Encoding.UTF8.GetByteCount(install.Version),
                CollectKind.LocalState or CollectKind.Preferences or CollectKind.SearchEngines => FileSize(item.Source),
                CollectKind.PortableMarker or CollectKind.FirstRun => 0,
                _ => Directory.Exists(item.Source) ? DirSize(item.Source, ct) : FileSize(item.Source),
            };
        }
        if (total <= 0) return null;
        progress.Report(0);
        return new ByteProgress(total, progress);
    }

    private static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void WriteJson(string source, string dest, Func<JsonNode, JsonNode> transform)
    {
        var node = ProfileReader.ReadJson(source)
            ?? throw new InvalidDataException($"读不懂 {Path.GetFileName(source)}，文件可能已损坏：{source}");
        File.WriteAllText(dest, transform(node).ToJsonString(JsonSerializerOptions.Default));
    }

    private static long DirSize(string source, CancellationToken ct)
    {
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (Skip(file)) continue;
            bytes += new FileInfo(file).Length;
        }
        return bytes;
    }

    private static bool Skip(string path) =>
        SkippedFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    private static long CopyDir(string source, string dest, CancellationToken ct, ByteProgress? progress)
    {
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (Skip(file)) continue;
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            bytes += CopyFile(file, target, ct, progress);
        }
        return bytes;
    }

    /// <summary>小文件整份复制。大文件分段写，取消后不用等这一份复制完。</summary>
    private static long CopyFile(string source, string dest, CancellationToken ct, ByteProgress? progress)
    {
        ct.ThrowIfCancellationRequested();
        var length = new FileInfo(source).Length;
        if (length <= 1024 * 1024)
        {
            File.Copy(source, dest, overwrite: true);
            progress?.Advance(length);
            return length;
        }

        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            total += read;
            progress?.Advance(read);
        }
        return total;
    }

    /// <summary>按统计好的总字节报百分比。只在整数变化时上报。</summary>
    private sealed class ByteProgress(long total, IProgress<int> progress)
    {
        private long _done;
        private int _reported;

        public void Advance(long bytes)
        {
            if (bytes <= 0) return;
            _done += bytes;
            var percent = (int)Math.Min(100, _done * 100 / total);
            if (percent == _reported) return;
            _reported = percent;
            progress.Report(percent);
        }

        public void Finish()
        {
            if (_reported == 100) return;
            _reported = 100;
            progress.Report(100);
        }
    }
}
