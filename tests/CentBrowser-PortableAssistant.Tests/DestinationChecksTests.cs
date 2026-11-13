using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class DestinationChecksTests
{
    [Fact]
    public void BackupStopsWhenAddressIsEmpty()
    {
        var target = new UploadTarget { Name = "坚果云", Address = "" };
        var message = BackupRunner.Validate(ReadyPlan(target), new AppConfig { Targets = [target] });
        Assert.Equal("“坚果云”还没填地址", message);
    }

    [Fact]
    public void DisabledDestinationIsSkipped()
    {
        var off = new UploadTarget { Name = "旧盘", Address = "", Disabled = true };
        var on = new UploadTarget { Name = "坚果云", Address = "https://dav.example.com/dav/" };
        var plan = ReadyPlan(on);
        plan.TargetIds = [off.Id, on.Id];
        Assert.Null(BackupRunner.Validate(plan, new AppConfig { Targets = [off, on] }));
    }

    [Fact]
    public void BackupStopsWhenEveryDestinationIsDisabled()
    {
        var target = new UploadTarget { Name = "坚果云", Address = "https://dav.example.com/dav/", Disabled = true };
        var message = BackupRunner.Validate(ReadyPlan(target), new AppConfig { Targets = [target] });
        Assert.Equal("“方案1”的目的地都停用了", message);
    }

    [Fact]
    public async Task WebDavChinesePathIsEncoded()
    {
        var target = new UploadTarget
        {
            Kind = TargetKind.WebDav,
            Address = "https://192.168.1.10/backup/测试",
            UserName = "name",
        };
        await using var uploader = (WebDavUploader)UploaderFactory.Create(target);
        var field = typeof(WebDavUploader).GetField("_base", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var baseUrl = Assert.IsType<string>(field!.GetValue(uploader));
        Assert.DoesNotContain(baseUrl, c => c > 127);
        Assert.Contains("%E6%B5%8B%E8%AF%95", baseUrl);
    }

    [Fact]
    public void BackupAllowsFilledAddress()
    {
        var target = new UploadTarget { Name = "坚果云", Address = "https://dav.example.com/dav/" };
        Assert.Null(BackupRunner.Validate(ReadyPlan(target), new AppConfig { Targets = [target] }));
    }

    [Fact]
    public void SmbLocationUsesServerAndShare()
    {
        var target = new UploadTarget { Kind = TargetKind.Smb, Address = "192.168.1.10", RemoteDir = "/backup/cent" };
        Assert.Equal(@"\\192.168.1.10\backup\cent", target.Location);
    }

    [Fact]
    public void SmbLocationKeepsUncAddress()
    {
        var target = new UploadTarget { Kind = TargetKind.Smb, Address = @"\\nas\share", RemoteDir = "/Cent" };
        Assert.Equal(@"\\nas\share\Cent", target.Location);
    }

    [Fact]
    public void SmbLocationNeedsNoSeparateDir()
    {
        var target = new UploadTarget { Kind = TargetKind.Smb, Address = @"\\192.168.1.10\home", RemoteDir = "" };
        Assert.Equal(@"\\192.168.1.10\home", target.Location);
        UploaderFactory.Check(target);
    }

    [Fact]
    public void BadDestinationInputUsesShortChinese()
    {
        Assert.Equal("要填用户名", Message(TargetKind.Ftp, "ftp.example.com", "/"));
        Assert.Equal("地址要以 https:// 开头", Message(TargetKind.WebDav, "啊但是到底是", "/"));
        const string shareHint = @"服务器要写到共享名，例如 \\192.168.1.10\home";
        Assert.Equal(shareHint, Message(TargetKind.Smb, "nas", ""));
        Assert.Equal("要填用户名", UploadErrors.Brief(new ArgumentException("UserName can't be empty", "user")));
        Assert.Equal(@"要填完整路径，例如 D:\备份", Message(TargetKind.LocalFolder, "asdas454dsa", ""));
        Assert.Equal("还没选文件夹", Message(TargetKind.LocalFolder, "", ""));
        Assert.Equal("还没填地址", Message(TargetKind.WebDav, "  ", ""));
        Assert.Equal("还没填服务器", Message(TargetKind.Ftp, "", ""));
        Assert.Equal("还没填服务器", Message(TargetKind.Smb, "", "/backup"));
        Assert.Equal(shareHint, Message(TargetKind.Smb, @"\\192.168.1.10", ""));
        Assert.Equal("服务器填主机名或 IP", Message(TargetKind.Ftp, "ftp://ftp.example.com", "/"));
        Assert.Equal("远程目录要写成 /目录", Message(TargetKind.Ftp, "ftp.example.com", @"\backup"));
        Assert.Equal(@"路径要用反斜杠，例如 D:\备份", Message(TargetKind.LocalFolder, "D:/备份", ""));
    }

    [Fact]
    public async Task LocalFolderTestRequiresExistingFolder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "centbrowser-missing-" + Guid.NewGuid().ToString("N"));
        var target = new UploadTarget { Kind = TargetKind.LocalFolder, Address = missing };
        await using var uploader = UploaderFactory.Create(target);
        var ex = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => uploader.TestAsync(CancellationToken.None));
        Assert.Equal("文件夹不存在", UploadErrors.Brief(ex));
    }

    [Fact]
    public async Task LocalUploadDoesNotCreateMissingFolder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "centbrowser-nocreate-" + Guid.NewGuid().ToString("N"));
        var src = Path.GetTempFileName();
        try
        {
            await using var uploader = new LocalFolderUploader(missing);
            var ex = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => uploader.UploadAsync(src, "a.bin", null, CancellationToken.None));
            Assert.Equal("文件夹不存在", UploadErrors.Brief(ex));
            Assert.False(Directory.Exists(missing));
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
        }
    }

    [Fact]
    public async Task LocalFolderTestWritesThenRemovesProbe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "centbrowser-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var target = new UploadTarget { Kind = TargetKind.LocalFolder, Address = dir };
            await using var uploader = UploaderFactory.Create(target);
            await uploader.TestAsync(CancellationToken.None);
            Assert.False(File.Exists(Path.Combine(dir, "centbrowser-write-test.txt")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LocalUploadWritesFinishedNameAndDropsPartial()
    {
        var dir = Path.Combine(Path.GetTempPath(), "centbrowser-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, "src.bin");
        await File.WriteAllBytesAsync(src, [1, 2, 3]);
        try
        {
            await using var uploader = new LocalFolderUploader(dir);
            var remote = await uploader.UploadAsync(src, "done.bin", null, CancellationToken.None);
            Assert.Equal(Path.Combine(dir, "done.bin"), remote);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(remote));
            Assert.False(File.Exists(remote + ".part"));

            var blocked = Path.Combine(dir, "blocked.bin");
            await File.WriteAllBytesAsync(blocked, [9]);
            var renamed = await uploader.UploadAsync(src, "blocked.bin", null, CancellationToken.None);
            Assert.Equal(Path.Combine(dir, "blocked (2).bin"), renamed);
            Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(blocked));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(renamed));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task UniqueNameUsesTheNextFreeNumber()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.exe", "a (2).exe" };
        var name = await UniqueName.Pick("a.exe", (candidate, _) => Task.FromResult(taken.Contains(candidate)), CancellationToken.None);
        Assert.Equal("a (3).exe", name);
    }

    [Fact]
    public void UnknownUploadErrorKeepsTheOriginalText()
    {
        Assert.Equal("地址里有未编码的特殊字符", UploadErrors.Brief(new HttpRequestException("Request headers must contain only ASCII characters.")));
        Assert.Equal("传了一半连接断了", UploadErrors.Brief(new HttpRequestException("The response ended prematurely.")));
        Assert.Equal("连接失败：certificate named example", UploadErrors.Brief(new HttpRequestException("certificate named example")));
    }

    private static string Message(TargetKind kind, string address, string dir)
    {
        var target = new UploadTarget { Kind = kind, Address = address, RemoteDir = dir, Name = "测试" };
        return UploadErrors.Brief(Assert.Throws<ArgumentException>(() => UploaderFactory.Create(target)));
    }

    private static BackupPlan ReadyPlan(UploadTarget target) => new()
    {
        PasswordProtected = SecretProtector.Protect("secret"),
        AllProfiles = true,
        TargetIds = [target.Id],
        NameTemplate = "{方案}_{时间}",
    };
}
