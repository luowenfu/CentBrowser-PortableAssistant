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

    public static long Collect(
        BrowserInstall install,
        BackupPlan plan,
        IReadOnlyList<string> profiles,
        IReadOnlyList<CollectItem> items,
        string stagingRoot,
        CancellationToken ct)
    {
        long bytes = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(stagingRoot, item.Dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            switch (item.Kind)
            {
                case CollectKind.PortableMarker:
                    File.WriteAllBytes(dest, []);
                    break;
                case CollectKind.LocalState:
                    WriteJson(item.Source, dest, n => JsonTrimmer.TrimLocalState(n, profiles));
                    break;
                case CollectKind.Preferences:
                    var profileDir = Path.GetDirectoryName(item.Source)!;
                    var options = plan.OptionsFor(Path.GetFileName(profileDir));
                    var keep = options.IncludeExtensions
                        ? CollectPlanner.SelectedExtensions(profileDir, options).ToHashSet()
                        : [];
                    WriteJson(item.Source, dest, n => JsonTrimmer.TrimPreferences(n, keep, options.IncludeSettings));
                    break;
                default:
                    bytes += Directory.Exists(item.Source) ? CopyDir(item.Source, dest, ct) : CopyFile(item.Source, dest, ct);
                    break;
            }
        }
        return bytes;
    }

    private static void WriteJson(string source, string dest, Func<JsonNode, JsonNode> transform)
    {
        var node = ProfileReader.ReadJson(source)
            ?? throw new InvalidDataException($"读不懂 {Path.GetFileName(source)}，文件可能已损坏：{source}");
        File.WriteAllText(dest, transform(node).ToJsonString(JsonSerializerOptions.Default));
    }

    private static long CopyDir(string source, string dest, CancellationToken ct)
    {
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (SkippedFileNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            bytes += CopyFile(file, target, ct);
        }
        return bytes;
    }

    /// <summary>小文件整份复制。大文件分段写，取消后不用等这一份复制完。</summary>
    private static long CopyFile(string source, string dest, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var length = new FileInfo(source).Length;
        if (length <= 1024 * 1024)
        {
            File.Copy(source, dest, overwrite: true);
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
        }
        return total;
    }
}
