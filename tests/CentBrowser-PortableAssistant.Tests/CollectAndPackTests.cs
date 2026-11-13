using System.Text.Json.Nodes;
using CentBrowser_PortableAssistant.Core.Backup;
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
        foreach (var secret in new[] { "Login Data", "Web Data", "Network/Cookies", "Cache" })
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
    public async Task ZipRejectsNonAsciiPassword() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Packer.PackAsync(_staging, _staging + "-out", "t", ArchiveFormats.Zip, ZipEncryption.Aes256, "中文", null, CancellationToken.None));

    public void Dispose()
    {
        _browser.Dispose();
        if (Directory.Exists(_staging)) Directory.Delete(_staging, recursive: true);
    }
}
