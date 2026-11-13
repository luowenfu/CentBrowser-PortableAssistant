namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>只读包装流，读取时汇报累计字节数，给 HttpClient 上传用。</summary>
public sealed class ProgressStream(Stream inner, IProgress<long>? progress) : Stream
{
    private long _total;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        Track(await inner.ReadAsync(buffer, ct));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        Track(await inner.ReadAsync(buffer.AsMemory(offset, count), ct));

    private int Track(int read)
    {
        _total += read;
        progress?.Report(_total);
        return read;
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
