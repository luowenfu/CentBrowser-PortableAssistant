using System.Text.Json.Nodes;
using CentBrowser_PortableAssistant.Core.Backup;
using Microsoft.Data.Sqlite;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Tests;

public sealed class CollectAndPackTests : IDisposable
{
    private readonly FakeBrowser _browser = new();
    private readonly string _staging = Path.Combine(Path.GetTempPath(), "cbpa-stage-" + Guid.NewGuid().ToString("N")[..8]);

    private static BackupPlan OnlyProfile1WithAlpha() => new()
    {
        AllProfiles = false,
        ProfileDirs = ["Profile 1"],
        Profiles = { ["Profile 1"] = new ProfileOptions { AllExtensions = false, ExtensionIds = ["aaaa"] } },
    };

    [Fact]
    public void ReadsRegisteredProfilesAndLocalizedExtensionNames()
    {
        var profiles = ProfileReader.ReadProfiles(_browser.Install.UserDataDir);
        Assert.Equal(["Profile 1", "Profile 2"], profiles.Select(p => p.DirName));

        var exts = ProfileReader.ReadExtensions(_browser.Path_("User Data/Profile 1"));
        Assert.Equal(["Beta", "阿尔法"], exts.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.False(exts.Single(e => e.Id == "bbbb").Enabled);
        Assert.DoesNotContain(exts, e => e.Id == "pdf");
    }

    [Fact]
    public void EmptySelectionChecksEveryoneUsedToday()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var older = now - 3 * 24 * 3600;
        _browser.Write("User Data/Local State",
            "{ \"profile\": { \"info_cache\": {" +
            $" \"Profile 1\": {{ \"name\": \"甲\", \"active_time\": {now} }}," +
            $" \"Profile 2\": {{ \"name\": \"乙\", \"active_time\": {older} }} }}," +
            " \"last_active_profiles\": [\"Profile 2\"], \"last_used\": \"Profile 2\" } }");
        var selected = CollectPlanner.SelectedProfiles(_browser.Install, new BackupPlan());
        Assert.Equal(["Profile 1"], selected);

        _browser.Write("User Data/Local State",
            "{ \"profile\": { \"info_cache\": {" +
            $" \"Profile 1\": {{ \"name\": \"甲\", \"active_time\": {now} }}," +
            $" \"Profile 2\": {{ \"name\": \"乙\", \"active_time\": {now} }} }}," +
            " \"last_active_profiles\": [\"Profile 2\"], \"last_used\": \"Profile 2\" } }");
        selected = CollectPlanner.SelectedProfiles(_browser.Install, new BackupPlan());
        Assert.Equal(["Profile 1", "Profile 2"], selected.Order(StringComparer.Ordinal));

        var yesterday = now - 24 * 3600;
        _browser.Write("User Data/Local State",
            "{ \"profile\": { \"info_cache\": {" +
            $" \"Profile 1\": {{ \"name\": \"甲\", \"active_time\": {yesterday} }}," +
            $" \"Profile 2\": {{ \"name\": \"乙\", \"active_time\": {yesterday} }} }}," +
            " \"last_active_profiles\": [\"Profile 1\"], \"last_used\": \"Profile 1\" } }");
        selected = CollectPlanner.SelectedProfiles(_browser.Install, new BackupPlan());
        Assert.Equal(["Profile 1", "Profile 2"], selected.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ExplicitEmptySelectionStaysEmpty()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _browser.Write("User Data/Local State",
            "{ \"profile\": { \"info_cache\": {" +
            $" \"Profile 1\": {{ \"name\": \"甲\", \"active_time\": {now} }}," +
            $" \"Profile 2\": {{ \"name\": \"乙\", \"active_time\": {now} }} }}," +
            " \"last_used\": \"Profile 1\" } }");
        var plan = new BackupPlan { ProfilesChosen = true, ProfileDirs = [] };
        var selected = CollectPlanner.SelectedProfiles(_browser.Install, plan);
        Assert.Empty(selected);
    }

    [Fact]
    public void EmptySelectionFallsBackToLastUsedProfile()
    {
        var selected = CollectPlanner.SelectedProfiles(_browser.Install, new BackupPlan());
        Assert.Equal(["Profile 2"], selected);
    }

    [Fact]
    public void CollectKeepsWhitelistAndDropsSecrets()
    {
        var plan = OnlyProfile1WithAlpha();
        var profiles = CollectPlanner.SelectedProfiles(_browser.Install, plan);
        var items = CollectPlanner.Plan(_browser.Install, plan, profiles);
        Collector.Collect(_browser.Install, plan, profiles, items, _staging, CancellationToken.None);

        string S(string rel) => Path.Combine(_staging, "CentBrowser", rel);
        Assert.True(File.Exists(S("chrome.exe")));
        Assert.True(File.Exists(S("1.2.3.4/cbportable")));
        Assert.True(File.Exists(S("Dictionaries/en.bdic")));
        Assert.False(File.Exists(S("wipe.bat")));
        Assert.True(File.Exists(S("User Data/First Run")));
        Assert.Equal(0, new FileInfo(S("User Data/First Run")).Length);
        Assert.Equal("1.2.3.4", File.ReadAllText(S("User Data/Last Version")));
        Assert.True(File.Exists(S("User Data/Profile 1/Bookmarks")));
        Assert.True(File.Exists(S("User Data/Profile 1/Favicons")));
        Assert.True(File.Exists(S("User Data/Profile 1/Local Storage/leveldb/000003.log")));
        Assert.True(File.Exists(S("User Data/Profile 1/Web Data")));
        Assert.Equal(1, CountRows(S("User Data/Profile 1/Web Data"), "keywords"));
        Assert.Equal(0, CountRows(S("User Data/Profile 1/Web Data"), "autofill"));
        Assert.Equal(0, CountRows(S("User Data/Profile 1/Web Data"), "token_service"));
        Assert.True(File.Exists(S("User Data/Profile 1/Google Profile Picture.png")));
        Assert.True(File.Exists(S("User Data/Profile 1/Accounts/Avatar Images/123")));
        Assert.False(File.Exists(S("User Data/Profile 1/Account Web Data")));
        Assert.True(File.Exists(S("User Data/Profile 1/History")));
        Assert.True(File.Exists(S("User Data/Profile 1/Extensions/aaaa/1.0_0/manifest.json")));
        Assert.True(File.Exists(S("User Data/Profile 1/Local Extension Settings/aaaa/000003.log")));

        Assert.False(Directory.Exists(S("User Data/Profile 2")));
        Assert.False(Directory.Exists(S("User Data/ShaderCache")));
        Assert.False(Directory.Exists(S("User Data/Profile 1/Extensions/bbbb")));
        Assert.False(Directory.Exists(S("User Data/Profile 1/Local Extension Settings/bbbb")));
        Assert.False(File.Exists(S("User Data/Profile 1/Local Extension Settings/aaaa/LOCK")));
        foreach (var secret in new[] { "Login Data", "Network/Cookies", "Cache" })
        {
            Assert.False(File.Exists(S($"User Data/Profile 1/{secret}")) || Directory.Exists(S($"User Data/Profile 1/{secret}")), secret);
        }

        var localState = JsonNode.Parse(File.ReadAllText(S("User Data/Local State")))!;
        Assert.Null(localState["os_crypt"]);
        Assert.Equal("1.2.3.4", localState["browser"]!["suppress_default_browser_prompt_for_version"]!.GetValue<string>());
        Assert.Equal("Profile 1", localState["profile"]!["last_used"]!.GetValue<string>());

        var prefs = JsonNode.Parse(File.ReadAllText(S("User Data/Profile 1/Preferences")))!;
        var ids = prefs["extensions"]!["settings"]!.AsObject().Select(kv => kv.Key).ToList();
        Assert.Equal(["aaaa", "pdf"], ids);
    }

    [Fact]
    public void ContentSizesFollowTheFilesEachOptionPacks()
    {
        var dir = _browser.Path_("User Data/Profile 1");
        var size = ProfilePaths.MeasureContent(dir);

        Assert.Equal(new FileInfo(_browser.Path_("User Data/Profile 1/Bookmarks")).Length, size.Bookmarks);
        Assert.Equal(new FileInfo(_browser.Path_("User Data/Profile 1/Favicons")).Length, size.Favicons);
        Assert.Equal(new FileInfo(_browser.Path_("User Data/Profile 1/History")).Length, size.History);
        Assert.True(size.Settings >= new FileInfo(_browser.Path_("User Data/Profile 1/Preferences")).Length);
        Assert.True(size.WebData > 0);
        Assert.Equal(new FileInfo(_browser.Path_("User Data/Profile 1/Local Storage/leveldb/000003.log")).Length, size.SiteData);
        Assert.Equal(
            new FileInfo(_browser.Path_("User Data/Profile 1/Login Data")).Length
            + new FileInfo(_browser.Path_("User Data/Profile 1/Network/Cookies")).Length
            + new FileInfo(_browser.Path_("User Data/Profile 1/Account Web Data")).Length,
            size.Secrets);
        Assert.Equal(0, size.WebSide);
        Assert.Equal(0, size.SharedExtensions);
    }

    [Fact]
    public void ProgramSizeSkipsScriptsAndCountsTheBrowser()
    {
        var size = CollectPlanner.MeasureProgram(_browser.Install);
        var expected = new FileInfo(_browser.Path_("chrome.exe")).Length
            + new FileInfo(_browser.Path_("1.2.3.4/chrome.dll")).Length
            + new FileInfo(_browser.Path_("Dictionaries/en.bdic")).Length;
        Assert.Equal(expected, size);
    }

    [Fact]
    public void SecretsKeepMachineKeyAndFullWebData()
    {
        var plan = OnlyProfile1WithAlpha();
        plan.Profiles["Profile 1"].IncludeSecrets = true;
        var profiles = CollectPlanner.SelectedProfiles(_browser.Install, plan);
        var items = CollectPlanner.Plan(_browser.Install, plan, profiles);
        Collector.Collect(_browser.Install, plan, profiles, items, _staging, CancellationToken.None);

        string S(string rel) => Path.Combine(_staging, "CentBrowser", rel);
        Assert.True(File.Exists(S("User Data/Profile 1/Login Data")));
        Assert.True(File.Exists(S("User Data/Profile 1/Network/Cookies")));
        Assert.True(File.Exists(S("User Data/Profile 1/Account Web Data")));
        Assert.Equal(1, CountRows(S("User Data/Profile 1/Web Data"), "autofill"));
        Assert.Equal(1, CountRows(S("User Data/Profile 1/Web Data"), "keywords"));

        var localState = JsonNode.Parse(File.ReadAllText(S("User Data/Local State")))!;
        Assert.Equal("k", localState["os_crypt"]!["encrypted_key"]!.GetValue<string>());
    }

    [Fact]
    public void EmptyPasswordIsAllowedUntilSecretsAreSelected()
    {
        var plan = new BackupPlan { RequirePassword = false, ProfileDirs = ["Profile 1"] };
        Assert.False(plan.NeedsArchivePassword());
        plan.Profiles["Profile 1"] = new ProfileOptions { IncludeSecrets = true };
        Assert.True(plan.NeedsArchivePassword());
    }

    private static long CountRows(string db, string table)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void ExtensionDataFollowsEachExtension()
    {
        BackupPlan Plan(bool allData, params string[] dataIds) => new()
        {
            IncludeProgram = false,
            AllProfiles = false,
            ProfileDirs = ["Profile 1"],
            Profiles =
            {
                ["Profile 1"] = new ProfileOptions
                {
                    AllExtensions = false,
                    ExtensionIds = ["aaaa"],
                    IncludeExtensionData = allData,
                    ExtensionDataIds = [.. dataIds],
                },
            },
        };

        var withData = CollectPlanner.Plan(_browser.Install, Plan(false, "aaaa"), ["Profile 1"])
            .Select(i => i.Dest.Replace('\\', '/')).ToList();
        var without = CollectPlanner.Plan(_browser.Install, Plan(false), ["Profile 1"])
            .Select(i => i.Dest.Replace('\\', '/')).ToList();

        Assert.Contains("CentBrowser/User Data/First Run", withData);
        Assert.Contains("CentBrowser/User Data/Last Version", withData);
        Assert.Contains(withData, d => d.Contains("Extensions/aaaa"));
        Assert.Contains(withData, d => d.Contains("Local Extension Settings/aaaa"));
        Assert.Contains(without, d => d.Contains("Extensions/aaaa"));
        Assert.DoesNotContain(without, d => d.Contains("Local Extension Settings/aaaa"));
    }

    [Fact]
    public void EachProfileUsesItsOwnOptions()
    {
        var plan = new BackupPlan
        {
            AllProfiles = true,
            IncludeProgram = false,
            Profiles = { ["Profile 2"] = new ProfileOptions { IncludeBookmarks = false, IncludeExtensions = false } },
        };
        var profiles = CollectPlanner.SelectedProfiles(_browser.Install, plan);
        var dests = CollectPlanner.Plan(_browser.Install, plan, profiles).Select(i => i.Dest.Replace('\\', '/')).ToList();

        Assert.Contains("CentBrowser/User Data/Profile 1/Bookmarks", dests);
        Assert.Contains(dests, d => d.StartsWith("CentBrowser/User Data/Profile 1/Extensions/"));
        Assert.Contains("CentBrowser/User Data/Profile 2/History", dests);
        Assert.DoesNotContain("CentBrowser/User Data/Profile 2/Bookmarks", dests);
        Assert.DoesNotContain(dests, d => d.StartsWith("CentBrowser/User Data/Profile 2/Extensions/"));
    }

    [Fact]
    public void FingerprintIgnoresProfilesNotInPlan()
    {
        var plan = OnlyProfile1WithAlpha();
        string Fp() => Fingerprint.Compute(_browser.Install, plan,
            CollectPlanner.Plan(_browser.Install, plan, CollectPlanner.SelectedProfiles(_browser.Install, plan)));

        var before = Fp();
        _browser.Write("User Data/Profile 2/Bookmarks", "{\"changed\":true}");
        Assert.Equal(before, Fp());

        _browser.Write("User Data/Profile 1/Bookmarks", "{\"changed\":true}");
        Assert.NotEqual(before, Fp());
    }

    [Theory]
    [InlineData("pa ss\"word!", ArchiveFormats.Sfx | ArchiveFormats.SevenZip | ArchiveFormats.Zip)]
    [InlineData("中文 密码\"", ArchiveFormats.Sfx | ArchiveFormats.SevenZip)]
    public async Task PacksWithPassword(string password, ArchiveFormats formats)
    {
        var plan = OnlyProfile1WithAlpha();
        var profiles = CollectPlanner.SelectedProfiles(_browser.Install, plan);
        Collector.Collect(_browser.Install, plan, profiles, CollectPlanner.Plan(_browser.Install, plan, profiles), _staging, CancellationToken.None);

        var output = _staging + "-out";
        var files = await Packer.PackAsync(_staging, output, "test", formats, ZipEncryption.Aes256, password, null, CancellationToken.None);
        try
        {
            Assert.Equal(formats.HasFlag(ArchiveFormats.Zip) ? 3 : 2, files.Count);
            foreach (var f in files)
            {
                await SevenZipTool.RunAsync(["t", f], password, null, CancellationToken.None);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    SevenZipTool.RunAsync(["t", f], "wrong", null, CancellationToken.None));
            }

            var exe = File.ReadAllBytes(files[0]);
            var sfx = File.ReadAllBytes(SevenZipTool.SfxPath);
            var archive = File.ReadAllBytes(files[1]);
            Assert.Equal(sfx.Concat(archive), exe);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task PacksWithoutPassword()
    {
        var plan = OnlyProfile1WithAlpha();
        var profiles = CollectPlanner.SelectedProfiles(_browser.Install, plan);
        Collector.Collect(_browser.Install, plan, profiles, CollectPlanner.Plan(_browser.Install, plan, profiles), _staging, CancellationToken.None);

        var output = _staging + "-out";
        var files = await Packer.PackAsync(_staging, output, "test", ArchiveFormats.SevenZip, ZipEncryption.Aes256, null, null, CancellationToken.None);
        try
        {
            var archive = files.Single();
            await SevenZipTool.RunAsync(["t", archive], null, null, CancellationToken.None);
            var listing = await ListArchive(archive);
            Assert.DoesNotContain("Encrypted = +", listing);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<string> ListArchive(string archive)
    {
        SevenZipTool.EnsureExtracted();
        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var psi = new System.Diagnostics.ProcessStartInfo(SevenZipTool.ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = utf8,
        };
        foreach (var a in new[] { "l", "-slt", archive }) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var text = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return text;
    }

    [Fact]
    public async Task ZipRejectsNonAsciiPassword() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Packer.PackAsync(_staging, _staging + "-out", "t", ArchiveFormats.Zip, ZipEncryption.Aes256, "中文", null, CancellationToken.None));

    [Fact]
    public void CollectPercentIgnoresLockAndCountsJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "cbpa-pct-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var dir = Path.Combine(root, "data");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "keep.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(dir, "LOCK"), new byte[9000]);
            var json = Path.Combine(root, "Local State");
            File.WriteAllText(json, "{}");
            var jsonLen = new FileInfo(json).Length;
            var bin = Path.Combine(root, "note.bin");
            File.WriteAllBytes(bin, new byte[jsonLen]);

            var seen = new List<int>();
            var copied = Collector.Collect(_browser.Install, new BackupPlan(), [], [
                new CollectItem(dir, "data", CollectKind.Copy),
            ], Path.Combine(root, "stage"), CancellationToken.None, new InlineProgress(seen));
            Assert.Equal(1000, copied);
            Assert.Equal([0, 100], seen);

            seen.Clear();
            copied = Collector.Collect(_browser.Install, new BackupPlan(), [], [
                new CollectItem(bin, "note.bin", CollectKind.Copy),
                new CollectItem(json, "User Data/Local State", CollectKind.LocalState),
            ], Path.Combine(root, "stage2"), CancellationToken.None, new InlineProgress(seen));
            Assert.Equal(jsonLen, copied);
            Assert.Equal([0, 50, 100], seen);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CollectPercentMovesWhileCopyingALargeFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "cbpa-big-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var bin = Path.Combine(root, "big.bin");
            Directory.CreateDirectory(root);
            File.WriteAllBytes(bin, new byte[1024 * 1024 + 1]);
            var seen = new List<int>();
            Collector.Collect(_browser.Install, new BackupPlan(), [], [
                new CollectItem(bin, "big.bin", CollectKind.Copy),
            ], Path.Combine(root, "stage"), CancellationToken.None, new InlineProgress(seen));
            Assert.Equal(0, seen[0]);
            Assert.Equal(100, seen[^1]);
            Assert.True(seen.Count >= 3);
            Assert.True(seen.Zip(seen.Skip(1), (a, b) => b > a).All(x => x));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class InlineProgress(List<int> seen) : IProgress<int>
    {
        public void Report(int value) => seen.Add(value);
    }

    public void Dispose()
    {
        _browser.Dispose();
        if (Directory.Exists(_staging)) Directory.Delete(_staging, recursive: true);
    }
}
