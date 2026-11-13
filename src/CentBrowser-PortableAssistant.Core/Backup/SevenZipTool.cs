using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>7-Zip 打包在程序里，第一次用时释放到本地目录。</summary>
public static partial class SevenZipTool
{
    private static readonly string[] Files = ["7z.exe", "7z.dll", "7z.sfx", "License.txt"];
    private static readonly object Gate = new();

    public static string ExePath => Path.Combine(AppPaths.ToolsDir, "7z.exe");

    public static string SfxPath => Path.Combine(AppPaths.ToolsDir, "7z.sfx");

    public static void EnsureExtracted()
    {
        lock (Gate)
        {
            Directory.CreateDirectory(AppPaths.ToolsDir);
            var asm = typeof(SevenZipTool).Assembly;
            foreach (var name in Files)
            {
                using var res = asm.GetManifestResourceStream("7zip." + name)
                    ?? throw new InvalidOperationException($"程序里缺少 7-Zip 文件 {name}");
                var path = Path.Combine(AppPaths.ToolsDir, name);
                if (File.Exists(path) && new FileInfo(path).Length == res.Length) continue;
                using var fs = File.Create(path);
                res.CopyTo(fs);
            }
        }
    }

    /// <summary>
    /// 运行 7z，progress 收到 0 到 100 的百分比。失败时抛出带 7-Zip 原话的异常。
    /// 密码从标准输入送进去：命令行里的引号会被 7-Zip 误解析，而且命令行别的程序能看到。
    /// </summary>
    public static async Task RunAsync(IEnumerable<string> args, string? password, IProgress<int>? progress, CancellationToken ct, bool lowPriority = false)
    {
        EnsureExtracted();
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        var list = args.ToList();
        foreach (var a in list) psi.ArgumentList.Add(a);
        // 建包时空的 -p 表示"提示输入密码"；解压和测试时空的 -p 表示"密码为空"，所以不加，让 7-Zip 遇到加密自己提示
        if (password is not null && list.FirstOrDefault() == "a") psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("-bsp1");
        psi.ArgumentList.Add("-bso0");
        psi.ArgumentList.Add("-bse2");
        psi.ArgumentList.Add("-sccUTF-8");

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 7-Zip");
        // 后台打包时浏览器可能开着，让着前台程序
        if (lowPriority)
        {
            try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (InvalidOperationException) { }
        }
        if (password is not null) await proc.StandardInput.WriteLineAsync(password);
        proc.StandardInput.Close();
        using var kill = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });

        var errTask = proc.StandardError.ReadToEndAsync(CancellationToken.None);
        var buffer = new char[256];
        var pending = new StringBuilder();
        int read;
        while ((read = await proc.StandardOutput.ReadAsync(buffer, CancellationToken.None)) > 0)
        {
            pending.Append(buffer, 0, read);
            var matches = PercentPattern().Matches(pending.ToString());
            if (matches.Count > 0)
            {
                progress?.Report(int.Parse(matches[^1].Groups[1].Value));
                pending.Clear();
            }
            else if (pending.Length > 4096)
            {
                pending.Clear();
            }
        }
        await proc.WaitForExitAsync(CancellationToken.None);
        var err = (await errTask).Trim();
        ct.ThrowIfCancellationRequested();

        // 退出码 1 只是警告（例如个别文件被跳过），压缩包仍然完整
        if (proc.ExitCode > 1)
        {
            throw new InvalidOperationException($"7-Zip 出错（代码 {proc.ExitCode}）：{(err.Length > 0 ? err : "没有更多信息")}");
        }
    }

    [GeneratedRegex(@"(\d{1,3})%")]
    private static partial Regex PercentPattern();
}
