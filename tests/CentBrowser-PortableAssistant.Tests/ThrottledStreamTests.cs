using System.Diagnostics;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class ThrottledStreamTests
{
    [Fact]
    public void WrapSkipsThrottleWhenUnlimited()
    {
        using var source = new MemoryStream(new byte[16]);
        Assert.Same(source, ThrottledStream.Wrap(source, 0));
        Assert.IsType<ThrottledStream>(ThrottledStream.Wrap(source, 512));
    }

    [Fact]
    public async Task ReadingTakesTheTimeTheLimitImplies()
    {
        // 256 KB 按 512 KB/s 至少要半秒
        using var source = new MemoryStream(new byte[256 * 1024]);
        await using var stream = new ThrottledStream(source, 512);
        var buffer = new byte[32 * 1024];

        var watch = Stopwatch.StartNew();
        while (await stream.ReadAsync(buffer) > 0) { }

        Assert.True(watch.ElapsedMilliseconds >= 400, $"只用了 {watch.ElapsedMilliseconds} 毫秒");
    }
}
