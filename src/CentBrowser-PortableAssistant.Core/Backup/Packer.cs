using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>把暂存目录打成方案要的格式。自解压 exe 就是 7z.sfx 后面接上 .7z 数据。</summary>
public static class Packer
{
    public const string ZipPasswordRule = "选了 .zip 格式时，密码只能用英文字母、数字和符号";

    /// <summary>7-Zip 生成 zip 时不接受非英文字符的密码。</summary>
    public static bool IsZipSafePassword(string password) => password.All(c => c is >= ' ' and <= '~');

    public static async Task<IReadOnlyList<string>> PackAsync(
        string stagingRoot,
        string outputDir,
        string baseName,
        ArchiveFormats formats,
        ZipEncryption zipEncryption,
        string password,
        IProgress<int>? progress,
        CancellationToken ct,
        bool lowPriority = false)
    {
        if (formats == ArchiveFormats.None) throw new ArgumentException("至少选一种打包格式");
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("解压密码不能为空");
        if (formats.HasFlag(ArchiveFormats.Zip) && !IsZipSafePassword(password)) throw new ArgumentException(ZipPasswordRule);
        Directory.CreateDirectory(outputDir);

        var needSevenZip = formats.HasFlag(ArchiveFormats.SevenZip) || formats.HasFlag(ArchiveFormats.Sfx);
        var steps = (needSevenZip ? 1 : 0) + (formats.HasFlag(ArchiveFormats.Zip) ? 1 : 0);
        var done = 0;
        IProgress<int> Step() => new Progress<int>(p => progress?.Report((done * 100 + p) / steps));

        var outputs = new List<string>();
        var source = Path.Combine(stagingRoot, "*");

        if (needSevenZip)
        {
            var sevenZip = Path.Combine(outputDir, baseName + ".7z");
            File.Delete(sevenZip);
            await SevenZipTool.RunAsync(
                ["a", "-t7z", "-mx=5", "-mmt=on", "-mhe=on", sevenZip, source],
                password, Step(), ct, lowPriority);
            done++;

            if (formats.HasFlag(ArchiveFormats.Sfx))
            {
                var exe = Path.Combine(outputDir, baseName + ".exe");
                await WriteSfxAsync(sevenZip, exe, ct);
                outputs.Add(exe);
            }
            if (formats.HasFlag(ArchiveFormats.SevenZip)) outputs.Add(sevenZip);
            else File.Delete(sevenZip);
        }

        if (formats.HasFlag(ArchiveFormats.Zip))
        {
            var zip = Path.Combine(outputDir, baseName + ".zip");
            File.Delete(zip);
            var method = zipEncryption == ZipEncryption.Aes256 ? "-mem=AES256" : "-mem=ZipCrypto";
            await SevenZipTool.RunAsync(
                ["a", "-tzip", "-mx=5", "-mmt=on", method, zip, source],
                password, Step(), ct, lowPriority);
            outputs.Add(zip);
        }

        progress?.Report(100);
        return outputs;
    }

    private static async Task WriteSfxAsync(string sevenZip, string exe, CancellationToken ct)
    {
        SevenZipTool.EnsureExtracted();
        await using var output = File.Create(exe);
        await using (var sfx = File.OpenRead(SevenZipTool.SfxPath)) await sfx.CopyToAsync(output, ct);
        await using (var data = File.OpenRead(sevenZip)) await data.CopyToAsync(output, ct);
    }
}
