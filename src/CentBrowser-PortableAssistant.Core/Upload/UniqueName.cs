namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>目的地已有同名文件时，新文件用「名字 (2).扩展名」。</summary>
public static class UniqueName
{
    public static async Task<string> Pick(string fileName, Func<string, CancellationToken, Task<bool>> exists, CancellationToken ct)
    {
        if (!await exists(fileName, ct)) return fileName;
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var n = 2; n < 100; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!await exists(candidate, ct)) return candidate;
        }
        return $"{stem} ({DateTime.Now:HHmmss}){ext}";
    }
}
