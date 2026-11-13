using System.Diagnostics;

namespace CentBrowser_PortableAssistant.Core.Update;

/// <summary>改名替换正在运行的 exe。Windows 允许给正在运行的程序改名。</summary>
public static class ExeSwap
{
    public static bool CanCheck(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        if (Path.GetFileName(exePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var full = Path.GetFullPath(exePath).Replace('/', '\\');
        if (full.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)) return false;
        if (full.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase)) return false;
        if (full.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public static string NewPath(string exe) => exe + ".new";

    public static string OldPath(string exe) => exe + ".old";

    public static bool CanWrite(string exe)
    {
        try
        {
            using var stream = new FileStream(NewPath(exe), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>先把正在运行的文件改成 .old，再把 .new 改成原名。第二步失败会改回去。</summary>
    public static bool TrySwap(string exe)
    {
        var old = OldPath(exe);
        var neu = NewPath(exe);
        TryDelete(old);
        if (!TryMove(exe, old)) return false;
        if (TryMove(neu, exe)) return true;
        TryMove(old, exe);
        return false;
    }

    public static bool TryRename(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return true;
        if (File.Exists(to)) return false;
        return TryMove(from, to);
    }

    public static bool TryStart(string exe, int pid, string? replaced = null)
    {
        var arguments = "--replace " + pid;
        if (ReplacedDiffers(exe, replaced))
            arguments += " --replaced \"" + replaced!.Replace("\"", "") + "\"";
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = true,
            }) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static string? ReplacedPath(string[] args)
    {
        var index = Array.IndexOf(args, "--replaced");
        if (index < 0 || index + 1 >= args.Length) return null;
        var path = args[index + 1];
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>新进程先等旧进程退出，再去拿单实例。进程号已经没了就当它退出了。</summary>
    public static void WaitForPrevious(string[] args)
    {
        var index = Array.IndexOf(args, "--replace");
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pid)) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(15_000);
        }
        catch (ArgumentException)
        {
            // 旧进程已经不在了
        }
    }

    /// <summary>启动完成后再删。删不掉就留到下次，不提示。</summary>
    public static void DeleteStale(string? exe, string? replaced = null)
    {
        DeletePair(exe);
        if (ReplacedDiffers(exe, replaced)) DeletePair(replaced);
    }

    private static void DeletePair(string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return;
        TryDelete(OldPath(exe));
        TryDelete(NewPath(exe));
    }

    private static bool ReplacedDiffers(string? exe, string? replaced) =>
        !string.IsNullOrEmpty(replaced)
        && !string.Equals(replaced, exe, StringComparison.OrdinalIgnoreCase);

    public static void DeleteNew(string exe) => TryDelete(NewPath(exe));

    private static bool TryMove(string from, string to)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (i == 9) return false;
                Thread.Sleep(200);
            }
        }
        return false;
    }

    private static void TryDelete(string path)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (i == 9) return;
                Thread.Sleep(200);
            }
        }
    }
}
