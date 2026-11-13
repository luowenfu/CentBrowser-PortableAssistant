using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>
/// 用要带走的文件的修改时间和大小算一个指纹。只看选中用户的文件，
/// 所以只用了别的用户时指纹不变，不会产生重复备份。
/// </summary>
public static class Fingerprint
{
    public static string Compute(BrowserInstall install, BackupPlan plan, IReadOnlyList<CollectItem> items)
    {
        var sb = new StringBuilder();
        // 改了打包内容（比如多勾了一个扩展）也算变化；密码每次加密结果都不同，不能参与
        var content = plan.Clone();
        content.PasswordProtected = "";
        content.TargetIds = [];
        content.KeepCount = 0;
        content.AutoBackup = false;
        sb.Append(JsonSerializer.Serialize(content, JsonFile.Options)).Append('\n');
        if (plan.IncludeProgram) sb.Append("program=").Append(install.Version).Append('\n');

        foreach (var item in items.Where(i => !i.IsProgram && i.Kind != CollectKind.PortableMarker).OrderBy(i => i.Dest, StringComparer.Ordinal))
        {
            if (item.Kind is CollectKind.FirstRun or CollectKind.LastVersion)
            {
                sb.Append(item.Dest).Append('|').Append(item.Kind == CollectKind.LastVersion ? install.Version : "0").Append('\n');
                continue;
            }
            if (Directory.Exists(item.Source))
            {
                foreach (var f in new DirectoryInfo(item.Source).EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(f => f.FullName, StringComparer.Ordinal))
                    Append(sb, f);
            }
            else if (File.Exists(item.Source))
            {
                Append(sb, new FileInfo(item.Source));
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static void Append(StringBuilder sb, FileInfo f) =>
        sb.Append(f.FullName).Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks).Append('\n');
}
