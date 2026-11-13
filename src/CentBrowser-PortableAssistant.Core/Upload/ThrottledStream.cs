using System.Diagnostics;

namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>按每秒多少字节放行的只读包装流。读得比限速快就等一会儿，用来给慢速备份让出带宽。</summary>
public sealed class ThrottledStream(Stream inner, int kbytesPerSecond) : Stream
{
    private readonly double _bytesPerMs = kbytesPerSecond * 1024 / 1000.0;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private long _total;

    /// <summary>不限速时原样返回，不用多包一层。</summary>
    public static Stream Wrap(Stream inner, int kbytesPerSecond) =>
        kbytesPerSecond > 0 ? new ThrottledStream(inner, kbytesPerSecond) : inner;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        var wait = Pace(read);
        if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await inner.ReadAsync(buffer, ct);
        var wait = Pace(read);
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    /// <summary>算这一段读完之后该等多久。对面本来就比限速慢时重新计时，免得攒出一波猛传。</summary>
    private TimeSpan Pace(int read)
    {
        if (read <= 0) return TimeSpan.Zero;
        _total += read;
        var delay = _total / _bytesPerMs - _watch.Elapsed.TotalMilliseconds;
        if (delay >= 1) return TimeSpan.FromMilliseconds(delay);
        if (delay < -1000)
        {
            _total = 0;
            _watch.Restart();
        }
        return TimeSpan.Zero;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
