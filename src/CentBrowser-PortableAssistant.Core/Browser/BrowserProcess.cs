using System.ComponentModel;
using System.Diagnostics;

namespace CentBrowser_PortableAssistant.Core.Browser;

public static class BrowserProcess
{
    /// <summary>只认程序目录下的 chrome.exe，别的 Chromium 浏览器不算。</summary>
    public static bool IsRunning(BrowserInstall install)
    {
        var root = install.RootDir.TrimEnd('\\') + "\\";
        foreach (var p in Process.GetProcessesByName("chrome"))
        {
            using (p)
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // 进程已退出，或属于别的账户无权读取
                }
            }
        }
        return false;
    }
}
