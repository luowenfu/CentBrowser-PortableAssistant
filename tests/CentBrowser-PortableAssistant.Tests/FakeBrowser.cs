using CentBrowser_PortableAssistant.Core.Browser;
using Microsoft.Data.Sqlite;

namespace CentBrowser_PortableAssistant.Tests;

/// <summary>在临时目录里搭一个最小的便携版 Cent Browser，两个用户，每人两个扩展。</summary>
public sealed class FakeBrowser : IDisposable
{
    public FakeBrowser()
    {
        Root = Path.Combine(Path.GetTempPath(), "cbpa-test-" + Guid.NewGuid().ToString("N")[..8]);
        Write("chrome.exe", "exe");
        Write("wipe.bat", "rem");
        Write("1.2.3.4/chrome.dll", new string('x', 4096));
        Write("1.2.3.4/cbportable", "");
        Write("Dictionaries/en.bdic", "dic");
        Write("User Data/Local State", """
            { "os_crypt": { "encrypted_key": "k" },
              "profile": { "info_cache": { "Profile 1": { "name": "甲" }, "Profile 2": { "name": "乙" } },
                           "last_used": "Profile 2" } }
            """);
        Write("User Data/ShaderCache/data", "cache");
        foreach (var p in new[] { "Profile 1", "Profile 2" }) WriteProfile(p);
        Install = new BrowserInstall(Root, Path.Combine(Root, "1.2.3.4"), "1.2.3.4", Path.Combine(Root, "User Data"));
    }

    public string Root { get; }

    public BrowserInstall Install { get; }

    public string Path_(string rel) => Path.Combine(Root, rel);

    private void WriteProfile(string p)
    {
        var d = $"User Data/{p}";
        Write($"{d}/Bookmarks", "{}");
        Write($"{d}/History", "sqlite");
        Write($"{d}/Login Data", "passwords");
        WriteWebData($"{d}/Web Data");
        Write($"{d}/Favicons", "icons");
        Write($"{d}/Local Storage/leveldb/000003.log", "site");
        Write($"{d}/Network/Cookies", "cookies");
        Write($"{d}/Google Profile Picture.png", "png");
        Write($"{d}/Accounts/Avatar Images/123", "png");
        Write($"{d}/Account Web Data", "account");
        Write($"{d}/Cache/Cache_Data/f_000001", "cache");
        Write($"{d}/Preferences", """
            { "homepage": "https://example.com",
              "extensions": { "settings": {
                "aaaa": { "location": 1, "manifest": { "name": "__MSG_appName__", "version": "1.0" }, "path": "aaaa\\1.0_0" },
                "bbbb": { "location": 1, "manifest": { "name": "Beta", "version": "2.0" }, "path": "bbbb\\2.0_0", "disable_reasons": [1] },
                "pdf":  { "location": 5, "manifest": { "name": "PDF", "version": "1" } } } } }
            """);
        Write($"{d}/Secure Preferences", """{ "protection": { "macs": {} } }""");
        Write($"{d}/Extensions/aaaa/1.0_0/manifest.json", """{ "name": "__MSG_appName__", "version": "1.0", "default_locale": "en" }""");
        Write($"{d}/Extensions/aaaa/1.0_0/_locales/zh_CN/messages.json", """{ "appName": { "message": "阿尔法" } }""");
        Write($"{d}/Extensions/bbbb/2.0_0/manifest.json", """{ "name": "Beta", "version": "2.0" }""");
        Write($"{d}/Local Extension Settings/aaaa/000003.log", "ext-data");
        Write($"{d}/Local Extension Settings/aaaa/LOCK", "");
        Write($"{d}/Local Extension Settings/bbbb/000003.log", "ext-data-b");
    }

    private void WriteWebData(string rel)
    {
        var path = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE keywords(id INTEGER PRIMARY KEY, keyword TEXT, url TEXT);
            CREATE TABLE autofill(name TEXT, value TEXT);
            CREATE TABLE token_service(service TEXT, encrypted_token BLOB);
            INSERT INTO keywords(keyword, url) VALUES ('bd', 'https://www.baidu.com/s?wd={searchTerms}');
            INSERT INTO autofill(name, value) VALUES ('email', 'a@b.c');
            INSERT INTO token_service(service, encrypted_token) VALUES ('oauth', X'0102');
            """;
        cmd.ExecuteNonQuery();
    }

    public void Write(string rel, string content)
    {
        var path = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
