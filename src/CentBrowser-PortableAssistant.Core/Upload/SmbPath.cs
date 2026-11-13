namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>把服务器和共享路径拼成 \\电脑\共享\子目录。</summary>
public static class SmbPath
{
    public static string Format(string address, string remoteDir)
    {
        var raw = address.Trim();
        if (raw.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)) raw = raw["smb://".Length..];
        raw = raw.Trim().TrimEnd('\\', '/');

        var unc = raw.StartsWith(@"\\") || raw.StartsWith("//")
            ? @"\\" + raw.TrimStart('\\', '/').Replace('/', '\\')
            : @"\\" + raw.Replace('/', '\\');

        var extra = UploaderFactory.NormalizeDir(remoteDir).Trim('/');
        return extra.Length == 0 ? unc : unc + @"\" + extra.Replace('/', '\\');
    }

    /// <summary>连接时只用 \\电脑\共享，子目录留给复制。</summary>
    public static string ShareRoot(string unc)
    {
        var parts = unc.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException(@"服务器要写到共享名，例如 \\192.168.1.10\home");
        return @"\\" + parts[0] + @"\" + parts[1];
    }
}
